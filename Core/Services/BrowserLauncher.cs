using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class BrowserLauncher
    {
        private readonly TemporaryFileManager _temporaryFileManager;

        public BrowserLauncher(TemporaryFileManager temporaryFileManager)
        {
            _temporaryFileManager = temporaryFileManager;
        }

        public void OpenFile(string filePath)
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }

        public void OpenFolder(string folderPath)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", folderPath)
            {
                UseShellExecute = true
            });
        }

        public string OpenMermaid(string mermaidCode)
        {
            string nonce = Guid.NewGuid().ToString("N");
            string html = CreateMermaidHtml(mermaidCode, nonce);

            string temporaryPath = _temporaryFileManager.CreatePath("Mermaid", ".html");
            File.WriteAllText(temporaryPath, html, Encoding.UTF8);
            OpenFile(temporaryPath);
            return temporaryPath;
        }

        public static string CreateMermaidHtml(string mermaidCode, string nonce)
        {
            string encodedDiagram = WebUtility.HtmlEncode(mermaidCode ?? string.Empty);
            return $$"""
                <!DOCTYPE html>
                <html lang="zh-CN">
                <head>
                  <meta charset="utf-8">
                  <meta http-equiv="Content-Security-Policy"
                        content="default-src 'none'; script-src 'nonce-{{nonce}}' https://cdn.jsdelivr.net; style-src 'unsafe-inline'; img-src data:; connect-src 'none'; object-src 'none'; base-uri 'none'">
                  <script nonce="{{nonce}}" src="https://cdn.jsdelivr.net/npm/mermaid@10.9.3/dist/mermaid.min.js"></script>
                </head>
                <body>
                  <pre class="mermaid">{{encodedDiagram}}</pre>
                  <script nonce="{{nonce}}">
                    mermaid.initialize({
                      startOnLoad: true,
                      securityLevel: 'strict',
                      flowchart: { htmlLabels: false }
                    });
                  </script>
                </body>
                </html>
                """;
        }
    }
}
