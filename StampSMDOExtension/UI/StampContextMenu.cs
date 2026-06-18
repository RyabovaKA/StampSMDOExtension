using Ascon.Pilot.SDK;
using Ascon.Pilot.SDK.Menu;
using StampSMDOExtension.Models;
using StampSMDOExtension.Utilities;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

using PilotDataObject = Ascon.Pilot.SDK.IDataObject;

namespace StampSMDOExtension.UI
{
    [Export(typeof(IMenu<GraphicLayerElementContext>))]
    public class StampContextMenu : IMenu<GraphicLayerElementContext>, IMouseLeftClickListener
    {
        private const string MenuRotateCW = "stamp_ctx_rotate_cw";
        private const string MenuRotateCCW = "stamp_ctx_rotate_ccw";
        private const string MenuMove = "stamp_ctx_move";

        private readonly IObjectModifier _modifier;
        private readonly IFileProvider _fileProvider;
        private readonly IXpsViewer _xpsViewer;
        private readonly IObjectsRepository _repository;

        private GraphicLayerElementContext _pendingMove;

        [ImportingConstructor]
        public StampContextMenu(IObjectModifier modifier, IFileProvider fileProvider, IXpsViewer xpsViewer, IObjectsRepository repository)
        {
            _modifier = modifier;
            _fileProvider = fileProvider;
            _xpsViewer = xpsViewer;
            _repository = repository;
        }

        public void Build(IMenuBuilder builder, GraphicLayerElementContext context)
        {
            builder.AddItem(MenuRotateCW, builder.Count).WithHeader("Повернуть +90° (по часовой)");
            builder.AddItem(MenuRotateCCW, builder.Count).WithHeader("Повернуть −90° (против часовой)");
        }

        public async void OnMenuItemClick(string name, GraphicLayerElementContext context)
        {
            if (name == MenuRotateCW) await RotateStampAsync(context, +90.0);
            else if (name == MenuRotateCCW) await RotateStampAsync(context, -90.0);
            else if (name == MenuMove) StartMove(context);
        }

        private void StartMove(GraphicLayerElementContext context)
        {
            _pendingMove = context;
            _xpsViewer.UnsubscribeLeftMouseClick(this);
            _xpsViewer.SubscribeLeftMouseClick(this);
        }

        public void OnLeftMouseButtonClick(XpsRenderClickPointContext pointContext)
        {
            if (_pendingMove == null) return;
            try
            {
                MoveStamp(_pendingMove, pointContext.ClickPoint, pointContext.PageNumber);
            }
            finally
            {
                _xpsViewer.UnsubscribeLeftMouseClick(this);
                _pendingMove = null;
            }
        }

        private async Task RotateStampAsync(GraphicLayerElementContext context, double deltaDegrees)
        {
            var workDoc = await StampObjectLoader.Load(_repository, context.DataObject.Id) ?? context.DataObject;

            var descriptorFile = workDoc.Files.FirstOrDefault(f =>
                f.Name.Contains(context.ElementId.ToString()) &&
                !f.Name.Contains("content"));

            if (descriptorFile == null) return;

            XDocument xmlDoc;
            using (var stream = _fileProvider.OpenRead(descriptorFile))
            {
                xmlDoc = XDocument.Load(stream);
            }

            var angleEl = xmlDoc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "Angle");
            double currentAngle = 0.0;
            if (angleEl != null) double.TryParse(angleEl.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out currentAngle);

            double newAngle = (currentAngle + deltaDegrees) % 360;
            if (newAngle < 0) newAngle += 360;

            UpdateOrCreateElement(xmlDoc, "Angle", newAngle.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SaveAndApply(workDoc, descriptorFile, xmlDoc);
        }

        private void MoveStamp(GraphicLayerElementContext context, Point newPoint, int newPage)
        {
            var doc = context.DataObject;
            var descriptorFile = doc.Files.FirstOrDefault(f =>
                f.Name.Contains(context.ElementId.ToString()) &&
                !f.Name.Contains("content"));

            if (descriptorFile == null) return;

            XDocument xmlDoc;
            using (var stream = _fileProvider.OpenRead(descriptorFile))
            {
                xmlDoc = XDocument.Load(stream);
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            UpdateOrCreateElement(xmlDoc, "OffsetX", newPoint.X.ToString(culture));
            UpdateOrCreateElement(xmlDoc, "OffsetY", newPoint.Y.ToString(culture));
            UpdateOrCreateElement(xmlDoc, "PageNumber", newPage.ToString(culture));

            SaveAndApply(doc, descriptorFile, xmlDoc);
        }

        private void UpdateOrCreateElement(XDocument doc, string localName, string value)
        {
            var element = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
            if (element != null) element.Value = value;
            else doc.Root.Add(new XElement(doc.Root.GetDefaultNamespace() + localName, value));
        }

        private void SaveAndApply(PilotDataObject doc, IFile descriptorFile, XDocument xmlDoc)
        {
            var xmlStream = new MemoryStream();
            xmlDoc.Save(xmlStream);
            xmlStream.Position = 0;

            var builder = _modifier.Edit(doc);
            builder.AddOrReplaceFile(descriptorFile.Name, xmlStream, descriptorFile, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);
            _modifier.Apply();
        }
    }
}