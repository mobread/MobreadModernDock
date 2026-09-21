using Avalonia;
using System;
using System.IO;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Infrastructure.Windows.Adapters;
using MobreadModernDock.Infrastructure.Windows.Persistence;

namespace MobreadModernDock;

sealed class Program
{
    private static SingleInstanceGuard? _singleInstanceGuard;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // The app is a WinExe (no console), so runtime failures are otherwise
        // silent. Log unhandled exceptions for diagnostics.
        CrashLogger.Hook();

        // The process cwd is inherited by everything launched from the dock
        // (Explorer, Settings, elevated launches, files opened from folder
        // stacks). Left at the exe's own folder it pins bin/ — a running child
        // blocks rebuilds and renames — so park it somewhere neutral.
        try { Environment.CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        catch { }

        // Single-instance guard — prevents multiple dock instances.
        // Port of App.java's SingleInstanceGuard + localized warning dialog.
        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.TryAcquire())
        {
            var language = new JsonDockRepository().Load().Language;
            string message = LocalizationService.BootstrapText(language, "dialog.singleInstance.message");
            System.Windows.Forms.MessageBox.Show(
                message,
                "Mobread Modern Dock",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
            return;
        }

        // #17 At login the Run-key launches us in parallel with explorer. The
        // dock reparents itself under Progman and hides Shell_TrayWnd; if
        // neither exists yet those steps silently fail and the dock ends up
        // as an ordinary top-level window. Wait (bounded) for the shell.
        Infrastructure.Windows.Native.ShellReadiness.WaitForShell(TimeSpan.FromSeconds(30));

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _singleInstanceGuard?.Dispose();
            _singleInstanceGuard = null;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal static class CrashLogger
{
    private static readonly object Sync = new();
    private static readonly string Path =
        System.IO.Path.Combine(MobreadModernDock.Infrastructure.Windows.Adapters.AppDataLocator.Root, "crash-log.txt");

    public static void Hook()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log($"UNHANDLED EXCEPTION: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Log($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
    }

    public static void Log(string message)
    {
        try
        {
            lock (Sync)
                File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
