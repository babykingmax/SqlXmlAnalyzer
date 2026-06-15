using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.ViewModels
{
    public class DeadlockStepItem : INotifyPropertyChanged
    {
        public int StepIndex { get; set; }
        public string DisplayName { get; set; } = "";
        public string ToolTip { get; set; } = "";
        public bool IsStart { get; set; }
        public bool IsVictim { get; set; }
        public bool IsGrant { get; set; }
        public bool IsRequest { get; set; }

        private bool _isActive;
        public bool IsActive 
        { 
            get => _isActive; 
            set { _isActive = value; OnPropertyChanged(); } 
        }

        private bool _isCurrent;
        public bool IsCurrent 
        { 
            get => _isCurrent; 
            set { _isCurrent = value; OnPropertyChanged(); } 
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DeadlockPlaybackViewModel : INotifyPropertyChanged
    {
        private readonly List<DeadlockEvent> _events;
        private readonly DispatcherTimer _timer;
        private int _currentStep;
        private bool _isPlaying;
        private bool _focusCriticalPath;
        private int _playbackSpeed = 1000; // ms

        public List<DeadlockStepItem> PlaybackSteps { get; }

        public DeadlockPlaybackViewModel(List<DeadlockEvent> events)
        {
            _events = events;
            _currentStep = 0;
            
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
            
            PlayCommand = new RelayCommand(o => TogglePlay());
            StepForwardCommand = new RelayCommand(o => StepForward(), o => CanStepForward);
            StepBackwardCommand = new RelayCommand(o => StepBackward(), o => CanStepBackward);
            ResetCommand = new RelayCommand(o => Reset());

            PlaybackSteps = new List<DeadlockStepItem>();
            PlaybackSteps.Add(new DeadlockStepItem 
            { 
                StepIndex = 0, 
                DisplayName = "始", 
                ToolTip = "初始/就绪状态", 
                IsStart = true 
            });

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                string name = ev.IsVictim ? "💀" : (i + 1).ToString();
                PlaybackSteps.Add(new DeadlockStepItem 
                { 
                    StepIndex = i + 1, 
                    DisplayName = name, 
                    ToolTip = $"步骤 {i + 1}: {ev.Description}", 
                    IsVictim = ev.IsVictim,
                    IsGrant = ev.Type == "Grant",
                    IsRequest = ev.Type == "Request"
                });
            }
            
            UpdateState();
        }

        public int TotalSteps => _events.Count;

        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                if (value >= 0 && value <= TotalSteps)
                {
                    _currentStep = value;
                    OnPropertyChanged(nameof(CurrentStep));
                    UpdateState();
                }
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(PlayButtonText));
                if (_isPlaying)
                {
                    _timer.Interval = TimeSpan.FromMilliseconds(_playbackSpeed);
                    _timer.Start();
                }
                else
                {
                    _timer.Stop();
                }
            }
        }

        public string PlayButtonText => IsPlaying ? "⏸ 暂停" : "▶️ 播放";

        public bool FocusCriticalPath
        {
            get => _focusCriticalPath;
            set
            {
                _focusCriticalPath = value;
                OnPropertyChanged(nameof(FocusCriticalPath));
                OnPropertyChanged(nameof(CurrentStep)); // Trigger re-evaluation of visibility
            }
        }

        public int PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                _playbackSpeed = value;
                OnPropertyChanged(nameof(PlaybackSpeed));
                if (IsPlaying)
                {
                    _timer.Interval = TimeSpan.FromMilliseconds(_playbackSpeed);
                }
            }
        }

        public string CurrentStepDescription
        {
            get
            {
                if (_currentStep == 0) return "准备就绪。点击播放开始回放死锁形成过程。";
                var ev = _events[_currentStep - 1];
                return $"步骤 {_currentStep}/{TotalSteps}: {ev.Description}";
            }
        }

        public ICommand PlayCommand { get; }
        public ICommand StepForwardCommand { get; }
        public ICommand StepBackwardCommand { get; }
        public ICommand ResetCommand { get; }

        public bool CanStepForward => _currentStep < TotalSteps;
        public bool CanStepBackward => _currentStep > 0;

        private void TogglePlay()
        {
            if (CurrentStep >= TotalSteps && !IsPlaying) CurrentStep = 0;
            IsPlaying = !IsPlaying;
        }

        private void StepForward()
        {
            if (CanStepForward) CurrentStep++;
        }

        private void StepBackward()
        {
            if (CanStepBackward) CurrentStep--;
        }

        private void Reset()
        {
            IsPlaying = false;
            CurrentStep = 0;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (CanStepForward)
            {
                CurrentStep++;
            }
            else
            {
                IsPlaying = false;
            }
        }

        private void UpdateState()
        {
            OnPropertyChanged(nameof(CurrentStepDescription));
            OnPropertyChanged(nameof(CanStepForward));
            OnPropertyChanged(nameof(CanStepBackward));

            if (PlaybackSteps != null)
            {
                foreach (var step in PlaybackSteps)
                {
                    step.IsActive = step.StepIndex <= _currentStep;
                    step.IsCurrent = step.StepIndex == _currentStep;
                }
            }
            
            StepChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? StepChanged;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
