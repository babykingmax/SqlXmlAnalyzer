using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class TuningSessionUiActionService
    {
        private readonly Core.Services.TuningSessionActionService _actionService;
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly Func<object?> _selectedSnapshotProvider;

        public TuningSessionUiActionService(
            Core.Services.TuningSessionActionService actionService,
            Core.ViewModels.MainViewModel viewModel,
            Func<object?> selectedSnapshotProvider)
        {
            _actionService = actionService
                ?? throw new ArgumentNullException(nameof(actionService));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _selectedSnapshotProvider = selectedSnapshotProvider
                ?? throw new ArgumentNullException(nameof(selectedSnapshotProvider));
        }

        public PlanSnapshot? GetSelectedHistorySnapshot(object? selectedItem)
        {
            Core.Services.PlanSnapshotOpenResult result =
                _actionService.OpenHistorySnapshot(selectedItem);

            return result.Status == Core.Services.PlanSnapshotOpenStatus.Ready
                ? result.Snapshot
                : null;
        }

        public async Task OpenSelectedHistorySnapshotAsync(
            Core.Services.AnalysisSessionCoordinator analysisSessions,
            Func<XDocument, string, long, CancellationToken, Task> analyzeExecutionPlanDocumentAsync)
        {
            ArgumentNullException.ThrowIfNull(analysisSessions);
            ArgumentNullException.ThrowIfNull(analyzeExecutionPlanDocumentAsync);

            PlanSnapshot? snapshot = GetSelectedHistorySnapshot(_selectedSnapshotProvider());
            if (snapshot == null)
            {
                return;
            }

            Core.Services.AnalysisSession session = analysisSessions.Begin();
            _viewModel.CurrentPlanFilePath = snapshot.FilePath;
            await analyzeExecutionPlanDocumentAsync(
                snapshot.Document,
                snapshot.FilePath,
                session.RequestId,
                session.Token);
        }

        public void SaveSession()
        {
            string? fileName = _actionService.ChooseSaveSessionPath();
            if (fileName != null)
            {
                _viewModel.SaveSession(fileName);
            }
        }

        public void LoadSession()
        {
            string? fileName = _actionService.ChooseLoadSessionPath();
            if (fileName != null)
            {
                _viewModel.LoadSession(fileName);
            }
        }

        public void SwapPlans()
        {
            Core.Services.PlanSwapResult result =
                _actionService.SwapPlans(
                    _viewModel.PlanA,
                    _viewModel.PlanB);

            _viewModel.PlanA = result.PlanA;
            _viewModel.PlanB = result.PlanB;
        }
    }
}
