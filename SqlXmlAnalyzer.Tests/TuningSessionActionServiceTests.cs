using System;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class TuningSessionActionServiceTests
    {
        [Fact]
        public void ChooseSaveSessionPath_UsesTuningSessionSaveDialogRequest()
        {
            var dialogService = new FakeFileDialogService(savePath: "C:\\Temp\\session.pesession");
            var service = new TuningSessionActionService(dialogService);

            string? path = service.ChooseSaveSessionPath();

            path.Should().Be("C:\\Temp\\session.pesession");
            dialogService.LastSaveRequest.Should().NotBeNull();
            dialogService.LastSaveRequest!.Filter.Should().Be("SqlXmlAnalyzer tuning session (*.pesession)|*.pesession");
            dialogService.LastSaveRequest.Title.Should().Be("Save current tuning session");
            dialogService.LastSaveRequest.DefaultExtension.Should().Be(".pesession");
            dialogService.LastSaveRequest.FileName.Should().Be("Tuning_Session.pesession");
        }

        [Fact]
        public void ChooseLoadSessionPath_UsesTuningSessionOpenDialogRequest()
        {
            var dialogService = new FakeFileDialogService(openPath: "C:\\Temp\\session.pesession");
            var service = new TuningSessionActionService(dialogService);

            string? path = service.ChooseLoadSessionPath();

            path.Should().Be("C:\\Temp\\session.pesession");
            dialogService.LastOpenRequest.Should().NotBeNull();
            dialogService.LastOpenRequest!.Filter.Should().Be("SqlXmlAnalyzer tuning session (*.pesession)|*.pesession");
            dialogService.LastOpenRequest.Title.Should().Be("Open tuning session");
            dialogService.LastOpenRequest.DefaultExtension.Should().Be(".pesession");
        }

        [Fact]
        public void SwapPlans_ReturnsPlanBAsPlanAAndPlanAAsPlanB()
        {
            var service = new TuningSessionActionService(new FakeFileDialogService());
            var planA = new PlanSnapshot { Title = "Plan A" };
            var planB = new PlanSnapshot { Title = "Plan B" };

            PlanSwapResult result = service.SwapPlans(planA, planB);

            result.PlanA.Should().BeSameAs(planB);
            result.PlanB.Should().BeSameAs(planA);
        }

        [Fact]
        public void SwapPlans_WhenOneSideIsNull_StillSwaps()
        {
            var service = new TuningSessionActionService(new FakeFileDialogService());
            var planB = new PlanSnapshot { Title = "Plan B" };

            PlanSwapResult result = service.SwapPlans(null, planB);

            result.PlanA.Should().BeSameAs(planB);
            result.PlanB.Should().BeNull();
        }

        private sealed class FakeFileDialogService : IFileDialogService
        {
            private readonly string? _openPath;
            private readonly string? _savePath;

            public FakeFileDialogService(
                string? openPath = null,
                string? savePath = null)
            {
                _openPath = openPath;
                _savePath = savePath;
            }

            public FileDialogRequest? LastOpenRequest { get; private set; }
            public FileDialogRequest? LastSaveRequest { get; private set; }

            public string? ShowOpenFile(FileDialogRequest request)
            {
                LastOpenRequest = request ?? throw new ArgumentNullException(nameof(request));
                return _openPath;
            }

            public string? ShowSaveFile(FileDialogRequest request)
            {
                LastSaveRequest = request ?? throw new ArgumentNullException(nameof(request));
                return _savePath;
            }
        }
    }
}
