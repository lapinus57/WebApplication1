using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Client.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Client.Models;
using Client.Helpers;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System.IO;
using Client.Pages;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.UI.StartScreen;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace Client
{
    public partial class App : Application
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        public static SignalRService ChatService { get; } = new SignalRService();
        public static Window? MainWindow { get; private set; }
        public static string UserName { get; set; } = string.Empty;
        public static UserInfo? LastUserChanged { get; set; }
        public static HotKeyService HotKeys { get; } = new HotKeyService();
        private DispatcherQueueTimer? _agendaTimer;
        private bool _agendaSwitchInProgress;
        private bool _restartScheduled;
        private bool _forceCloseRequested;
        private EventWaitHandle? _forceCloseEvent;
        private RegisteredWaitHandle? _forceCloseWait;
        private const string ForceCloseArgument = "--force-close";
        private const string ForceCloseDisplayName = "\uE8BB  Forcer la fermeture";
        private const string ForceCloseEventName = @"Local\EyeChat.ForceClose";
        public App()
        {
            this.InitializeComponent();

            this.UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            AppSettings.SettingsChanged += async () =>
            {
                try
                {
                    if (ChatService.Connection != null && ChatService.Connection.State == HubConnectionState.Connected)
                    {
                        await ChatService.SaveUserSettingsAsync(UserName, AppSettings.Export());
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException("[App] SettingsChanged handler failed", ex, "CLI12");
                }
            };
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Depending on how the packaged desktop application is activated, Windows can
            // expose a jump-list argument either here or only on the process command line.
            // Check both before creating the main window; otherwise the helper activation
            // briefly becomes a second, fully initialized EyeChat instance.
            if (IsForceCloseActivation(args.Arguments, Environment.GetCommandLineArgs()))
            {
                SignalForceCloseAndExit();
                return;
            }

            Logger.Log($"[App] Démarrage d'EyeChat ({RuntimeInformation.ProcessArchitecture}, Windows {Environment.OSVersion.Version}).");

            try
            {
                m_window = new MainWindow();
                MainWindow = m_window;
                RegisterForceCloseRequest();

                var theme = AppSettings.Get("AppTheme", "Dark");
                if (Enum.TryParse<ApplicationTheme>(theme, out var appTheme))
                {
                    if (m_window.Content is FrameworkElement rootElement)
                    {
                        rootElement.RequestedTheme =
                            appTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
                    }
                }

                m_window.Closed += MainWindow_Closed;
                ChatService.Dispatcher = m_window.DispatcherQueue;
                ChatService.OnMessageReceived += ChatService_OnMessageReceived;
                // Register handler once the window root has loaded so XamlRoot is valid
                if (m_window.Content is FrameworkElement windowRoot)
                {
                    windowRoot.Loaded += MainWindow_Loaded;
                }
                // Show the window before optional integrations are initialized. A keyboard-hook
                // failure must not prevent EyeChat from opening on a newly configured computer.
                m_window.Activate();
                _ = RegisterForceCloseJumpListItemAsync();

                try
                {
                    HotKeys.Start();
                }
                catch (Exception ex)
                {
                    Logger.LogException("[App] Global keyboard shortcuts could not be initialized", ex, "CLI25");
                }

                Logger.Log("[App] Fenêtre principale activée.");
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] Startup failed", ex, "CLI26");
                ShowStartupError();
            }
        }

        private static void ShowStartupError()
        {
            var message =
                "EyeChat n'a pas pu démarrer.\n\n" +
                "Le diagnostic a été enregistré ici :\n" + Logger.LogPath + "\n\n" +
                "Transmettez ce fichier au support EyeChat (code CLI26).";

            try
            {
                MessageBox(IntPtr.Zero, message, "Erreur de démarrage EyeChat", 0x00000010);
            }
            catch
            {
                // Logging remains available even if Windows cannot display the fallback dialog.
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            HotKeys.Dispose();

            _forceCloseWait?.Unregister(null);
            _forceCloseWait = null;
            _forceCloseEvent?.Dispose();
            _forceCloseEvent = null;

            if (_restartScheduled || _forceCloseRequested)
                return;

            var config = MachineConfig.Load();
            if (!config.AutoRestartOnClose)
                return;

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            _restartScheduled = true;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] Auto restart failed", ex, "CLI25");
            }
        }

        public void RequestForceClose()
        {
            if (_forceCloseRequested)
                return;

            _forceCloseRequested = true;
            Logger.Log("[App] Fermeture forcée demandée : le redémarrage automatique est ignoré.");
            MainWindow?.Close();
        }

        internal static bool IsForceCloseActivation(string? launchArguments, IEnumerable<string> commandLineArguments)
        {
            if (ContainsForceCloseArgument(launchArguments))
                return true;

            return commandLineArguments.Any(argument =>
                string.Equals(argument.Trim().Trim('"'), ForceCloseArgument, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsForceCloseArgument(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return false;

            return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(argument => string.Equals(
                    argument.Trim().Trim('"'),
                    ForceCloseArgument,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void RegisterForceCloseRequest()
        {
            _forceCloseEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ForceCloseEventName);
            _forceCloseWait = ThreadPool.RegisterWaitForSingleObject(
                _forceCloseEvent,
                (_, timedOut) =>
                {
                    if (!timedOut)
                    {
                        MainWindow?.DispatcherQueue.TryEnqueue(RequestForceClose);
                    }
                },
                null,
                Timeout.Infinite,
                false);
        }

        private static void SignalForceCloseAndExit()
        {
            try
            {
                using var forceCloseEvent = EventWaitHandle.OpenExisting(ForceCloseEventName);
                forceCloseEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // There is no running EyeChat instance to close.
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        private static async Task RegisterForceCloseJumpListItemAsync()
        {
            try
            {
                var jumpList = await JumpList.LoadCurrentAsync();
                // JumpListItem only accepts an image URI for Logo; it cannot receive a
                // Segoe Fluent glyph as an icon. Put Windows' ChromeClose glyph directly
                // in the label instead, avoiding an extra bitmap in the application.
                foreach (var existingItem in jumpList.Items
                    .Where(item => string.Equals(
                        item.Arguments,
                        ForceCloseArgument,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList())
                {
                    jumpList.Items.Remove(existingItem);
                }

                var forceCloseItem = JumpListItem.CreateWithArguments(
                    ForceCloseArgument,
                    ForceCloseDisplayName);
                forceCloseItem.Description = "Ferme EyeChat sans le redémarrer automatiquement";
                jumpList.Items.Add(forceCloseItem);
                await jumpList.SaveAsync();
            }
            catch (Exception ex)
            {
                // Jump lists require a packaged Windows installation. The native system
                // menu remains available when running unpackaged or from Visual Studio.
                Logger.LogException("[App] Impossible d'ajouter l'action à la barre des tâches", ex, "CLI27");
            }
        }
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement root)
                return;

            root.Loaded -= MainWindow_Loaded;
            RegisterActivityHandlers(root);

            try
            {
                await InitializeMainWindowAsync(root);

                // Delay creation of ChatPage until the first-run dialogs are closed and
                // a user has been selected. Loading the page earlier starts its own async
                // XAML/SignalR initialization while a ContentDialog is active, which can
                // surface as an uncatchable Microsoft.UI.Xaml.dll stowed exception.
                if (!string.IsNullOrWhiteSpace(UserName) && MainWindow is MainWindow window)
                    window.ShowChatPage();
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] Main window initialization failed", ex, "CLI28");
                await ShowInitializationErrorAsync(root.XamlRoot);
            }
        }

        private async Task InitializeMainWindowAsync(FrameworkElement root)
        {
            var isFirstRun = !File.Exists(MachineConfig.FilePath)
                && !File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EyeChat", "users.json"));
            var machine = MachineConfig.Load();

            if (isFirstRun)
            {
                var imported = await PromptForInitialConfigurationAsync(root.XamlRoot, machine);
                if (imported)
                    machine = MachineConfig.Load();
            }

            if (string.IsNullOrWhiteSpace(machine.RoomName))
            {
                var dialog = new ContentDialog
                {
                    Title = "Nom de la salle",
                    PrimaryButtonText = "Valider",
                    XamlRoot = root.XamlRoot
                };

                var box = new TextBox();
                dialog.Content = box;
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    machine.RoomName = box.Text.Trim();
                    MachineConfig.Save(machine);
                }
            }

            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
            Directory.CreateDirectory(appFolder);
            var settingsFiles = Directory.GetFiles(appFolder, "*_settings.json");
            var validSettingsFiles = new List<string>();

            foreach (var file in settingsFiles)
            {
                var rawName = Path.GetFileNameWithoutExtension(file)?.Replace("_settings", string.Empty) ?? string.Empty;
                var sanitized = AppSettings.SanitizeUserNameForFile(rawName);
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException($"[App] Impossible de supprimer le fichier de paramètres invalide '{file}'", ex, "CLI24");
                    }

                    continue;
                }

                validSettingsFiles.Add(file);
            }

            settingsFiles = validSettingsFiles.ToArray();

            string? username = null;
            var scheduledUser = machine.GetAgendaUser(DateTime.Now);
            if (!string.IsNullOrWhiteSpace(scheduledUser))
            {
                username = scheduledUser;
            }
            if (settingsFiles.Length == 0 && string.IsNullOrWhiteSpace(username))
            {
                var requested = await PromptForUsernameAsync(root.XamlRoot);
                if (string.IsNullOrWhiteSpace(requested))
                    return;

                username = requested;

                if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                    machine.DefaultUser = username;
                machine.LastUser = username;
                machine.ConnectLastUser = false;
                MachineConfig.Save(machine);
            }
            else if (string.IsNullOrWhiteSpace(username))
            {
                var candidate = machine.ConnectLastUser
                    ? machine.LastUser
                    : machine.DefaultUser;

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    var fromFile = Path.GetFileNameWithoutExtension(settingsFiles[0])?
                        .Replace("_settings", string.Empty) ?? string.Empty;
                    candidate = AppSettings.SanitizeUserNameForFile(fromFile);

                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                            machine.DefaultUser = candidate;
                        if (string.IsNullOrWhiteSpace(machine.LastUser))
                            machine.LastUser = candidate;
                        MachineConfig.Save(machine);
                    }
                }

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = await PromptForUsernameAsync(root.XamlRoot);
                    if (string.IsNullOrWhiteSpace(candidate))
                        return;

                    if (string.IsNullOrWhiteSpace(machine.DefaultUser))
                        machine.DefaultUser = candidate;
                    machine.LastUser = candidate;
                    machine.ConnectLastUser = false;
                    MachineConfig.Save(machine);
                }

                username = candidate;
            }

            if (string.IsNullOrWhiteSpace(username))
                return;

            App.UserName = username;
            AppSettings.Reload();
            ApplySavedAppearance(root);
            AppSettings.CurrentSelectedUser = new UserInfo
            {
                Username = username,
                Avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/utilisateur.png")
            };

            if (!string.IsNullOrWhiteSpace(machine.RoomName) )
            {
                ChatService.RoomName = machine.RoomName;
                await ChatService.InitializeAsync();
                await SyncUserSettingsAsync(root);
                await DownloadMissingUserSettingsAsync();
            }

            RefreshAgendaTimer();
        }

        private static async Task ShowInitializationErrorAsync(XamlRoot? xamlRoot)
        {
            if (xamlRoot is null)
                return;

            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Initialisation impossible",
                    Content = new TextBlock
                    {
                        Text = "EyeChat n’a pas pu terminer son initialisation. " +
                               $"Le diagnostic a été enregistré dans {Logger.LogPath} (code CLI28).",
                        TextWrapping = TextWrapping.Wrap
                    },
                    CloseButtonText = "Fermer",
                    XamlRoot = xamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                // A dialog may already be closing when initialization fails. Logging the
                // secondary error is safer than allowing an async event handler to crash.
                Logger.LogException("[App] Initialization error dialog failed", ex, "CLI29");
            }
        }

        private static async Task<bool> PromptForInitialConfigurationAsync(XamlRoot xamlRoot, MachineConfig machine)
        {
            var welcome = new ContentDialog
            {
                Title = "Première configuration d’EyeChat",
                Content = new TextBlock
                {
                    Text = "Voulez-vous commencer à zéro ou importer une configuration préparée sur un autre ordinateur ?",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Importer un fichier",
                CloseButtonText = "Commencer à zéro",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            if (await welcome.ShowAsync() != ContentDialogResult.Primary)
                return false;

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".eyechatsetup");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return false;

            try
            {
                var configuration = JsonConvert.DeserializeObject<DeploymentConfiguration>(await FileIO.ReadTextAsync(file));
                var users = configuration?.Users?
                    .Where(user => !string.IsNullOrWhiteSpace(user.Username))
                    .GroupBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList() ?? new List<UserInfo>();
                var workstations = configuration?.Workstations?
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .ToList() ?? new List<DeploymentWorkstation>();

                if (configuration is null || users.Count == 0 || workstations.Count == 0)
                    throw new InvalidDataException("Le fichier doit contenir au moins un utilisateur et un poste.");

                var workstationName = await PromptForListSelectionAsync(
                    xamlRoot, "Choisir ce poste", "Poste", workstations.Select(item => item.Name).ToList());
                if (string.IsNullOrWhiteSpace(workstationName))
                    return false;

                var defaultUser = await PromptForListSelectionAsync(
                    xamlRoot, "Choisir l’utilisateur par défaut", "Utilisateur", users.Select(user => user.Username).ToList());
                if (string.IsNullOrWhiteSpace(defaultUser))
                    return false;

                var workstation = workstations.First(item =>
                    string.Equals(item.Name, workstationName, StringComparison.OrdinalIgnoreCase));
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                Directory.CreateDirectory(folder);
                foreach (var user in users.Where(user => string.IsNullOrWhiteSpace(user.Avatar)))
                    user.Avatar = UserInfo.DefaultAvatar;

                await File.WriteAllTextAsync(Path.Combine(folder, "users.json"),
                    JsonConvert.SerializeObject(users, Formatting.Indented));

                foreach (var user in users)
                {
                    var safeName = AppSettings.SanitizeUserNameForFile(user.Username);
                    if (string.IsNullOrWhiteSpace(safeName))
                        continue;
                    var settings = configuration.UserSettings?
                        .FirstOrDefault(entry => string.Equals(entry.Key, user.Username, StringComparison.OrdinalIgnoreCase))
                        .Value;
                    await File.WriteAllTextAsync(
                        Path.Combine(folder, $"{safeName}_settings.json"),
                        NormalizeImportedUserSettings(settings, user.Username));
                }

                ExamOption.Save(new ObservableCollection<ExamOption>(configuration.Exams ?? new List<ExamOption>()));
                RoomList.Save(new ObservableCollection<string>(configuration.Rooms ?? new List<string>()));

                machine.WorkstationName = workstation.Name;
                machine.DefaultUser = defaultUser;
                machine.LastUser = defaultUser;
                machine.ConnectLastUser = false;
                machine.ShiftF9Exam = workstation.ShiftF9Exam;
                machine.CtrlF9Exam = workstation.CtrlF9Exam;
                machine.ShiftF10Exam = workstation.ShiftF10Exam;
                machine.CtrlF10Exam = workstation.CtrlF10Exam;
                machine.ShiftF11Exam = workstation.ShiftF11Exam;
                machine.CtrlF11Exam = workstation.CtrlF11Exam;
                machine.ShiftF12Exam = workstation.ShiftF12Exam;
                machine.CtrlF12Exam = workstation.CtrlF12Exam;
                MachineConfig.Save(machine);
                return true;
            }
            catch (Exception ex)
            {
                var error = new ContentDialog
                {
                    Title = "Import impossible",
                    Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap },
                    CloseButtonText = "OK",
                    XamlRoot = xamlRoot
                };
                await error.ShowAsync();
                return false;
            }
        }

        private static string NormalizeImportedUserSettings(string? settings, string username)
        {
            var document = string.IsNullOrWhiteSpace(settings)
                ? new JObject()
                : JObject.Parse(settings);

            if (string.IsNullOrWhiteSpace(document.Value<string>("Avatar")))
                document["Avatar"] = UserInfo.DefaultAvatar;

            if (string.IsNullOrWhiteSpace(document.Value<string>("SelectedUser")))
                document["SelectedUser"] = username;

            return document.ToString(Formatting.Indented);
        }

        private static async Task<string?> PromptForListSelectionAsync(
            XamlRoot xamlRoot, string title, string placeholder, IReadOnlyList<string> values)
        {
            var combo = new ComboBox
            {
                ItemsSource = values,
                PlaceholderText = placeholder,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var dialog = new ContentDialog
            {
                Title = title,
                Content = combo,
                PrimaryButtonText = "Continuer",
                CloseButtonText = "Annuler",
                IsPrimaryButtonEnabled = false,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };
            combo.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = combo.SelectedItem is string;
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? combo.SelectedItem as string
                : null;
        }

        private void RegisterActivityHandlers(FrameworkElement root)
        {
            root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnUserActivity), true);
            root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnUserActivity), true);
            root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnUserActivity), true);
            root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnUserActivity), true);
        }

        private void OnUserActivity(object sender, RoutedEventArgs e)
        {
            ChatService.ReportUserActivity();
        }

        public void RefreshAgendaTimer()
        {
            var machine = MachineConfig.Load();
            var dispatcher = MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            if (dispatcher == null)
            {
                return;
            }

            if (!machine.AgendaModeEnabled || !machine.AutoSwitchEnabled)
            {
                void StopTimer() => _agendaTimer?.Stop();
                if (dispatcher.HasThreadAccess)
                {
                    StopTimer();
                }
                else
                {
                    dispatcher.TryEnqueue(StopTimer);
                }

                return;
            }

            void StartTimer()
            {
                if (_agendaTimer == null)
                {
                    _agendaTimer = dispatcher.CreateTimer();
                    _agendaTimer.Interval = TimeSpan.FromMinutes(1);
                    _agendaTimer.Tick += AgendaTimer_Tick;
                }

                _agendaTimer.Start();
            }

            if (dispatcher.HasThreadAccess)
            {
                StartTimer();
            }
            else
            {
                dispatcher.TryEnqueue(StartTimer);
            }
        }

        private async void AgendaTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            await ApplyAgendaSwitchAsync();
        }

        private async Task ApplyAgendaSwitchAsync()
        {
            if (_agendaSwitchInProgress)
            {
                return;
            }

            var machine = MachineConfig.Load();
            if (!machine.AgendaModeEnabled || !machine.AutoSwitchEnabled)
            {
                _agendaTimer?.Stop();
                return;
            }

            var scheduledUser = machine.GetAgendaUser(DateTime.Now);
            if (string.IsNullOrWhiteSpace(scheduledUser))
            {
                return;
            }

            if (string.Equals(scheduledUser, UserName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _agendaSwitchInProgress = true;
            try
            {
                await ChangeUserAsync(scheduledUser);
            }
            finally
            {
                _agendaSwitchInProgress = false;
            }
        }

        private static async Task<string?> PromptForUsernameAsync(XamlRoot xamlRoot)
        {
            while (true)
            {
                var userDialog = new ContentDialog
                {
                    Title = "Nom d'utilisateur",
                    PrimaryButtonText = "Valider",
                    XamlRoot = xamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                var userBox = new TextBox();
                userDialog.Content = userBox;
                userDialog.IsPrimaryButtonEnabled = false;

                userBox.TextChanged += (_, __) =>
                {
                    userDialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(userBox.Text);
                };

                var userResult = await userDialog.ShowAsync();
                if (userResult != ContentDialogResult.Primary)
                    return null;

                var username = userBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(username))
                    return username;
            }
        }

        public async Task<string?> PromptForAccountSelectionAsync()
        {
            if (MainWindow?.Content is not FrameworkElement root)
                return null;

            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
            Directory.CreateDirectory(appFolder);
            var settingsFiles = Directory.GetFiles(appFolder, "*_settings.json");
            var users = settingsFiles
                .Select(f => Path.GetFileNameWithoutExtension(f)?.Replace("_settings", string.Empty) ?? string.Empty)
                .Select(AppSettings.SanitizeUserNameForFile)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dialog = new ContentDialog
            {
                Title = "Choisir l'utilisateur",
                PrimaryButtonText = "OK",
                CloseButtonText = "Annuler",
                XamlRoot = root.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };
            var combo = new ComboBox { ItemsSource = users, PlaceholderText = "Utilisateur" };
            var newBox = new TextBox { PlaceholderText = "Nouvel utilisateur" };
            stack.Children.Add(combo);
            stack.Children.Add(newBox);
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return null;

            var name = !string.IsNullOrWhiteSpace(newBox.Text)
                ? newBox.Text.Trim()
                : combo.SelectedItem as string;

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        public static void ApplySavedAppearance(FrameworkElement root)
        {
            var theme = AppSettings.Get("AppTheme", "Dark");
            if (Enum.TryParse<ApplicationTheme>(theme, out var appTheme))
            {
                root.RequestedTheme =
                    appTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
            }

            var colors = AppSettings.GetObject<AppColorSettings>("Colors");

            var titleBar = root.FindName("AppTitleBar") as Grid;
            var nav = root.FindName("nvSample") as NavigationView;
            var titleText = root.FindName("TitleBarTextBlock") as TextBlock;
            if (root.FindName("PersonPic") is PersonPicture pic)
            {
                var initials = AppSettings.Get("Initials", string.Empty);
                if (string.IsNullOrWhiteSpace(initials))
                {
                    initials = string.Concat(App.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => char.ToUpperInvariant(s[0])));
                }
                pic.Initials = initials;

            }

            AppearanceSettingsPage.ApplyColors(colors, titleBar, nav, titleText);
        }

        private void ChatService_OnMessageReceived(ChatMessageModel chat)
        {
            if (MainWindow is not MainWindow mw)
                return;

            bool isForeground = WindowHelper.IsForeground(mw);
            bool isChat = mw.IsChatPageActive;

            if (!isForeground)
            {
                mw.DispatcherQueue.TryEnqueue(() =>
                {
                    mw.ShowChatPage();
                    mw.BringToForeground();
                    mw.ScrollMessagesToEnd();
                });
            }
            else if (mw.IsTopMost)
            {
                if (isChat)
                {
                    mw.DispatcherQueue.TryEnqueue(mw.ScrollMessagesToEnd);
                }
                else
                {
                    mw.DispatcherQueue.TryEnqueue(() => ShowNotification(chat));
                }
            }
            else
            {
                if (isChat)
                {
                    mw.DispatcherQueue.TryEnqueue(mw.ScrollMessagesToEnd);
                }
                else
                {
                    mw.DispatcherQueue.TryEnqueue(() => ShowNotification(chat));
                }
            }
        }

        private static void ShowNotification(ChatMessageModel chat)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText("EyeChat")
                    .AddText($"{chat.Sender}: {chat.Content}")
                    .BuildNotification();

                AppNotificationManager.Default.Register();
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] ShowNotification failed", ex, "CLI13");
            }
        }

        private async Task SyncUserSettingsAsync(FrameworkElement root)
        {
            try
            {
                var json = await ChatService.GetUserSettingsAsync(App.UserName);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    AppSettings.Import(json);
                    ApplySavedAppearance(root);
                }
                else
                {
                    var local = AppSettings.Export();
                    if (!string.IsNullOrWhiteSpace(local))
                        await ChatService.SaveUserSettingsAsync(App.UserName, local);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] SyncUserSettingsAsync failed", ex, "CLI14");
            }
        }

        private async Task DownloadMissingUserSettingsAsync()
        {
            try
            {
                var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                Directory.CreateDirectory(appFolder);
                var localUsers = Directory.GetFiles(appFolder, "*_settings.json")
                    .Select(f => Path.GetFileNameWithoutExtension(f)?.Replace("_settings", string.Empty) ?? string.Empty)
                    .Select(AppSettings.SanitizeUserNameForFile)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
                var missing = await ChatService.GetMissingUserSettingsAsync(localUsers);
                foreach (var kvp in missing)
                {
                    var sanitized = AppSettings.SanitizeUserNameForFile(kvp.Key ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(sanitized))
                    {
                        continue;
                    }

                    var path = Path.Combine(appFolder, $"{sanitized}_settings.json");
                    File.WriteAllText(path, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] DownloadMissingUserSettingsAsync failed", ex, "CLI15");
            }
        }

        public async Task ChangeUserAsync(string username)
        {
            if (MainWindow?.Content is not FrameworkElement root)
                return;

            try
            {
                await ChatService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Logger.LogException("[App] ChangeUserAsync.DisconnectAsync failed", ex, "CLI16");
            }

            ChatService.ClearLocalData();

            UserName = username;
            AppSettings.Reload();
            ApplySavedAppearance(root);
            AppSettings.CurrentSelectedUser = new UserInfo
            {
                Username = username,
                Avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/utilisateur.png")
            };

            var machine = MachineConfig.Load();
            machine.LastUser = username;
            MachineConfig.Save(machine);

            await ChatService.InitializeAsync();
            await SyncUserSettingsAsync(root);
            await DownloadMissingUserSettingsAsync();

            if (MainWindow is MainWindow mw)
            {
                var chat = mw.ShowChatPage();
                chat?.RefreshUsername();
                mw.SetAccountState(true);
                mw.RefreshAgendaSwitchState();
            }

            RefreshAgendaTimer();
        }

        public async Task LogoutAsync()
        {
            try
            {
                await ChatService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Logger.LogException("LogoutAsync.DisconnectAsync failed", ex, "CLI09");
            }

            ChatService.ClearLocalData();

            if (MainWindow is MainWindow mw)
            {
                mw.SetAccountState(false);
            }
        }

        private Window? m_window;

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            if (e.Exception is Exception ex)
            {
                Logger.LogException("Unhandled UI exception", ex, "CLI01");
            }
            else
            {
                Logger.Log("Unhandled UI exception without an Exception instance.");
            }

            e.Handled = true;

            if (MainWindow is MainWindow window && window.Content is FrameworkElement root)
            {
                window.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Erreur inattendue",
                            Content = "Une erreur est survenue. Un journal a été enregistré dans les données locales.",
                            CloseButtonText = "Fermer",
                            XamlRoot = root.XamlRoot
                        };

                        await dialog.ShowAsync();
                    }
                    catch
                    {
                        // Ignore dialog errors – the dispatcher may not be available during shutdown.
                    }
                });
            }
        }

        private void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.LogException("AppDomain unhandled exception", ex, "CLI02");
            }
            else
            {
                Logger.Log($"AppDomain unhandled exception object: {e.ExceptionObject}");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.LogException("Unobserved task exception", e.Exception, "CLI03");
            e.SetObserved();
        }
    }
}
