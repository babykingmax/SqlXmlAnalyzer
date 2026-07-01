using System.Windows.Controls;

namespace SqlXmlAnalyzer.Views
{
    public partial class ShellStatusBar : UserControl
    {
        public ShellStatusBar()
        {
            InitializeComponent();
        }

        public TextBlock StatusTextBlock => PART_StatusTextBlock;
    }
}
