using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatServeur;

/// <summary>
/// Hosted service responsible for exposing the server status through a Windows system tray icon.
/// </summary>
public sealed class TrayIconHostedService : IHostedService, IDisposable
{
    private readonly ILogger<TrayIconHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    private readonly TaskCompletionSource _initializationCompletion = new();
    private SynchronizationContext? _uiContext;
    private TrayApplicationContext? _trayContext;
    private Thread? _uiThread;
    private int _isCleaningBackups;

    public TrayIconHostedService(ILogger<TrayIconHostedService> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _lifetime = lifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _uiThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "TrayIconThread"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        using (cancellationToken.Register(() => _initializationCompletion.TrySetCanceled(cancellationToken)))
        {
            await _initializationCompletion.Task.ConfigureAwait(false);
        }

        SetStatus(TrayStatus.Starting);

        _lifetime.ApplicationStarted.Register(() => SetStatus(TrayStatus.Running));
        _lifetime.ApplicationStopping.Register(() => SetStatus(TrayStatus.Stopping));
        _lifetime.ApplicationStopped.Register(() => SetStatus(TrayStatus.Stopped));

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_uiContext is null || _trayContext is null)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource();
        _uiContext.Post(_ =>
        {
            try
            {
                _trayContext.Dispose();
                Application.ExitThread();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);

        return completion.Task;
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_trayContext is not null)
        {
            _uiContext?.Post(_ => _trayContext?.Dispose(), null);
        }

        if (_uiThread is not null && _uiThread.IsAlive)
        {
            _uiThread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void RunMessageLoop()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var syncContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            _uiContext = syncContext;

            var context = new TrayApplicationContext(
                RestartApplication,
                TriggerBackupCleanup,
                LaunchUpdateWorkflow,
                ExitApplication,
                _logger);

            _trayContext = context;
            syncContext.Post(_ => context.Initialize(), null);
            _initializationCompletion.TrySetResult();

            Application.Run(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'initialisation de l'icône de notification.");
            _initializationCompletion.TrySetException(ex);
        }
    }

    private void SetStatus(TrayStatus status)
    {
        PostToUi(() => _trayContext?.SetStatus(status));
    }

    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'exécution d'une action sur le thread UI.");
            }
        }, null);
    }

    private void RestartApplication()
    {
        Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Redémarrage manuel demandé depuis l'icône de notification.");

                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    var args = Environment.GetCommandLineArgs().Skip(1)
                        .Select(QuoteArgument);
                    var arguments = string.Join(" ", args);

                    var startInfo = new ProcessStartInfo(processPath)
                    {
                        UseShellExecute = true,
                        Arguments = arguments
                    };

                    Process.Start(startInfo);
                }

                _lifetime.StopApplication();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du redémarrage de l'application.");
                PostToUi(() => _trayContext?.ShowBalloon(
                    "Redémarrage",
                    "Impossible de redémarrer le serveur. Consultez les journaux."));
            }
        });
    }

    private void LaunchUpdateWorkflow()
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Sélectionnez le dossier contenant la nouvelle version du serveur"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var scriptPath = Path.Combine(AppContext.BaseDirectory, "Deployment", "ChatServeurService.ps1");
            if (!File.Exists(scriptPath))
            {
                PostToUi(() => _trayContext?.ShowBalloon(
                    "Mise à jour",
                    "Script de mise à jour introuvable."));
                return;
            }

            var arguments = $"-NoExit -ExecutionPolicy Bypass -File \"{scriptPath}\" -Action Update -SourcePath \"{dialog.SelectedPath}\"";
            var startInfo = new ProcessStartInfo("powershell.exe", arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)
            };

            Process.Start(startInfo);
            PostToUi(() => _trayContext?.ShowBalloon(
                "Mise à jour",
                "La fenêtre de mise à jour a été ouverte."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du lancement de la mise à jour.");
            PostToUi(() => _trayContext?.ShowBalloon(
                "Mise à jour",
                "Impossible de lancer la mise à jour. Consultez les journaux."));
        }
    }

    private void TriggerBackupCleanup()
    {
        if (Interlocked.Exchange(ref _isCleaningBackups, 1) == 1)
        {
            PostToUi(() => _trayContext?.ShowBalloon(
                "Nettoyage des sauvegardes",
                "Un nettoyage est déjà en cours."));
            return;
        }

        Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Nettoyage des sauvegardes demandé depuis l'icône de notification.");
                PostToUi(() => _trayContext?.ShowBalloon(
                    "Nettoyage des sauvegardes",
                    "Analyse des fichiers de sauvegarde..."));

                var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                var backups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (Directory.Exists(appFolder))
                {
                    var patterns = new[] { "*.bak", "*.bak*", "*.backup", "*.backup*" };
                    foreach (var pattern in patterns)
                    {
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(appFolder, pattern, SearchOption.AllDirectories))
                            {
                                backups.Add(file);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Erreur lors de l'énumération des sauvegardes avec le motif {Pattern}", pattern);
                        }
                    }
                }

                if (backups.Count == 0)
                {
                    PostToUi(() => _trayContext?.ShowBalloon(
                        "Nettoyage des sauvegardes",
                        "Aucune sauvegarde à supprimer."));
                    return;
                }

                int deleted = 0;
                int failed = 0;
                long freedBytes = 0;

                foreach (var file in backups)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Exists)
                        {
                            freedBytes += info.Length;
                        }

                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogWarning(ex, "Impossible de supprimer la sauvegarde {File}", file);
                    }
                }

                var summary = $"Suppression terminée. {deleted} fichier(s) supprimé(s)";
                if (freedBytes > 0)
                {
                    summary += $" ({FormatBytes(freedBytes)} libérés)";
                }
                summary += ".";

                if (failed > 0)
                {
                    summary += $" {failed} fichier(s) n'ont pas pu être supprimés.";
                }

                _logger.LogInformation(
                    "Nettoyage des sauvegardes terminé : {Deleted} supprimé(s), {Failed} échec(s), {Freed} octets libérés.",
                    deleted,
                    failed,
                    freedBytes);

                PostToUi(() => _trayContext?.ShowBalloon(
                    "Nettoyage des sauvegardes",
                    summary));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur inattendue lors du nettoyage des sauvegardes.");
                PostToUi(() => _trayContext?.ShowBalloon(
                    "Nettoyage des sauvegardes",
                    $"Erreur : {ex.Message}"));
            }
            finally
            {
                Interlocked.Exchange(ref _isCleaningBackups, 0);
            }
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "octets", "Ko", "Mo", "Go", "To" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{size:0.##} {units[unitIndex]}";
    }

    private void ExitApplication()
    {
        Task.Run(() =>
        {
            _logger.LogInformation("Arrêt manuel demandé depuis l'icône de notification.");
            _lifetime.StopApplication();
        });
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.ExceptionObject as Exception, "Exception non gérée détectée.");
        SetStatus(TrayStatus.Error);
        PostToUi(() => _trayContext?.ShowBalloon(
            "Erreur",
            "Une erreur critique est survenue. Consultez les journaux."));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Exception asynchrone non observée détectée.");
        SetStatus(TrayStatus.Error);
        PostToUi(() => _trayContext?.ShowBalloon(
            "Erreur",
            "Une erreur asynchrone est survenue. Consultez les journaux."));
    }

    private static string QuoteArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        return arg.Contains(' ') || arg.Contains('\"')
            ? $"\"{arg.Replace("\"", "\\\"")}\""
            : arg;
    }

    private sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon _runningIcon;
        private readonly Icon _stoppedIcon;
        private readonly Icon _startingIcon;
        private readonly Icon _errorIcon;
        private readonly ILogger _logger;
        private readonly Action _restartAction;
        private readonly Action _cleanupAction;
        private readonly Action _updateAction;
        private readonly Action _exitAction;
        private bool _isDisposed;

        public TrayApplicationContext(
            Action restartAction,
            Action cleanupAction,
            Action updateAction,
            Action exitAction,
            ILogger logger)
        {
            _restartAction = restartAction;
            _cleanupAction = cleanupAction;
            _updateAction = updateAction;
            _exitAction = exitAction;
            _logger = logger;

            _runningIcon = CreateStatusIcon(Color.LimeGreen);
            _stoppedIcon = CreateStatusIcon(Color.Firebrick);
            _startingIcon = CreateStatusIcon(Color.Orange);
            _errorIcon = CreateStatusIcon(Color.DarkRed);

            _notifyIcon = new NotifyIcon
            {
                Icon = _startingIcon,
                Text = "ChatServeur - démarrage..."
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Redémarrer", null, (_, _) => _restartAction());
            menu.Items.Add("Nettoyer les sauvegardes...", null, (_, _) => _cleanupAction());
            menu.Items.Add("Mettre à jour...", null, (_, _) => _updateAction());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Arrêter", null, (_, _) => _exitAction());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, _) => ShowBalloon("État du serveur", _notifyIcon.Text);
        }

        public void Initialize()
        {
            try
            {
                _notifyIcon.Visible = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible d'afficher l'icône de notification.");
            }
        }

        public void SetStatus(TrayStatus status)
        {
            switch (status)
            {
                case TrayStatus.Running:
                    UpdateIcon(_runningIcon, "ChatServeur - en ligne");
                    break;
                case TrayStatus.Starting:
                    UpdateIcon(_startingIcon, "ChatServeur - démarrage...");
                    break;
                case TrayStatus.Stopping:
                    UpdateIcon(_startingIcon, "ChatServeur - arrêt en cours...");
                    break;
                case TrayStatus.Stopped:
                    UpdateIcon(_stoppedIcon, "ChatServeur - arrêté");
                    break;
                case TrayStatus.Error:
                    UpdateIcon(_errorIcon, "ChatServeur - erreur");
                    break;
            }
        }

        public void ShowBalloon(string title, string message)
        {
            if (_isDisposed)
            {
                _logger.LogDebug("Notification ignorée car l'icône a été supprimée.");
                return;
            }
            _notifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _isDisposed = true;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _runningIcon.Dispose();
                _stoppedIcon.Dispose();
                _startingIcon.Dispose();
                _errorIcon.Dispose();
            }

            base.Dispose(disposing);
        }

        private void UpdateIcon(Icon icon, string text)
        {
            if (_isDisposed)
            {
                _logger.LogDebug("Demande de mise à jour ignorée car l'icône a été supprimée.");
                return;
            }

            if (icon is null)
            {
                _logger.LogWarning("Icône de statut nulle reçue. Aucune mise à jour effectuée.");
                return;
            }
            try
            {
                _notifyIcon.Icon = icon;
                _notifyIcon.Text = text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de mettre à jour l'icône de notification.");
            }
        }

        private static Icon CreateStatusIcon(Color color)
        {
            using var bitmap = new Bitmap(16, 16);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, new Rectangle(0, 0, 15, 15));

            var hIcon = bitmap.GetHicon();
            try
            {
                using var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
    }

    private enum TrayStatus
    {
        Starting,
        Running,
        Stopping,
        Stopped,
        Error
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
