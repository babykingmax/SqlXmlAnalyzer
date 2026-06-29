using System;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class TuningSessionUiActionService
    {
        private readonly Core.Services.TuningSessionActionService _actionService;
        private readonly Core.ViewModels.MainViewModel _viewModel;

        public TuningSessionUiActionService(
            Core.Services.TuningSessionActionService actionService,
            Core.ViewModels.MainViewModel viewModel)
        {
            _actionService = actionService
                ?? throw new ArgumentNullException(nameof(actionService));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public PlanSnapshot? GetSelectedHistorySnapshot(object? selectedItem)
        {
            Core.Services.PlanSnapshotOpenResult result =
                _actionService.OpenHistorySnapshot(selectedItem);

            return result.Status == Core.Services.PlanSnapshotOpenStatus.Ready
                ? result.Snapshot
                : null;
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
