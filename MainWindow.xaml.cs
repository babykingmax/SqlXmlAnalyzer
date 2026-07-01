using System;
using System.Windows;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow : Window
    {
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowChromeInterop.Attach(this);
        }


    }
}
