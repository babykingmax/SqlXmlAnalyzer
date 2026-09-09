namespace SqlXmlAnalyzer.Core.Services;

public static class InputReadPresentation
{
    public static string Describe(InputRecognitionResult input) =>
        $"{input.Status} · {input.ErrorCode}: {input.ErrorMessage}" + Environment.NewLine +
        string.Join(Environment.NewLine, input.Diagnostics.Select(d =>
            $"{d.Code}: {d.Message} {d.Location?.XmlPath}" +
            (d.Location?.Line is int line ? $" (行 {line}，列 {d.Location.Column})" : "") +
            (d.Location?.ByteOffset is long offset ? $" (字节 {offset})" : "")));
}
