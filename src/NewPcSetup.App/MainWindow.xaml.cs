using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace NewPcSetup.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
    }
}
