using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace SqlXmlAnalyzer
{
    public static class PhysicalOpToIconMapper
    {
        private static readonly Brush RedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
        private static readonly Brush BlueBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3498DB"));
        private static readonly Brush PurpleBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9B59B6"));
        private static readonly Brush OrangeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F39C12"));
        private static readonly Brush GrayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D"));
        private static readonly Brush LightGrayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6"));

        private static readonly Geometry DefaultGeometry = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
        private static readonly Brush DefaultBrush = GrayBrush;

        // Common geometries (Material Design Paths)
        private static readonly Geometry ScanGeo = Geometry.Parse("M4 4h16v16H4V4zm2 4v10h12V8H6zM4 2h16c1.1 0 2 .9 2 2v16c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2z");
        private static readonly Geometry SeekGeo = Geometry.Parse("M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z");
        private static readonly Geometry JoinGeo = Geometry.Parse("M15 16c0-3.31-2.69-6-6-6S3 12.69 3 16s2.69 6 6 6 6-2.69 6-6zm-6 4c-2.21 0-4-1.79-4-4s1.79-4 4-4 4 1.79 4 4-1.79 4-4 4zm10-14c-3.31 0-6 2.69-6 6 0 .42.06.82.14 1.21.63-.58 1.39-.99 2.22-1.15.52-2.15 2.45-3.77 4.74-3.77 2.65 0 4.8 2.15 4.8 4.8 0 2.29-1.62 4.22-3.77 4.74-.16.83-.57 1.59-1.15 2.22.39.08.79.14 1.21.14 3.31 0 6-2.69 6-6s-2.69-6-6-6z");
        private static readonly Geometry SortGeo = Geometry.Parse("M3 18h6v-2H3v2zM3 6v2h18V6H3zm0 7h12v-2H3v2z");
        private static readonly Geometry ComputeGeo = Geometry.Parse("M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-6 2h2v2h-2V5zm0 4h2v2h-2V9zm-4-4h2v2H9V5zm0 4h2v2H9V9zm-4-4h2v2H5V5zm0 4h2v2H5V9zm14 10H5v-6h14v6zm0-8h-2V5h2v6z");
        private static readonly Geometry ParallelGeo = Geometry.Parse("M14 4l2.29 2.29-2.88 2.88 1.42 1.42 2.88-2.88L20 10V4h-6zm-4 0H4v6l2.29-2.29 4.71 4.7V20h2v-8.41l-5.29-5.3L10 4z");

        private static readonly Dictionary<string, (Geometry Geometry, Brush Brush)> Mapping =
            new Dictionary<string, (Geometry, Brush)>(StringComparer.OrdinalIgnoreCase)
            {
                // Scans -> Red
                ["Table Scan"] = (ScanGeo, RedBrush),
                ["Index Scan"] = (ScanGeo, RedBrush),
                ["Clustered Index Scan"] = (ScanGeo, RedBrush),
                ["Columnstore Index Scan"] = (ScanGeo, RedBrush),

                // Seeks -> Blue
                ["Index Seek"] = (SeekGeo, BlueBrush),
                ["Clustered Index Seek"] = (SeekGeo, BlueBrush),
                ["Key Lookup"] = (SeekGeo, BlueBrush),
                ["RID Lookup"] = (SeekGeo, BlueBrush),

                // Joins -> Purple
                ["Nested Loops"] = (JoinGeo, PurpleBrush),
                ["Merge Join"] = (JoinGeo, PurpleBrush),
                ["Hash Match"] = (JoinGeo, PurpleBrush),

                // Sort/Spool -> Gray
                ["Sort"] = (SortGeo, GrayBrush),
                ["Table Spool"] = (Geometry.Parse("M12 2C6.48 2 2 3.79 2 6v12c0 2.21 4.48 4 10 4s10-1.79 10-4V6c0-2.21-4.48-4-10-4zm0 18c-4.42 0-8-1.42-8-3.17V15c1.86 1.05 4.75 1.67 8 1.67s6.14-.62 8-1.67v1.83c0 1.75-3.58 3.17-8 3.17zm0-5c-4.42 0-8-1.42-8-3.17V10c1.86 1.05 4.75 1.67 8 1.67s6.14-.62 8-1.67v1.83c0 1.75-3.58 3.17-8 3.17zm0-5c-4.42 0-8-1.42-8-3.17S7.58 3.67 12 3.67s8 1.42 8 3.17-3.58 3.16-8 3.16z"), GrayBrush),
                ["Index Spool"] = (SortGeo, GrayBrush),

                // Compute Scalar -> Light Gray
                ["Compute Scalar"] = (ComputeGeo, LightGrayBrush),
                ["Sequence Project"] = (ComputeGeo, LightGrayBrush),

                // Parallelism -> Orange
                ["Parallelism"] = (ParallelGeo, OrangeBrush),
                ["Exchange"] = (ParallelGeo, OrangeBrush),
                ["Gather Streams"] = (ParallelGeo, OrangeBrush),
                ["Repartition Streams"] = (ParallelGeo, OrangeBrush),
                ["Distribute Streams"] = (ParallelGeo, OrangeBrush),

                // Aggregations
                ["Stream Aggregate"] = (ComputeGeo, PurpleBrush),
                ["Hash Aggregate"] = (ComputeGeo, PurpleBrush),
                
                // Other
                ["Filter"] = (Geometry.Parse("M10 18h4v-2h-4v2zM3 6v2h18V6H3zm3 7h12v-2H6v2z"), GrayBrush)
            };

        public static (Geometry Geometry, Brush Brush) Map(string physicalOp)
        {
            if (Mapping.TryGetValue(physicalOp, out var result))
                return result;

            // Fallback rules
            if (physicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase)) return (ScanGeo, RedBrush);
            if (physicalOp.Contains("Seek", StringComparison.OrdinalIgnoreCase)) return (SeekGeo, BlueBrush);
            if (physicalOp.Contains("Join", StringComparison.OrdinalIgnoreCase)) return (JoinGeo, PurpleBrush);
            if (physicalOp.Contains("Sort", StringComparison.OrdinalIgnoreCase)) return (SortGeo, GrayBrush);
            
            return (DefaultGeometry, DefaultBrush);
        }
    }
}
