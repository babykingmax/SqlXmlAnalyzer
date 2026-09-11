using System;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record WorkspaceLayout(double MinimumWidth, double MinimumHeight, bool Compact);

public sealed class WorkspaceInteractionService
{
    private readonly IUnexpectedErrorReporter _reporter;
    public WorkspaceInteractionService(IUnexpectedErrorReporter? reporter = null) => _reporter = reporter ?? UnexpectedErrorReporter.Shared;

    public static WorkspaceLayout Layout(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0)
            throw new System.IO.InvalidDataException("工作区尺寸必须是有效的非负 DIP 数值。");
        return new(520, 620, width < 1050 || height < 620);
    }

    public static int MoveSelection(int count, int selectedIndex, int direction)
    {
        if (count <= 0) return -1;
        if (selectedIndex < 0 || selectedIndex >= count) return direction < 0 ? count - 1 : 0;
        return direction < 0
            ? (selectedIndex == 0 ? count - 1 : selectedIndex - 1)
            : (selectedIndex == count - 1 ? 0 : selectedIndex + 1);
    }

    public bool Run(string operation, Func<bool> canExecute, Action execute, Action<string> showStatus)
    {
        try
        {
            if (!canExecute()) { Logger.Debug($"IMP25 command unavailable: {operation}."); return false; }
            execute();
            Logger.Debug($"IMP25 command completed: {operation}.");
            return true;
        }
        catch (Exception exception)
        {
            string detail = ExceptionPolicy.Describe(exception, "IMP25." + operation, _reporter);
            showStatus(detail);
            return false;
        }
    }
}
