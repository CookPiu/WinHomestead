using System;
using System.Windows;
using System.Windows.Threading;
using NewPcSetup.App.ViewModels;
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

        services.Logger.Info("启动 v" + typeof(App).Assembly.GetName().Version);
        var window = new MainWindow { DataContext = new MainViewModel(services) };
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandled(AppServices services, DispatcherUnhandledExceptionEventArgs args)
    {
        services.Logger.Error("未处理异常", args.Exception);
        MessageBox.Show("发生了未处理的错误，已记录到日志：\n" + services.Store.LogDir + "\n\n" + args.Exception.Message, "新机开荒", MessageBoxButton.OK, MessageBoxImage.Error);
        args.Handled = true;
    }
}
