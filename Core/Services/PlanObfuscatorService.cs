using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public static class PlanObfuscatorService
    {
        public static XDocument ObfuscatePlan(XDocument originalPlan)
        {
            if (originalPlan == null) throw new ArgumentNullException(nameof(originalPlan));
            
            var maskedDoc = new XDocument(originalPlan);
            var dict = new Dictionary<string, string>();
            
            foreach (var elem in maskedDoc.Descendants())
            {
                // 1. 掩码物理库表对象名
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
                
                // 2. 清洗或模糊化 ScalarString 中的物理对象
                var scalarStrAttr = elem.Attribute("ScalarString");
                if (scalarStrAttr != null)
                {
                    scalarStrAttr.Value = "[Masked Formula / Predicate Expression]";
                }

                // 3. 彻底清除包含个人隐私/业务机密的参数运行时值和编译时值
                var compileVal = elem.Attribute("ParameterCompiledValue");
                if (compileVal != null) compileVal.Value = "[MASKED_PARAM_VAL]";

                var runVal = elem.Attribute("ParameterRuntimeValue");
                if (runVal != null) runVal.Value = "[MASKED_PARAM_VAL]";

                // 4. 清理 StatementText
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
