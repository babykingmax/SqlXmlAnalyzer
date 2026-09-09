using System.Runtime.CompilerServices;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class AnalysisInputLifetimeTests
{
    [Fact]
    public void ClearResults_ReleasesBothInputsAndTheirSourceBuffersWhileTheViewModelRemainsAlive()
    {
        var vm = new MainViewModel();
        WeakReference[] retained = AttachInputs(vm);
        vm.ClearResults();
        vm.ClearResults();
        vm.CurrentPlanInput.Should().BeNull();
        vm.CurrentDeadlockInput.Should().BeNull();
        vm.CurrentPlanDoc.Should().BeNull();
        vm.CurrentDeadlockDoc.Should().BeNull();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        retained.Should().OnlyContain(reference => !reference.IsAlive);
        GC.KeepAlive(vm);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] AttachInputs(MainViewModel vm)
    {
        var planBytes = new byte[1024 * 1024];
        var deadlockBytes = new byte[1024 * 1024];
        var reader = new InputRecognitionService();
        var plan = reader.Parse(PlanIdentityModelTests.Wrap("<StmtSimple/>")) with { SourceSnapshot = planBytes };
        var deadlock = reader.Parse("<deadlock><process-list><process id='p1'/></process-list><resource-list><keylock/></resource-list></deadlock>")
            with { SourceSnapshot = deadlockBytes };
        vm.CurrentPlanInput = plan;
        vm.CurrentPlanDoc = plan.Document;
        vm.CurrentDeadlockInput = deadlock;
        vm.CurrentDeadlockDoc = deadlock.Document;
        return new[] { new WeakReference(plan), new WeakReference(deadlock), new WeakReference(planBytes),
            new WeakReference(deadlockBytes), new WeakReference(plan.Document!), new WeakReference(deadlock.Document!) };
    }
}
