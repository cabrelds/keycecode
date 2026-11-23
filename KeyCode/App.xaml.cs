using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace KeyCode;

/// <summary>
/// Point d'entrée de l'application WPF avec filets de sécurité contre les exceptions non gérées.
/// </summary>
public partial class App : Application
{
    private static readonly string _fichierJournal = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KeyCode", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private static void EnregistrerErreur(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fichierJournal)!);
            File.AppendAllText(_fichierJournal,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source})\n{ex}\n\n");
        }
        catch (Exception)
        {
            // Ignore les erreurs d'écriture du journal
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        EnregistrerErreur("UI", e.Exception);
        MessageBox.Show(
            $"Une erreur inattendue est survenue mais KeyCode reste ouvert :\n\n{e.Exception.Message}",
            "KeyCode - Erreur interceptée",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        EnregistrerErreur("AppDomain", e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        EnregistrerErreur("Task", e.Exception);
        e.SetObserved();
    }
}
