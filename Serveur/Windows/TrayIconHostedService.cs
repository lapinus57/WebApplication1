using System.Diagnostics;
using System.Drawing;
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
        private readonly Action _updateAction;
        private readonly Action _exitAction;

        public TrayApplicationContext(
            Action restartAction,
            Action updateAction,
            Action exitAction,
            ILogger logger)
        {
            _restartAction = restartAction;
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
            _notifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
