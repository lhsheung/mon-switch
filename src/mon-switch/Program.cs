using MonSwitch.App;
using MonSwitch.Core;
using MonSwitch.Services;

namespace MonSwitch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CommandLine commandLine = CommandLine.Parse(args);

        // Localise before anything can produce a message, so even a start-up failure is
        // readable. The default is the system language; the saved preference is applied
        // further down, once settings.json has been read.
        LocalizationService.Instance.Apply(AppLanguage.System, raiseEvent: false);

        // ---- switches that must work even when settings.json is unreadable ----------

        if (commandLine.UnknownOption is not null)
        {
            ConsoleBridge.Attach();
            ConsoleBridge.WriteLine(Loc.T("cli.errorUnknownOption", commandLine.UnknownOption));
            ConsoleBridge.WriteLine();
            ConsoleBridge.WriteLine(CommandLine.BuildHelp());
            return 2;
        }

        if (commandLine.ShowHelp)
        {
            ConsoleBridge.Attach();
            ConsoleBridge.WriteLine(CommandLine.BuildHelp());
            return 0;
        }

        if (commandLine.ShowVersion)
        {
            ConsoleBridge.Attach();
            ConsoleBridge.WriteLine($"{AppInfo.Name} {AppInfo.VersionText}  ({AppInfo.FrameworkDescription})");
            return 0;
        }

        if (commandLine.CheckLanguages)
        {
            return LanguagePackCheck.Run();
        }

        // ---- tray application ------------------------------------------------------

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // A long-running tray process that dies silently is impossible to diagnose. Both the UI
        // thread and background threads get a handler that records the failure and says so.
        // SetUnhandledExceptionMode must run before the first window is created.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportUnhandled(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportUnhandled(e.ExceptionObject as Exception);

        // Check the mutex before touching settings: a second launch must be cheap and quiet.
        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            return 0;
        }

        var settingsService = new SettingsService();
        settingsService.Load();

        LocalizationService.Instance.Apply(ParseLanguage(settingsService.Current.Language), raiseEvent: false);
        AppLog.Configure(settingsService.Current.EnableLog);

        using var context = new TrayApplicationContext(settingsService, commandLine.OpenSettings);

        // ActivateRequested is raised on a thread-pool thread; the context marshals it onto
        // the UI thread itself, so no SynchronizationContext juggling is needed here.
        singleInstance.ActivateRequested += (_, _) => context.RequestOpenSettings();

        // No main window: the message loop runs on the tray context and lives until Exit.
        Application.Run(context);
        return 0;
    }

    /// <summary>
    /// Last-resort handler. Records the failure even when logging is switched off, then tells
    /// the user - a tray utility must not disappear without a word.
    /// </summary>
    private static void ReportUnhandled(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        AppLog.Always("unhandled exception", exception);

        try
        {
            MessageBox.Show(
                Loc.T("err.unhandled", exception.Message),
                Loc.T("err.unhandledTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Reporting must never be the thing that throws.
        }
    }

    private static AppLanguage ParseLanguage(string? code) =>
        AppLanguages.TryParseCode(code, out AppLanguage language) ? language : AppLanguage.System;
}
