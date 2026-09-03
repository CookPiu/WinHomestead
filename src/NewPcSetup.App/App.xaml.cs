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

    /// <summary>UC-12：--resume（RunOnce 拉起）时对上次“需重启”项复核并先展示记录；上次进程在执行中退出时提示检查。</summary>
    private static StartupMode DecideStartup(AppServices services, bool resumeFlag)
    {
        ResumeSession? last;
        try { last = services.Coordinator.LoadLast(); }
        catch (Exception ex) { services.Logger.Warn("读取上次会话失败: " + ex.Message); return StartupMode.Normal; }
        if (last == null) return StartupMode.Normal;

        if (last.Unfinished)
        {
            MessageBox.Show($"上次运行在执行某一项时意外退出（会话 {last.Plan.Id}）。改动记录在 {services.Store.BaseDir}，请在列表中查看该项的当前状态后再决定是否重新执行。",
                "新机开荒", MessageBoxButton.OK, MessageBoxImage.Warning);
            services.Coordinator.ClearPending(last.Plan.Id);
        }
        if (resumeFlag && last.Result != null)
        {
            try { services.Session.PreviousResult = services.Coordinator.Reverify(last, services.Catalog); return StartupMode.Reverify; }
            catch (Exception ex) { services.Logger.Error("重启后复核失败", ex); }
        }
        return StartupMode.Normal;
    }

    private static void OnUnhandled(AppServices services, DispatcherUnhandledExceptionEventArgs args)
    {
        services.Logger.Error("未处理异常", args.Exception);
        MessageBox.Show("发生了未处理的错误，已记录到日志：\n" + services.Store.LogDir + "\n\n" + args.Exception.Message, "新机开荒", MessageBoxButton.OK, MessageBoxImage.Error);
        args.Handled = true;
    }
}
