using Ascon.Pilot.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StampSMDOExtension.Utilities
{
    public class StampImageResult : IDisposable
    {
        public MemoryStream Stream { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public void Dispose() => Stream?.Dispose();
    }

    public static class StampGraphicsBuilder
    {
        public const double DPI_FACTOR = 3.125;

        public static StampImageResult CreateCombinedStampStream(
            string fio, string position, string sender,
            string docDate, string docNumber, DateTime stampDateTime,
            double sealScale, double signatureScale, double sealAngleDeg,
            string signatureImagePath)
        {
            var sealBmp = TryLoadLocalImage(@"Icons\stamp.png");
            var sigBmp = TryLoadAbsoluteImage(signatureImagePath);

            double sealW = sealBmp != null ? sealBmp.PixelWidth * sealScale : 0;
            double sealH = sealBmp != null ? sealBmp.PixelHeight * sealScale : 0;

            double sigW, sigH;
            var typefaceSig = new Typeface("Segoe UI");
            const double sigMsgFont = 8.5;
            const double pixelsPerDip = 1.0;
            FormattedText missingSigFt = null;

            if (sigBmp != null)
            {
                sigW = sigBmp.PixelWidth * signatureScale;
                sigH = sigBmp.PixelHeight * signatureScale;
            }
            else
            {
                missingSigFt = new FormattedText("НЕТ ПОДПИСИ", CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typefaceSig, sigMsgFont, Brushes.Red, pixelsPerDip);
                sigW = missingSigFt.Width;
                sigH = missingSigFt.Height;
            }

            const double fixedSigAreaH = 15.0;

            using (var infoRaw = CreateStampBitmapStream(fio, position, sender, docDate, docNumber, fixedSigAreaH))
            {
                var infoBmp = new BitmapImage();
                infoBmp.BeginInit();
                infoBmp.StreamSource = infoRaw;
                infoBmp.CacheOption = BitmapCacheOption.OnLoad;
                infoBmp.EndInit();
                infoBmp.Freeze();

                double infoW = infoBmp.Width;
                double infoH = infoBmp.Height;

                // Угол поворота печати
                double actualSealAngleDeg = -20.0;
                double rad = actualSealAngleDeg * Math.PI / 180.0;

                // --- ЛОГИЧЕСКИЕ КООРДИНАТЫ ЭЛЕМЕНТОВ (ОТНОСИТЕЛЬНО (0,0)) ---

                // Текст и рамка начинаются в логическом нуле (0,0) холста
                double infoBaseX = 0;
                double infoBaseY = 0;

                // ПЕЧАТЬ (stamp): Позиционируем относительно НИЖНЕЙ границы рамки
                // sealBaseX остается исходным (центрирование по левой границе рамки)
                double sealCenterX = infoBaseX;
                double sealBaseX = sealCenterX - (sealW / 2.0);

                // sealBaseY: Смещаем ВВЕРХ от нижней границы черной рамки (infoBaseY + infoH).
                // Отступаем от нижнего края рамки 10 пикселей вверх.
                double indentUp = 10.0;
                double sealBaseY = (infoBaseY + infoH) - sealH - indentUp;

                // Подпись и дата (позиция не меняется относительно рамки)
                // ИЗМЕНЕНО: Отступ уменьшен с 80.0 до 40.0 для сдвига подписи и даты левее
                double sigX = infoBaseX + 40.0;

                double baseReservedY = infoBaseY + infoH - fixedSigAreaH - 3.0;
                double sigDrawY = baseReservedY + (fixedSigAreaH - sigH) / 2.0;
                var dateFt = new FormattedText(stampDateTime.ToString("dd.MM.yyyy"),
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typefaceSig, 10.0, Brushes.Black, pixelsPerDip);
                double dateX = sigX + sigW + 15.0;
                double dateY = baseReservedY + (fixedSigAreaH - dateFt.Height) / 2.0;

                // --- ТОЧНЫЙ РАСЧЕТ ГРАНИЦ ДЛЯ ОБРЕЗКИ ПУСТОТЫ (Minimize General Picture Border) ---

                // Изначально границы соответствуют рамке (0,0 -> infoW, infoH)
                double minX = 0, minY = 0, maxX = infoW, maxY = infoH;

                if (sealBmp != null)
                {
                    // Центр печати для вращения
                    double sCenterX = sealBaseX + sealW / 2.0;
                    double sCenterY = sealBaseY + sealH / 2.0;

                    // Углы печати до поворота (logical coordinates)
                    Point[] corners = {
                        new Point(sealBaseX, sealBaseY),
                        new Point(sealBaseX + sealW, sealBaseY),
                        new Point(sealBaseX, sealBaseY + sealH),
                        new Point(sealBaseX + sealW, sealBaseY + sealH)
                    };

                    // Вращаем каждый угол и расширяем границы canvas, если повернутая печать выходит за рамку
                    foreach (var p in corners)
                    {
                        double tx = p.X - sCenterX;
                        double ty = p.Y - sCenterY;
                        double rx = tx * Math.Cos(rad) - ty * Math.Sin(rad) + sCenterX;
                        double ry = tx * Math.Sin(rad) + ty * Math.Cos(rad) + sCenterY;

                        minX = Math.Min(minX, rx);
                        minY = Math.Min(minY, ry);
                        maxX = Math.Max(maxX, rx);
                        maxY = Math.Max(maxY, ry);
                    }
                }

                // Учитываем границы подписи и даты (хотя они обычно внутри, для надежности)
                minX = Math.Min(minX, sigX);
                minY = Math.Min(minY, Math.Min(sigDrawY, dateY));
                maxX = Math.Max(maxX, dateX + dateFt.Width);
                maxY = Math.Max(maxY, Math.Max(sigDrawY + sigH, dateY + dateFt.Height));

                // Итоговый размер холста (минимально возможный без transparent padding)
                double canvasW = Math.Ceiling(maxX - minX);
                double canvasH = Math.Ceiling(maxY - minY);

                // Смещения для финальной отрисовки (crop logic)
                double offsetX = -minX;
                double offsetY = -minY;

                // Базовые точки отрисовки на canvas
                double finalInfoX = infoBaseX + offsetX;
                double finalInfoY = infoBaseY + offsetY;
                double finalSealX = sealBaseX + offsetX;
                double finalSealY = sealBaseY + offsetY;
                double finalSigX = sigX + offsetX;
                double finalSigY = sigDrawY + offsetY;
                double finalDateX = dateX + offsetX;
                double finalDateY = dateY + offsetY;

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    TextOptions.SetTextRenderingMode(visual, TextRenderingMode.ClearType);
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);


                    if (sealBmp != null)
                    {
                        dc.PushOpacity(0.8);
                        dc.PushTransform(new RotateTransform(actualSealAngleDeg, finalSealX + sealW / 2.0, finalSealY + sealH / 2.0));
                        dc.DrawImage(sealBmp, new Rect(finalSealX, finalSealY, sealW, sealH));
                        dc.Pop();
                        dc.Pop();
                    }

                    if (sigBmp != null)
                        dc.DrawImage(sigBmp, new Rect(finalSigX, finalSigY, sigW, sigH));
                    else if (missingSigFt != null)
                        dc.DrawText(missingSigFt, new Point(finalSigX, finalSigY));

                    dc.DrawImage(infoBmp, new Rect(finalInfoX, finalInfoY, infoW, infoH));
                    dc.DrawText(dateFt, new Point(finalDateX, finalDateY));
                }

                var bmp = new RenderTargetBitmap((int)(canvasW * DPI_FACTOR), (int)(canvasH * DPI_FACTOR), 96.0 * DPI_FACTOR, 96.0 * DPI_FACTOR, PixelFormats.Pbgra32);
                bmp.Render(visual);

                var ms = new MemoryStream();
                new PngBitmapEncoder { Frames = { BitmapFrame.Create(bmp) } }.Save(ms);
                ms.Position = 0;

                return new StampImageResult { Stream = ms, Width = canvasW, Height = canvasH };
            }
        }

        private static MemoryStream CreateStampBitmapStream(string fio, string position, string sender, string docDate, string docNumber, double sigAreaH)
        {
            var bottomLineParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(sender)) bottomLineParts.Add(sender.Trim());
            if (!string.IsNullOrWhiteSpace(docDate)) bottomLineParts.Add($"от {docDate.Trim()}");
            if (!string.IsNullOrWhiteSpace(docNumber)) bottomLineParts.Add($"№{docNumber.Trim()}");

            string senderAndRequisites = string.Join(" ", bottomLineParts);

            var topLines = new List<string>
            {
                "УП \"Институт Витебскгражданпроект\"",
                string.IsNullOrWhiteSpace(position) ? $"Я, {fio}" : $"Я, {fio}, {position},",
                "удостоверяю, что настоящий документ является копией электронного документа"
            };

            if (!string.IsNullOrWhiteSpace(senderAndRequisites)) topLines.Add(senderAndRequisites);

            const int width = 400;
            const int padding = 3;
            const double rowPadding = 0.5;
            const double fontSize = 10.0;
            const double pixelsPerDip = 1.0;
            const double textToSignatureGap = 10.0;

            var borderPen = new Pen(Brushes.Black, 1.0);
            var contentWidth = width - 2 * padding;

            FormattedText MakeText(string s, bool isBold, double maxW)
            {
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, isBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                return new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, pixelsPerDip)
                {
                    MaxTextWidth = maxW,
                    LineHeight = fontSize * 1.1
                };
            }

            var measured = topLines.Select((l, i) => MakeText(l, i == 0, contentWidth)).ToList();
            var topTextH = measured.Sum(t => t.Height);
            var gapsH = (topLines.Count - 1) * rowPadding * 2;

            var height = (int)Math.Ceiling(padding + topTextH + gapsH + textToSignatureGap + sigAreaH + 4);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                TextOptions.SetTextRenderingMode(visual, TextRenderingMode.ClearType);
                dc.DrawRectangle(null, borderPen, new Rect(0.5, 0.5, width - 1, height - 1));
                double y = padding;
                for (int i = 0; i < measured.Count; i++)
                {
                    dc.DrawText(measured[i], new Point(padding, y));
                    y += measured[i].Height + (i < measured.Count - 1 ? rowPadding * 2 : 0);
                }
            }

            var bitmap = new RenderTargetBitmap((int)(width * DPI_FACTOR), (int)(height * DPI_FACTOR), 96.0 * DPI_FACTOR, 96.0 * DPI_FACTOR, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var stream = new MemoryStream();
            new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } }.Save(stream);
            stream.Position = 0;
            return stream;
        }

        private static BitmapImage TryLoadLocalImage(string fileName)
        {
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", fileName);
                if (!File.Exists(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private static BitmapImage TryLoadAbsoluteImage(string fullPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(fullPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}