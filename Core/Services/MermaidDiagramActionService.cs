using System;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum MermaidDiagramActionStatus
    {
        Ready,
        MissingDocument
    }

    public sealed record MermaidDiagramActionResult(
        MermaidDiagramActionStatus Status,
        string MermaidCode,
        string UserMessage,
        string LogMessage);

    public sealed class MermaidDiagramActionService
    {
        private readonly MermaidDiagramService _mermaidDiagramService;

        public MermaidDiagramActionService(MermaidDiagramService? mermaidDiagramService = null)
        {
            _mermaidDiagramService = mermaidDiagramService ?? new MermaidDiagramService();
        }

        public MermaidDiagramActionResult BuildDeadlockDiagram(XDocument? document)
        {
            if (document == null)
            {
                return new MermaidDiagramActionResult(
                    MermaidDiagramActionStatus.MissingDocument,
                    "",
                    "当前没有加载的死锁文件！",
                    "");
            }

            return new MermaidDiagramActionResult(
                MermaidDiagramActionStatus.Ready,
                _mermaidDiagramService.BuildDeadlockDiagram(document),
                "",
                "已生成死锁 Mermaid 等待图。");
        }

        public MermaidDiagramActionResult BuildPlanDiagram(
            XDocument? document,
            XNamespace showplanNamespace)
        {
            ArgumentNullException.ThrowIfNull(showplanNamespace);

            if (document == null)
            {
                return new MermaidDiagramActionResult(
                    MermaidDiagramActionStatus.MissingDocument,
                    "",
                    "当前没有加载的执行计划文件！",
                    "");
            }

            return new MermaidDiagramActionResult(
                MermaidDiagramActionStatus.Ready,
                _mermaidDiagramService.BuildPlanDiagram(document, showplanNamespace),
                "",
                "已生成执行计划 Mermaid 图。");
        }
    }
}
