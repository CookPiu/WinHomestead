using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NewPcSetup.App;
using NewPcSetup.App.ViewModels;
using NewPcSetup.App.Views;
using Xunit;

namespace NewPcSetup.Core.Tests;

/// <summary>
/// 在 STA 线程上加载 App.xaml 资源并实例化全部视图，确保 XAML 中引用的样式/画刷键在 WPF-UI 中存在。
/// 不显示窗口、不执行任何任务；使用的 AppServices 只做只读探测。
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
                var app = new global::NewPcSetup.App.App();
                app.InitializeComponent();

                var services = AppServices.Create();
                var snapshot = TestData.Snapshot("D:", true, "pip", "docker");
                services.Session.Snapshot = snapshot;
                services.Session.Answers = TestData.Answers(snapshot, Core.Models.UiStyle.Win10Like, promo: true);
                services.Session.Plan = services.Planner.Build(services.Catalog, snapshot, services.Session.Answers);

                Render(new WelcomeView { DataContext = new WelcomeViewModel(services, () => { }) });
                Render(new QuestionnaireView { DataContext = new QuestionnaireViewModel(services, () => { }, () => { }) });
                Render(new PlanView { DataContext = new PlanViewModel(services, () => { }, () => { }) });
                Render(new ExecuteView());
                Render(new ReportView { DataContext = new ReportViewModel(services, () => { }) });
                Render(new SoftwareView { DataContext = new SoftwareViewModel(services, () => { }) });
                Render(new StorageView { DataContext = new StorageViewModel(services, () => { }) });

                var main = new MainWindow { DataContext = new MainViewModel(services) };
                main.Measure(new Size(980, 680));

                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.True(failure == null, failure?.ToString());
    }

    private static void Render(UserControl view)
    {
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));
        view.UpdateLayout();
    }
}
