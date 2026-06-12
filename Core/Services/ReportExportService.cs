using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xceed.Document.NET;
using Xceed.Words.NET;

namespace SqlXmlAnalyzer.Core.Services
{
    public static class ReportExportService
    {
        static ReportExportService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public static void ExportToPdf(string filePath, string title, string content, string? imagePath = null)
        {
            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(QuestPDF.Helpers.Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial));

                    page.Header()
                        .Text(title)
                        .SemiBold().FontSize(20).FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(x =>
                        {
                            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                            {
                                x.Item().PaddingBottom(1, Unit.Centimetre).Image(imagePath);
                            }
                            x.Item().Text(content);
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("生成于: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | SqlXmlAnalyzer 智能诊断引擎 - 第 ");
                            x.CurrentPageNumber();
                            x.Span(" 页，共 ");
                            x.TotalPages();
                            x.Span(" 页");
                        });
                });
            })
            .GeneratePdf(filePath);
        }

        public static void ExportToWord(string filePath, string title, string content, string? imagePath = null)
        {
            using (var document = DocX.Create(filePath))
            {
                // Title
                var titleFormat = new Formatting();
                titleFormat.FontFamily = new Xceed.Document.NET.Font("Arial");
                titleFormat.Size = 20D;
                titleFormat.Bold = true;
                document.InsertParagraph(title, false, titleFormat)
                        .Alignment = Alignment.center;

                document.InsertParagraph("----------------------------------------------------------------------");
                document.InsertParagraph();

                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    var img = document.AddImage(imagePath);
                    var picture = img.CreatePicture();
                    
                    // 适配宽度
                    if (picture.Width > 600)
                    {
                        double ratio = 600.0 / picture.Width;
                        picture.Width = 600;
                        picture.Height = (int)(picture.Height * ratio);
                    }
                    
                    document.InsertParagraph().AppendPicture(picture).Alignment = Alignment.center;
                    document.InsertParagraph();
                }

                // Content
                var contentFormat = new Formatting();
                contentFormat.FontFamily = new Xceed.Document.NET.Font("Consolas");
                contentFormat.Size = 10D;
                document.InsertParagraph(content, false, contentFormat);

                // Save
                document.Save();
            }
        }
    }
}
