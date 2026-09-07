using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinHomestead.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
    }
}
