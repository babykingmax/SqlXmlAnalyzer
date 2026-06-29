using System;
using System.Linq;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class PlanStatisticsUiActionService
    {
        private readonly StatisticsHistogramControl _statisticsHistogramView;

        public PlanStatisticsUiActionService(
            StatisticsHistogramControl statisticsHistogramView)
        {
            _statisticsHistogramView = statisticsHistogramView
                ?? throw new ArgumentNullException(nameof(statisticsHistogramView));
        }

        public void LoadFromPlan(XDocument document, XNamespace showplanNamespace)
        {
            var parameterList = document
                .Descendants(showplanNamespace + "ParameterList")
                .Descendants(showplanNamespace + "ColumnReference");
            var sniffedParameter = parameterList.FirstOrDefault(parameter =>
                !string.IsNullOrEmpty(parameter.Attribute("ParameterCompiledValue")?.Value)
                && !string.IsNullOrEmpty(parameter.Attribute("ParameterRuntimeValue")?.Value)
                && parameter.Attribute("ParameterCompiledValue")?.Value
                    != parameter.Attribute("ParameterRuntimeValue")?.Value);

            XElement? displayParameter = sniffedParameter ?? parameterList.FirstOrDefault();
            if (displayParameter != null)
            {
                string column = displayParameter.Attribute("Column")?.Value ?? "@Param";
                string compiled = displayParameter.Attribute("ParameterCompiledValue")?.Value
                    ?? (sniffedParameter == null ? "1" : string.Empty);
                string runtime = displayParameter.Attribute("ParameterRuntimeValue")?.Value
                    ?? (sniffedParameter == null ? "1" : string.Empty);

                _statisticsHistogramView.LoadParameterData(column, compiled, runtime);
            }

            _statisticsHistogramView.LoadStatisticsUsage(document, showplanNamespace);
        }
    }
}
