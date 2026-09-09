using System;
using System.Collections.Generic;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Analysis
{
    public class SqlXmlAnalysisEngine : IAnalysisEngine
    {
        private readonly string? _configPath;
        private readonly InputRecognitionService _recognition;
        private readonly IUnexpectedErrorReporter _unexpectedErrors;
        private readonly IPlanDocumentBuilder? _planBuilder;
        private readonly Func<Core.Rules.RuleEngine>? _ruleEngineFactory;

        public SqlXmlAnalysisEngine(string? configPath = null, InputRecognitionService? recognition = null,
            IUnexpectedErrorReporter? unexpectedErrors = null, IPlanDocumentBuilder? planBuilder = null,
            Func<Core.Rules.RuleEngine>? ruleEngineFactory = null)
        {
            _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
            _planBuilder = planBuilder;
            _ruleEngineFactory = ruleEngineFactory;
            _recognition = recognition ?? new InputRecognitionService(_unexpectedErrors);
            _configPath = string.IsNullOrWhiteSpace(configPath)
                ? null
                : RuleConfigurationPathResolver.Resolve(configPath);
        }

        public AnalysisReport Analyze(string xmlContent) => AnalyzeInput(() => _recognition.Parse(xmlContent));

        public AnalysisReport AnalyzeDocument(XDocument document) => AnalyzeInput(() => InputRecognitionService.Recognize(document));

        public AnalysisReport AnalyzeInput(InputRecognitionResult input) => AnalyzeInput(() => input);
        public AnalysisReport AnalyzeInput(InputRecognitionResult input, System.Threading.CancellationToken cancellationToken) =>
            AnalyzeInput(() => input, cancellationToken);

        private AnalysisReport AnalyzeInput(Func<InputRecognitionResult> recognize, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                InputRecognitionResult input = recognize().ForExecutionPlan();
                if (!input.IsSuccess) return Failure(input.Status, input.ErrorCode!, input.ErrorMessage!) with
                    { InputEnvelope = input.Envelope, Capabilities = input.Capabilities, InputDiagnostics = input.Diagnostics };
                XDocument doc = input.Document!;
                XNamespace ns = doc.Root!.Name.Namespace;
                var plan = _planBuilder?.Build(doc, input.Envelope!, cancellationToken) ?? PlanIdentityAdapter.GetDocument(doc, cancellationToken);
                var diagnostics = _ruleEngineFactory?.Invoke().AnalyzePlanDetailed(doc, ns, plan, input.Capabilities, cancellationToken)
                    ?? PlanDiagnosticAnalyzer.AnalyzeDetailed(doc, ns, _configPath, plan, input.Capabilities, cancellationToken, _unexpectedErrors);
                var ruleResults = diagnostics.ToLegacyResults();

                var issues = new List<IAnalysisIssue>();
                foreach (var res in ruleResults)
                {
                    var severity = MapSeverity(res.Severity);

                    issues.Add(new SqlPlanAnalysisIssue(
                        res.RuleId,
                        res.Diagnostic == null ? $"[{res.Location?.DisplayScope}] [Node {res.NodeId}] {res.Title}: {res.Message}"
                            : Core.Rules.DiagnosticTextFormatter.FormatDiagnostic(res.Diagnostic),
                        severity
                    ) { Location = res.Location, Objects = res.Objects, Diagnostic = res.Diagnostic, Run = res.Run });
                }

                return new AnalysisReport(issues) { Diagnostics = diagnostics, Plan = plan, InputEnvelope = input.Envelope, Capabilities = input.Capabilities, InputDiagnostics = input.Diagnostics };
            }
            catch (OperationCanceledException ex)
            {
                return Failure(InputStatus.Cancelled, "INPUT_CANCELLED", ExceptionPolicy.Describe(ex, "SqlXmlAnalysisEngine.Analyze", _unexpectedErrors));
            }
            catch (System.IO.InvalidDataException ex)
            {
                return Failure(InputStatus.Invalid, "INPUT_PLAN_IDENTITY_INVALID", ExceptionPolicy.Describe(ex, "SqlXmlAnalysisEngine.Analyze", _unexpectedErrors));
            }
            catch (Exception ex)
            {
                string message = ExceptionPolicy.Describe(ex, "SqlXmlAnalysisEngine.Analyze", _unexpectedErrors);
                return Failure(InputStatus.UnexpectedError, "INPUT_ANALYSIS_ERROR", message);
            }
        }

        private static AnalysisReport Failure(InputStatus status, string code, string message) =>
            new(new List<IAnalysisIssue> { new SqlPlanAnalysisIssue(code, message, IssueSeverity.Critical) })
            { InputStatus = status, InputErrorCode = code, InputErrorMessage = message };

        private static IssueSeverity MapSeverity(string severityStr)
        {
            switch (severityStr?.Trim())
            {
                case "Critical":
                    return IssueSeverity.Critical;
                case "Info":
                    return IssueSeverity.Info;
                case "Warning":
                default:
                    return IssueSeverity.Warning;
            }
        }
    }
}
