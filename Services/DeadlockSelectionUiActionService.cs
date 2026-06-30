using System;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockSelectionUiActionService
    {
        private readonly Core.Services.DeadlockSelectionDetailService _detailService;
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly Func<object?> _selectedProcessProvider;
        private readonly Func<object?> _selectedResourceProvider;
        private readonly Func<object?> _selectedPatternProvider;

        public DeadlockSelectionUiActionService(
            Core.Services.DeadlockSelectionDetailService detailService,
            Core.ViewModels.MainViewModel viewModel,
            Func<object?> selectedProcessProvider,
            Func<object?> selectedResourceProvider,
            Func<object?> selectedPatternProvider)
        {
            _detailService = detailService
                ?? throw new ArgumentNullException(nameof(detailService));
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _selectedProcessProvider = selectedProcessProvider
                ?? throw new ArgumentNullException(nameof(selectedProcessProvider));
            _selectedResourceProvider = selectedResourceProvider
                ?? throw new ArgumentNullException(nameof(selectedResourceProvider));
            _selectedPatternProvider = selectedPatternProvider
                ?? throw new ArgumentNullException(nameof(selectedPatternProvider));
        }

        public void SelectCurrentProcess()
        {
            SelectProcess(_selectedProcessProvider());
        }

        public void SelectCurrentResource()
        {
            SelectResource(_selectedResourceProvider());
        }

        public void SelectCurrentPattern()
        {
            SelectPattern(_selectedPatternProvider());
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
