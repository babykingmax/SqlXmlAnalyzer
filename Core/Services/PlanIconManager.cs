using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SqlXmlAnalyzer.Core.Services
{
    public static class PlanIconManager
    {
        public static string? FindIconPath(string op)
        {
            string? iconFile = GetIconFileName(op);
            if (iconFile == null)
            {
                return null;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] searchPaths = new[]
            {
                Path.Combine(baseDir, "ssms_icons", iconFile),
                Path.Combine(baseDir, "..", "..", "..", "ssms_icons", iconFile),
                Path.Combine(Directory.GetCurrentDirectory(), "ssms_icons", iconFile)
            };

            foreach (string path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        public static string? GetIconFileName(string op)
        {
            if (string.IsNullOrEmpty(op))
            {
                return null;
            }

            string opLower = op.ToLowerInvariant().Trim();
            string name = opLower.Replace(" ", "-").Replace("_", "-");

            if (opLower.Contains("hash match") || opLower.Contains("hash"))
            {
                name = "hash-match";
            }
            else if (opLower.Contains("merge join") || opLower.Contains("merge"))
            {
                name = "merge-join";
            }
            else if (opLower.Contains("nested loops") || opLower.Contains("loops") || opLower.Contains("loop"))
            {
                name = "nested-loops";
            }
            else if (opLower.Contains("parallelism") || opLower.Contains("exchange"))
            {
                name = "parallelism";
            }
            else if (opLower.Contains("stream aggregate") || opLower.Contains("hash aggregate") || opLower.Contains("aggregate"))
            {
                name = "aggregate";
            }
            else if (opLower.Contains("compute scalar") || opLower.Contains("compute"))
            {
                name = "compute-scalar";
            }
            else if (opLower.Contains("key lookup"))
            {
                name = "key-lookup";
            }
            else if (opLower.Contains("clustered index scan"))
            {
                name = "clustered-index-scan";
            }
            else if (opLower.Contains("clustered index seek"))
            {
                name = "clustered-index-seek";
            }
            else if (opLower.Contains("index scan") || opLower.Contains("nonclustered index scan"))
            {
                name = "nonclustered-index-scan";
            }
            else if (opLower.Contains("index seek") || opLower.Contains("nonclustered index seek"))
            {
                name = "nonclustered-index-seek";
            }
            else if (opLower.Contains("table scan"))
            {
                name = "table-scan";
            }
            else if (opLower.Contains("sort"))
            {
                name = "sort";
            }
            else if (opLower.Contains("filter"))
            {
                name = "filter";
            }
            else if (opLower.Contains("top"))
            {
                name = "top";
            }
            else if (opLower.Contains("table-valued function") || opLower.Contains("table valued function"))
            {
                name = "table-valued-function";
            }
            else if (opLower.Contains("union"))
            {
                name = "union";
            }
            else if (opLower.Contains("delete"))
            {
                name = "delete";
            }
            else if (opLower.Contains("insert"))
            {
                name = "insert";
            }
            else if (opLower.Contains("update"))
            {
                name = "update";
            }

            return $"icon-{name}.png";
        }

        public static ImageSource? GetIcon(string op)
        {
            string? path = FindIconPath(op);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
