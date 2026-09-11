namespace SqlXmlAnalyzer.Core.Diagnostics;

public static class ExceptionPolicy
{
    // Expected input, storage, permissions and cancellation failures do not require a process dump.
    public static bool IsExpected(Exception exception) => exception is
        OperationCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or
        System.Xml.XmlException or System.Text.DecoderFallbackException or System.Text.EncoderFallbackException;

    public static string Describe(Exception exception, string operation, IUnexpectedErrorReporter? reporter = null)
    {
        if (IsExpected(exception))
        {
            if (exception is OperationCanceledException) Logger.Warning($"操作取消：{operation}");
            else Logger.Error($"操作失败：{operation}", exception);
            return exception.Message;
        }
        return $"{Message(exception)}；{Capture(exception, operation, reporter).Summary}";
    }

    public static UnexpectedErrorReport Capture(Exception exception, string operation, IUnexpectedErrorReporter? reporter = null)
    {
        try { return (reporter ?? UnexpectedErrorReporter.Shared).Report(exception, operation); }
        catch (Exception captureError)
        {
            // Even a custom diagnostic provider must not destroy the primary error or commit state.
            Logger.Critical($"未知异常：{operation}", exception);
            var result = new UnexpectedErrorReport(null, null, $"诊断组件失败：{Message(captureError)}");
            Logger.Error(result.Summary);
            return result;
        }
    }

    public static string Message(Exception exception)
    {
        try { return exception.Message; }
        catch { return exception.GetType().FullName ?? "Exception"; }
    }

    public static string Details(Exception exception)
    {
        try { return exception.ToString(); }
        catch { return $"{exception.GetType().FullName}: {Message(exception)} (exception formatting failed)"; }
    }
}
