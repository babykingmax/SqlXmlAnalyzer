using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    public sealed class NodeDetail
    {
        public string NodeId { get; set; } = string.Empty;
        public string PhysicalOp { get; set; } = string.Empty;
        public double OwnCost { get; set; }
        public double SubtreeCost { get; set; }
    }

    public static class PlanDiagnosticAnalyzer
    {
        public static Core.Rules.PlanDiagnosticReport AnalyzeDetailed(XDocument doc, XNamespace ns, string? configPath = null,
            Core.Models.PlanDocument? identities = null, Core.Services.DocumentCapabilities? capabilities = null,
            System.Threading.CancellationToken cancellationToken = default, Core.Diagnostics.IUnexpectedErrorReporter? unexpectedErrors = null)
        {
            var engine = new Core.Rules.RuleEngine(configPath, unexpectedErrors);
            engine.RegisterDefaultRules();
            return engine.AnalyzePlanDetailed(doc, ns, identities, capabilities, cancellationToken);
        }

        public static List<SqlXmlAnalyzer.Core.Rules.AnalysisResult> AnalyzePlan(XDocument doc, XNamespace ns, string? configPath = null,
            Core.Models.PlanDocument? identities = null)
        {
            if (doc?.Root == null)
            {
                return new List<SqlXmlAnalyzer.Core.Rules.AnalysisResult>();
            }

            var ruleEngine = new SqlXmlAnalyzer.Core.Rules.RuleEngine(configPath);
            ruleEngine.RegisterDefaultRules();
            return ruleEngine.AnalyzePlan(doc, ns, identities);
        }

        public static string GenerateDiagnosticReport(XDocument doc, XNamespace ns)
        {
            if (doc?.Root == null) return "⚠️ 无效的执行计划 XML 结构。";
            try { return Core.Rules.DiagnosticTextFormatter.Format(AnalyzeDetailed(doc, ns)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                string detail = Core.Diagnostics.ExceptionPolicy.Describe(exception, "PlanDiagnosticAnalyzer.GenerateDiagnosticReport");
                return "执行计划诊断未完成：" + detail;
            }
        }
        public static List<XElement> GetDirectChildRelOps(XElement element, XNamespace ns)
        {
            var children = new List<XElement>();
            if (element == null) return children;

            try
            {
                var stack = new Stack<XElement>();

                var childList = element.Elements().ToList();
                for (int i = childList.Count - 1; i >= 0; i--)
                {
                    var ch = childList[i];
                    if (ch != null) stack.Push(ch);
                }

                while (stack.Count > 0)
                {
                    var child = stack.Pop();
                    if (child == null || child.Name.Namespace != ns ||
                        child.Name.LocalName is "InternalInfo" or "Statements" or "QueryPlan") continue;

                    if (child.Name == ns + "RelOp")
                    {
                        children.Add(child);
                    }
                    else
                    {
                        var innerList = child.Elements().ToList();
                        for (int i = innerList.Count - 1; i >= 0; i--)
                        {
                            var ich = innerList[i];
                            if (ich != null) stack.Push(ich);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("PlanDiagnosticAnalyzer.GetDirectChildRelOps", ex);
            }
            return children;
        }

        public static double ParseDouble(string? val)
        {
            if (string.IsNullOrEmpty(val)) return 0.0;
            if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double res)) return res;
            return 0.0;
        }

        public static List<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion> ExtractMissingIndexes(XDocument doc, XNamespace ns)
        {
            ArgumentNullException.ThrowIfNull(doc);
            ArgumentNullException.ThrowIfNull(ns);
            try { return ExtractMissingIndexesCore(doc, ns); }
            catch (Exception exception)
            {
                Core.Diagnostics.ExceptionPolicy.Describe(exception, "PlanDiagnosticAnalyzer.ExtractMissingIndexes");
                throw;
            }
        }

        private static List<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion> ExtractMissingIndexesCore(XDocument doc, XNamespace ns)
        {
            var results = new List<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion>();
            var identities = Core.Services.PlanIdentityAdapter.GetDocument(doc);
            var queryPlans = identities == null ? Enumerable.Empty<XElement>()
                : identities.QueryPlans.Select(plan => identities.GetQueryPlanSource(plan.Key)!);
            var missingIndexGroups = queryPlans.SelectMany(plan => plan.Elements(ns + "MissingIndexes")
                .Elements(ns + "MissingIndexGroup"));
            foreach (var mig in missingIndexGroups)
            {
                if (mig == null) continue;
                double impact = ParseDouble(mig.Attribute("Impact")?.Value);
                double? capturedImpact = Core.NumericParser.TryParseInvariantDouble((string?)mig.Attribute("Impact"), out double value)
                    && double.IsFinite(value) && value is >= 0 and <= 100 ? value : null;
                var mis = mig.Elements(ns + "MissingIndex");
                foreach (var mi in mis)
                {
                    if (mi == null) continue;
                    var suggestion = new SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion
                    {
                        Schema = mi.Attribute("Schema")?.Value ?? "",
                        Table = mi.Attribute("Table")?.Value ?? "",
                        Server = (string?)mi.Attribute("Server"),
                        Database = (string?)mi.Attribute("Database"),
                        ObjectIdentity = new(Core.Models.SqlObjectIdentity.DecodeIdentifier((string?)mi.Attribute("Server")),
                            Core.Models.SqlObjectIdentity.DecodeIdentifier((string?)mi.Attribute("Database")),
                            Core.Models.SqlObjectIdentity.DecodeIdentifier((string?)mi.Attribute("Schema")),
                            Core.Models.SqlObjectIdentity.DecodeIdentifier((string?)mi.Attribute("Table"))),
                        Location = identities?.FindLocation(mi),
                        Source = Core.Models.IndexSuggestionSource.CapturedMissingIndex,
                        Impact = impact,
                        CapturedImpact = capturedImpact
                    };

                    foreach (var cg in mi.Elements(ns + "ColumnGroup"))
                    {
                        if (cg == null) continue;
                        string usage = cg.Attribute("Usage")?.Value ?? "";
                        if (usage is not ("EQUALITY" or "INEQUALITY" or "INCLUDE"))
                            throw new System.IO.InvalidDataException("INDEX_COLUMN_ROLE_INVALID: 缺失索引列角色无效。");
                        var cols = cg.Elements(ns + "Column")
                            .Select(c => c.Attribute("Name")?.Value ?? "")
                            .Where(n => n != "")
                            .Select(n => new SqlXmlAnalyzer.Core.Models.IndexColumn { Name = n, Usage = usage })
                            .ToList();

                        if (usage == "EQUALITY" || usage == "INEQUALITY")
                        {
                            suggestion.KeyColumns.AddRange(cols);
                        }
                        else if (usage == "INCLUDE")
                        {
                            suggestion.IncludeColumns.AddRange(cols);
                        }
                    }

                    if (suggestion.KeyColumns.Count > 0)
                    {
                        // Preserve order within each role, regardless of XML group order.
                        suggestion.KeyColumns = suggestion.KeyColumns.OrderBy(c => c.Usage == "EQUALITY" ? 0 : 1).ToList();
                        SqlXmlAnalyzer.Core.Scoring.IndexScoringCalculator.CalculateScore(suggestion, doc, ns);
                        results.Add(suggestion);
                    }
                }
            }
            Logger.Debug($"IMP-15: 缺失索引提取完成；候选数 {results.Count}；既有索引目录未核验。");
            return results;
        }

        public static string ExtractObjectName(XElement relOp, XNamespace ns)
        {
            if (relOp == null) return "(未知表)";
            string name = string.Join("; ", Core.Services.PlanOperatorFactsService.Get(relOp, ns).Objects.Select(o => o.DisplayName));
            return name.Length == 0 ? "(未知表)" : name;
        }

        public static string ExtractPredicates(XElement relOp, XNamespace ns) => relOp == null ? "" :
            string.Join(" AND ", Core.Services.PlanOperatorFactsService.Get(relOp, ns).Predicates);

        public static bool HasFunctionWrapper(string pred)
        {
            if (string.IsNullOrEmpty(pred)) return false;
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(pred, @"\w+\s*\(.*?\[.+?\]");
            }
            catch
            {
                return false;
            }
        }

        public static string ExtractSeekPredicate(XElement relOp, XNamespace ns) => relOp == null ? "" :
            string.Join(" AND ", Core.Services.PlanOperatorFactsService.Get(relOp, ns).SeekPredicates);

        public static string ExtractResidualPredicate(XElement relOp, XNamespace ns) => ExtractPredicates(relOp, ns);
    }
}

