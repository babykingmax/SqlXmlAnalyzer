using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SqlXmlAnalyzer.Core.Models
{
    public class IndexColumn : INotifyPropertyChanged
    {
        private string _name = "";
        private string _usage = "";

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        // e.g. "EQUALITY", "INEQUALITY", "INCLUDE"
        public string Usage
        {
            get => _usage;
            set
            {
                _usage = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
