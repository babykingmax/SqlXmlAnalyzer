using System;
using System.IO;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>The document and provenance of one request, committed together on success.</summary>
public sealed record PlanAnalysisSource
{
    public PlanAnalysisSource(XDocument document, string filePath, InputRecognitionResult? input = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (input != null && (!input.IsSuccess || input.Kind != AnalysisDocumentKind.ExecutionPlanXml ||
            !ReferenceEquals(input.Document, document)))
            throw new InvalidDataException("执行计划文档与输入来源快照不一致，请重新打开文件。");
        Document = document;
        FilePath = filePath;
        Input = input;
    }

    public XDocument Document { get; }
    public string FilePath { get; }
    public InputRecognitionResult? Input { get; }
}
