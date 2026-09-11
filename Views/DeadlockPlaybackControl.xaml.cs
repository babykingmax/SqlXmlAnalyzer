using System.Windows.Controls;

namespace SqlXmlAnalyzer.Views
{
    public partial class DeadlockPlaybackControl : UserControl
    {
        public DeadlockPlaybackControl()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => ApplyMotion();
        }
        public static readonly System.Windows.DependencyProperty ReduceMotionProperty = System.Windows.DependencyProperty.Register(
            nameof(ReduceMotion), typeof(bool), typeof(DeadlockPlaybackControl), new System.Windows.PropertyMetadata(false, (sender, _) => ((DeadlockPlaybackControl)sender).ApplyMotion()));
        public bool ReduceMotion { get => (bool)GetValue(ReduceMotionProperty); set => SetValue(ReduceMotionProperty, value); }
        private void ApplyMotion() { if (DataContext is ViewModels.DeadlockPlaybackViewModel model) model.ReduceMotion = ReduceMotion; }
    }
}
