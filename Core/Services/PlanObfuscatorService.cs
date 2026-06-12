using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public static class PlanObfuscatorService
    {
        public static XDocument ObfuscatePlan(XDocument originalPlan)
        {
            var maskedDoc = new XDocument(originalPlan);
            var dict = new Dictionary<string, string>();
            
            foreach (var elem in maskedDoc.Descendants())
            {
                var attrsToMask = new[] { "Table", "Schema", "Database", "Column", "Index" };
                foreach (var attr in attrsToMask)
                {
                    var a = elem.Attribute(attr);
                    if (a != null)
                    {
                        string coreVal = a.Value.Trim('[', ']');
                        if (string.IsNullOrWhiteSpace(coreVal) || coreVal.StartsWith("@") || coreVal.StartsWith("Expr") || coreVal.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                            continue;
                            
                        string key = $"{attr}:{coreVal.ToLower()}";
                        if (!dict.TryGetValue(key, out string? masked))
                        {
                            masked = $"Masked_{attr}_{dict.Count + 1}";
                            dict[key] = masked;
                        }
                        a.Value = a.Value.StartsWith("[") ? $"[{masked}]" : masked;
                    }
                }
                
                var stmtAttr = elem.Attribute("StatementText");
                if (stmtAttr != null)
                {
                    stmtAttr.Value = "-- 本语句已被 SqlXmlAnalyzer 引擎脱敏保护 (Statement Obfuscated) --";
                }
            }
            return maskedDoc;
        }
    }
}
