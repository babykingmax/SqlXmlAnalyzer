namespace SqlXmlAnalyzer.Core.Services;

public enum AnalysisOperationState { Idle, Loading, Parsing, Analyzing, PreparingView, Ready, Cancelled, Partial, Failed }
public sealed record AnalysisStageTiming(AnalysisOperationState State, double Milliseconds);
public sealed record AnalysisProgress(long RequestId, long Revision, AnalysisOperationState State, string Detail,
    IReadOnlyList<AnalysisStageTiming> Stages)
{
    public bool IsBusy => State is AnalysisOperationState.Loading or AnalysisOperationState.Parsing
        or AnalysisOperationState.Analyzing or AnalysisOperationState.PreparingView;
    public string DisplayText => State switch
    {
        AnalysisOperationState.Idle => "就绪", AnalysisOperationState.Loading => "正在读取",
        AnalysisOperationState.Parsing => "正在解析", AnalysisOperationState.Analyzing => "正在分析",
        AnalysisOperationState.PreparingView => "正在准备视图", AnalysisOperationState.Ready => "分析完成",
        AnalysisOperationState.Cancelled => "已取消", AnalysisOperationState.Partial => "部分完成", _ => "分析失败"
    };
}
