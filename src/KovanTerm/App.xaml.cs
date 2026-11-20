using System.IO;
using System.Windows;
using KovanTerm.Services;

namespace KovanTerm;

public partial class App : Application
{
    /// <summary>Unhandled exceptions are appended here before the app goes down, so a crash leaves a trace.</summary>
    public static string CrashLogPath => Path.Combine(AppPaths.DataDirectory, "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("AppDomain", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) => LogCrash("Dispatcher", args.Exception);
        // Faulted tasks nobody awaited: logged, not fatal
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("UnobservedTask", args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        // Only for crashes that end the app: unobserved tasks are kept in the log
        if (source != "UnobservedTask") CrashReports.Queue(source, ex);
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(CrashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
