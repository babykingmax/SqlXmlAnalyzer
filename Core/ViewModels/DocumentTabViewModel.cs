using System;
using System.Windows.Input;

using SqlXmlAnalyzer.Core.Mvvm;

namespace SqlXmlAnalyzer.Core.ViewModels
{
    public abstract class DocumentTabViewModel : ObservableObject
    {
        private string _title = "New Document";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string FilePath { get; set; } = string.Empty;

        public event EventHandler? CloseRequested;

        private ICommand? _closeCommand;
        public ICommand CloseCommand
        {
            get
            {
                if (_closeCommand == null)
                {
                    _closeCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
                }
                return _closeCommand;
            }
        }
    }
}
