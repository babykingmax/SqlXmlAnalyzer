using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Services;

public static class WorkspaceSourceFormatter
{
    public static string Context(DocumentEnvelope? envelope, DocumentCapabilities capabilities, InputStatus? status = null) =>
        $"来源：{envelope?.SourceName ?? "内存 XML"}；采集时间：{envelope?.CapturedAt?.ToString("O") ?? "未采集"}；"
        + $"引擎：{envelope?.EngineBuild ?? "N/A"}；Schema：{envelope?.SchemaVersion ?? "N/A"}；能力：{capabilities}"
        + (status == null ? "" : $"；读取状态：{status}");

    public static string Location(SourceLocation? source) => source == null ? "未采集原始位置。" :
        $"XML：{source.XmlPath}；行：{source.Line?.ToString() ?? "N/A"}；列：{source.Column?.ToString() ?? "N/A"}"
        + (source.EventIndex == null ? "" : $"；源事件：{source.EventIndex}")
        + (source.ByteOffset == null ? "" : $"；字节偏移：{source.ByteOffset}；长度：{source.ByteLength}");
}
