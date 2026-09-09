namespace SqlXmlAnalyzer.Core.Privacy;

public static class OutputPrivacy
{
    public const string RawNotice = "未脱敏：此输出可能包含 SQL、参数、对象名称、文件路径或图像中的敏感信息，仅供本地诊断；不能作为脱敏材料共享。";
    public const string RedactedNotice = "已按已支持字段生成脱敏副本；文本与对象名称已改变，仅用于结构诊断，不可执行其中的 SQL。未声明任意 XML 均无敏感信息或已通过 SSMS 兼容验证。";
    public static string MarkRaw(string text) => text.StartsWith(RawNotice, StringComparison.Ordinal)
        ? text : RawNotice + Environment.NewLine + Environment.NewLine + text;
}
