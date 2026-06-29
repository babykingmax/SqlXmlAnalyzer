using System;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockSelectionUiActionService
    {
        private readonly Core.Services.DeadlockSelectionDetailService _detailService;
        private readonly Core.ViewModels.MainViewModel _viewModel;

        public DeadlockSelectionUiActionService(
            Core.Services.DeadlockSelectionDetailService detailService,
            Core.ViewModels.MainViewModel viewModel)
        {
            _detailService = detailService
                ?? throw new ArgumentNullException(nameof(detailService));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public void SelectProcess(object? selectedItem)
        {
            if (selectedItem is DeadlockProcess process)
            {
                _viewModel.DeadlockPatternText =
                    _detailService.BuildProcessDetail(process);
            }
        }

        public void SelectResource(object? selectedItem)
        {
            if (selectedItem is LockResource resource)
            {
                _viewModel.DeadlockPatternText =
                    _detailService.BuildResourceDetail(resource);
            }
        }

        public void SelectPattern(object? selectedItem)
        {
            if (selectedItem is DeadlockPattern pattern)
            {
                _viewModel.DeadlockPatternText =
                    _detailService.BuildPatternDetail(pattern);
            }
        }
    }
}
