using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinHomestead.App;
using WinHomestead.App.ViewModels;
using WinHomestead.App.Views;
using Xunit;

namespace WinHomestead.Core.Tests;

/// <summary>
/// 在 STA 线程上加载 App.xaml 资源并实例化全部视图，确保 XAML 中引用的样式/画刷键在 WPF-UI 中存在。
/// 不显示窗口、不执行任何任务；使用的 AppServices 只做只读探测（Detect）。
/// </summary>
public class UiSmokeTests
{
    [Fact]
    public void AllViewsLoadWithAppResources()
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                var app = new global::WinHomestead.App.App();
                app.InitializeComponent();

                var services = AppServices.Create();
                var snapshot = TestData.Snapshot("D:", true, "pip", "docker");
                services.Session.Snapshot = snapshot;

                var home = new HomeViewModel(services);
                // 列表生成已改到后台线程，测试线程要自己泵一下 Dispatcher 才能等到结果
                Pump(() => home.Categories.Count > 0, TimeSpan.FromSeconds(30));
                Assert.NotEmpty(home.Categories);
                Render(new HomeView { DataContext = home });
                Render(new ReportView { DataContext = new ReportViewModel(services, () => { }) });
                Render(new SoftwareView { DataContext = new SoftwareViewModel(services, () => { }) });
                Render(new CheckupView { DataContext = new CheckupViewModel(services, () => { }) });
                Render(new StartupView { DataContext = new StartupViewModel(services, () => { }) });

                // 每个子页都必须有 DataTemplate：MainViewModel 把 ViewModel 直接塞进 ContentControl，
                // 漏了映射的页面会原样显示类名而不是界面，而上面的 Render 是直接 new 视图，测不出这个
                foreach (var vmType in new[]
                         {
                             typeof(HomeViewModel), typeof(ReportViewModel), typeof(SoftwareViewModel),
                             typeof(CheckupViewModel), typeof(StartupViewModel),
                         })
                    Assert.True(app.Resources.Contains(new DataTemplateKey(vmType)), vmType.Name + " 没有对应的 DataTemplate，页面会显示成类名");

                // 黄色提示条靠 InfoBar 自带的关闭按钮，它走 TemplateButtonCommand 并把 IsOpen 置回 false；
                // 这是第三方控件的行为，钉在测试里，换版本时能第一时间发现
                var bar = new Wpf.Ui.Controls.InfoBar { IsOpen = true, IsClosable = true };
                bar.TemplateButtonCommand.Execute(null);
                Assert.False(bar.IsOpen, "InfoBar 的关闭按钮不再把 IsOpen 置为 false，提示条的关闭功能需要改实现");

                var main = new MainWindow { DataContext = new MainViewModel(services) };
                main.Measure(new Size(1100, 740));

                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.True(failure == null, failure?.ToString());
    }

    /// <summary>在没有消息循环的测试线程上驱动 Dispatcher，直到条件成立或超时。</summary>
    private static void Pump(Func<bool> until, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    private static void Render(UserControl view)
    {
        view.Measure(new Size(1000, 650));
        view.Arrange(new Rect(0, 0, 1000, 650));
        view.UpdateLayout();
    }
}
