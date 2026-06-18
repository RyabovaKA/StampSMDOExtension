using Ascon.Pilot.SDK;
using Ascon.Pilot.SDK.Menu;
using Ascon.Pilot.SDK.Toolbar;
using StampSMDOExtension.Models;
using StampSMDOExtension.Utilities;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Serialization;
using System.Reflection;

using PilotDataObject = Ascon.Pilot.SDK.IDataObject;

namespace StampSMDOExtension.UI
{
    [Export(typeof(IToolbar<XpsRenderContext>))]
    public class StampToolbar : IToolbar<XpsRenderContext>, IMouseLeftClickListener
    {
        private const double SEAL_SCALE = 0.320;
        private const double SIGNATURE_SCALE = 0.320;
        private const string StampButtonName = "AddStamp";
        private const string StampButtonHint = "Заверить копию документа (доступно после подписания)";

        private readonly IObjectsRepository _repository;
        private readonly IObjectModifier _modifier;
        private readonly IXpsViewer _xpsViewer;

        private bool _waitingClickForStamp;
        private IToolbarBuilder _toolbarBuilder;
        private IDisposable _docSubscription;
        private Guid _currentDocId;

        [ImportingConstructor]
        public StampToolbar(IObjectsRepository repository, IObjectModifier modifier, IXpsViewer xpsViewer)
        {
            _repository = repository;
            _modifier = modifier;
            _xpsViewer = xpsViewer;
        }

        private Guid GetStableGuid(int page, int positionId)
        {
            string key = $"Stamp_Slot_P{page}_Pos{positionId}";
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                return new Guid(hash);
            }
        }

        public void Build(IToolbarBuilder builder, XpsRenderContext context)
        {
            _toolbarBuilder = builder;
            var doc = context.DataObject;

            if (doc != null && doc.Id != _currentDocId)
            {
                _docSubscription?.Dispose();
                _currentDocId = doc.Id;
                _docSubscription = _repository.SubscribeObjects(new[] { doc.Id }).Subscribe(updatedDoc =>
                {
                    if (updatedDoc.Id != _currentDocId || updatedDoc.State != DataState.Loaded) return;
                    UpdateStampButtonState(updatedDoc);
                });
            }

            if (!builder.ItemNames.Any(n => n == StampButtonName))
            {
                builder.AddSeparator(builder.Count);
                var button = builder.AddToggleButtonItem(StampButtonName, builder.Count);
                StyleStampButton(button, IsStampEnabled(doc), _waitingClickForStamp);
            }
        }

        private static void StyleStampButton(IToolbarToggleButtonItemBuilder button, bool isEnabled, bool isChecked)
        {
            button.WithIsChecked(isChecked)
                  .WithIcon(IconLoader.GetIcon("stamp-ok.svg"))
                  .WithShowHeader(true)
                  .WithIsEnabled(isEnabled)
                  .WithHint(StampButtonHint);
        }

        private bool IsStampEnabled(PilotDataObject doc)
        {
            if (doc == null) return false;
            var currentPerson = _repository.GetCurrentPerson();
            int currentPositionId = currentPerson?.MainPosition?.Position ?? 0;

            return currentPositionId != 0 &&
                   doc.ActualFileSnapshot.Files.Any(f => f.SignatureRequests.Any(sr => sr.PositionId == currentPositionId && sr.Signs.Any()));
        }

        private void UpdateStampButtonState(PilotDataObject updatedDoc)
        {
            if (_toolbarBuilder == null) return;
            try
            {
                var button = _toolbarBuilder.ReplaceToggleButtonItem(StampButtonName);
                StyleStampButton(button, IsStampEnabled(updatedDoc), _waitingClickForStamp);
            }
            catch { }
        }

        public void OnToolbarItemClick(string name, XpsRenderContext context)
        {
            if (name != StampButtonName) return;
            _waitingClickForStamp = !_waitingClickForStamp;
            _xpsViewer.UnsubscribeLeftMouseClick(this);
            if (_waitingClickForStamp) _xpsViewer.SubscribeLeftMouseClick(this);
            UpdateStampButtonState(context.DataObject);
        }

        public async void OnLeftMouseButtonClick(XpsRenderClickPointContext pointContext)
        {
            if (!_waitingClickForStamp) return;

            _xpsViewer.UnsubscribeLeftMouseClick(this);
            _waitingClickForStamp = false;
            UpdateStampButtonState(pointContext.DataObject);

            try
            {
                await AddStampToDocument(pointContext.DataObject, pointContext.ClickPoint, pointContext.PageNumber);
            }
            catch { }
        }

        private async Task AddStampToDocument(PilotDataObject doc, Point clickPoint, int pageNumber)
        {
            var currentPerson = _repository.GetCurrentPerson();
            var positionId = currentPerson?.MainPosition?.Position ?? 0;
            Guid stableId = GetStableGuid(pageNumber, positionId);
            string metadataFileName = GraphicLayerElementConstants.GRAPHIC_LAYER_ELEMENT + stableId.ToString();

       
            var fio = currentPerson?.DisplayName ?? string.Empty;
            var positionTitle = _repository.GetOrganisationUnit(positionId)?.Title ?? string.Empty;
            var sender = await StampMetadataHelper.ResolveRefBookName(_repository, doc, "sender", "name");
            var docDate = StampMetadataHelper.GetAttributeDateString(doc, "date") ?? DateTime.Now.ToString("dd.MM.yyyy");
            var docNumber = StampMetadataHelper.GetAttributeString(doc, "number") ?? string.Empty;
            double sealAngle = new Random().NextDouble() * 40.0 - 20.0;

            string signatureImagePath = GetSignaturePathFromOtherExtension();

            try
            {

                if (!string.IsNullOrEmpty(signatureImagePath) && !File.Exists(signatureImagePath))
                    signatureImagePath = string.Empty;

                using (var result = StampGraphicsBuilder.CreateCombinedStampStream(
                    fio, positionTitle, sender, docDate, docNumber, DateTime.Now,
                    SEAL_SCALE, SIGNATURE_SCALE, sealAngle, signatureImagePath))
                {
                    if (result == null || result.Stream == null) return;

                    var builder = _modifier.Edit(doc);

                    var existingFile = doc.Files.FirstOrDefault(f => f.Name == metadataFileName);
                    if (existingFile != null)
                        builder.RemoveFile(existingFile.Id);

                    AddGraphicElement(builder, result.Stream, clickPoint, pageNumber, positionId, result.Width, result.Height, metadataFileName, stableId);

                    _modifier.Apply();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при создании штампа: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetSignaturePathFromOtherExtension()
        {
            try
            {
          
                var targetAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.StartsWith("Ascon.Pilot.SDK.GraphicLayerSample", StringComparison.OrdinalIgnoreCase));

                if (targetAssembly == null) return string.Empty;

                var settingsType = targetAssembly.GetType("Ascon.Pilot.SDK.GraphicLayerSample.Properties.Settings");
                if (settingsType == null) return string.Empty;

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

                var defaultInstance = settingsType.GetProperty("Default", flags)?.GetValue(null);
                if (defaultInstance == null) return string.Empty;

                var itemProp = settingsType.GetProperty("Item", flags, null, typeof(object), new[] { typeof(string) }, null);
                var path = itemProp?.GetValue(defaultInstance, new object[] { "Path" }) as string;

                if (string.IsNullOrEmpty(path))
                    path = settingsType.GetProperty("Path", flags)?.GetValue(defaultInstance) as string;

                return path ?? string.Empty;
            }
            catch
            {
                return string.Empty; 
            }
        }

        private static void AddGraphicElement(IObjectBuilder builder, MemoryStream imageStream, Point clickPoint, int pageNumber, int positionId, double width, double height, string metadataFileName, Guid stableId)
        {
            var contentId = Guid.NewGuid();
            double left = clickPoint.X - (width / 2.0);
            double top = clickPoint.Y - (height / 2.0);

            var element = new StampElement
            {
                ElementId = stableId,
                ContentId = contentId,
                OffsetX = left,
                OffsetY = top,
                Width = width,
                Height = height,
                Scale = new Point(1.0, 1.0),
                Angle = 0.0,
                PositionId = positionId,
                PageNumber = pageNumber,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                ContentType = GraphicLayerElementConstants.BITMAP,
                IsFloating = true,
                CornerPoint = new Point(0.5, 0.5)
            };

            XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
            ns.Add("", "");
            var xmlStream = new MemoryStream();
            new XmlSerializer(typeof(StampElement)).Serialize(xmlStream, element, ns);
            xmlStream.Position = 0;

            builder.AddFile(metadataFileName, xmlStream, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);

            imageStream.Position = 0;
            builder.AddFile(GraphicLayerElementConstants.GRAPHIC_LAYER_ELEMENT_CONTENT + contentId,
                            imageStream, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);
        }
    }
}