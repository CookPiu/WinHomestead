using System;
using System.Windows;
using System.Windows.Threading;
using NewPcSetup.App.ViewModels;
using NewPcSetup.Core.Engine;
using NewPcSetup.Native;

namespace NewPcSetup.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppServices services;
        try { services = AppServices.Create(); }
        catch (Exception ex)
        {
            MessageBox.Show("初始化失败：" + ex.Message, "新机开荒", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += (_, args) => OnUnhandled(services, args);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => services.Logger.Error("未处理异常(AppDomain)", args.ExceptionObject as Exception);

        if (!ProcessInfo.IsAdministrator())
        {
            MessageBox.Show("本工具需要以管理员身份运行。请使用管理员账户，或右键选择“以管理员身份运行”。", "新机开荒", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(2);
            return;
        }
        if (!ProcessInfo.ExplorerOwnedByCurrentUser())
        {
            MessageBox.Show("检测到提权账户与当前登录账户不一致。请直接用登录的管理员账户运行，否则用户级设置会写到错误的账户。", "新机开荒", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(3);
            return;
        }

        services.Logger.Info("启动 v" + typeof(App).Assembly.GetName().Version + (e.Args.Length > 0 ? " 参数: " + string.Join(" ", e.Args) : string.Empty));
        var mode = DecideStartup(services, Array.IndexOf(e.Args, "--resume") >= 0);
        var window = new MainWindow { DataContext = new MainViewModel(services, mode) };
        MainWindow = window;
        window.Show();
    }

    /// <summary>UC-12：上次未完成 → 询问是否续跑；--resume 且上次已完成 → 重启后复核并直接看报告。</summary>
    private static StartupMode DecideStartup(AppServices services, bool resumeFlag)
    {
        ResumeSession? last;
        try { last = services.Coordinator.LoadLast(); }
        catch (Exception ex) { services.Logger.Warn("读取上次会话失败: " + ex.Message); return StartupMode.Normal; }
        if (last == null) return StartupMode.Normal;

        if (last.Unfinished)
        {
            var done = last.Result?.Results.Count ?? 0;
            var answer = MessageBox.Show(
                $"上次执行（方案 {last.Plan.Id}）没有正常结束，已完成 {done} 项。\n\n是否继续执行剩余的项目？\n选“否”则从头开始新的探测。",
                "新机开荒", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) { services.Session.Resume = last; return StartupMode.Continue; }
            services.Store.SaveState(new NewPcSetup.Core.Models.AppState(last.Plan.Id, false));
            return StartupMode.Normal;
        }
        if (resumeFlag && last.Result != null) { services.Session.Resume = last; return StartupMode.Reverify; }
        return StartupMode.Normal;
    }

    private static void OnUnhandled(AppServices services, DispatcherUnhandledExceptionEventArgs args)
    {
        services.Logger.Error("未处理异常", args.Exception);
        MessageBox.Show("发生了未处理的错误，已记录到日志：\n" + services.Store.LogDir + "\n\n" + args.Exception.Message, "新机开荒", MessageBoxButton.OK, MessageBoxImage.Error);
        args.Handled = true;
    }
}
