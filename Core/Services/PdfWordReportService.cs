using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PdfWordReportService
    {
        private readonly TemporaryFileManager _temporaryFileManager;

        public PdfWordReportService(TemporaryFileManager temporaryFileManager)
        {
            _temporaryFileManager = temporaryFileManager;
        }

        public void Export(
            string extension,
            string outputPath,
            string title,
            string content,
            FrameworkElement? imageElement = null)
        {
            string? temporaryImagePath = null;
            try
            {
                temporaryImagePath = CaptureElement(imageElement);
                switch (extension.ToLowerInvariant())
                {
                    case "pdf":
                        ReportExportService.ExportToPdf(
                            outputPath,
                            title,
                            content,
                            temporaryImagePath);
                        break;
                    case "docx":
                        ReportExportService.ExportToWord(
                            outputPath,
                            title,
                            content,
                            temporaryImagePath);
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Unsupported report extension: {extension}");
                }
            }
            finally
            {
                _temporaryFileManager.Delete(temporaryImagePath);
            }
        }

        private string? CaptureElement(FrameworkElement? element)
        {
            if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return null;
            }

            double width = element.ActualWidth;
            double height = element.ActualHeight;
            var bitmap = new RenderTargetBitmap(
                (int)Math.Round(width),
                (int)Math.Round(height),
                96d,
                96d,
                PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                context.DrawRectangle(
                    Brushes.White,
                    null,
                    new Rect(0, 0, width, height));
                context.DrawRectangle(
                    new VisualBrush(element),
                    null,
                    new Rect(0, 0, width, height));
            }
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string temporaryPath = _temporaryFileManager.CreatePath("Graph", ".png");
            using var stream = new FileStream(temporaryPath, FileMode.Create);
            encoder.Save(stream);
            return temporaryPath;
        }
    }
}
