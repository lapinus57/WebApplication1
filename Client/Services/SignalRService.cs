using Client.Models;
using Client.Helpers;
using Client;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.Chat;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.AppNotifications;
using Client.Pages;
using System.IO;

namespace Client.Services
{
    public class SignalRService
    {
        public HubConnection Connection { get; private set; }
        public DispatcherQueue Dispatcher { get; set; }
        public ObservableCollection<UserInfo> ConnectedUsers { get; } = new();
        public ObservableCollection<Patient> Patients { get; } = new();
        public ObservableCollection<object> Messages { get; } = new();
        public string RoomName { get; set; } = string.Empty;

        private bool _initialized;
        private bool _historyLoaded;
        private string _username = string.Empty;
        private string _avatar = string.Empty;
        private string _color = string.Empty;
        private List<UserInfo> _lastServerUserList = new();
        private Timer? _reconnectTimer;
        private Timer? _reconnectCountdownTimer;
        private int _reconnectCountdown;
        public event Action<int>? ReconnectCountdownChanged;
        private bool _isConnecting;
        public bool EnableReconnect { get; set; } = true;

        public bool IsHistoryLoaded => _historyLoaded;
        public IReadOnlyList<UserInfo> KnownUsers => _lastServerUserList;

        private string _serverAddress = string.Empty;
        public string ServerAddress
        {
            get => _serverAddress;
            set => _serverAddress = NormalizeServerAddress(value);
        }

        public SignalRService()
        {
            var cfg = ConnectionConfig.Load();
            ServerAddress = cfg.ServerAddress;
            var machine = MachineConfig.Load();
            RoomName = machine.RoomName;
        }

        public event Action<ChatMessageModel>? OnMessageReceived;
        public event Func<string, string, Task>? OnCallReceived;
        public event Action<Patient>? OnNewPatient;
        public event Action<string>? OnPatientRemoved;
        public event Action<Patient>? OnPatientUpdated;
        public event Action<IEnumerable<ExamOption>>? ExamOptionsUpdated;
        public event Action<IEnumerable<string>>? RoomsUpdated;

        private static readonly JsonSerializerSettings CamelCaseSettings = new()
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        };

        private static string NormalizeServerAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return string.Empty;

            address = address.Trim();

            if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                address = "http://" + address;
            }

            return address.TrimEnd('/');
        }

        private string ToServerAvatar(string avatar)
        {
            if (string.IsNullOrWhiteSpace(avatar))
                return avatar;
            try
            {
                var serverUri = new Uri(ServerAddress);
                var uri = new Uri(avatar, UriKind.RelativeOrAbsolute);
                if (uri.IsAbsoluteUri && uri.Host == serverUri.Host && uri.Port == serverUri.Port)
                    return uri.PathAndQuery;
            }
            catch
            {
            }
            return avatar;
        }

        private string ToClientAvatar(string avatar)
        {
            if (string.IsNullOrWhiteSpace(avatar))
                return avatar;
            if (Uri.TryCreate(avatar, UriKind.RelativeOrAbsolute, out var uri))
            {
                if (!uri.IsAbsoluteUri && avatar.StartsWith("/"))
                    return $"{ServerAddress}{avatar}";
            }
            return avatar;
        }

        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            _initialized = true;

            if (string.IsNullOrWhiteSpace(ServerAddress) || ServerAddress.Contains("localhost"))
            {
                var detected = await NetworkScanner.FindServerAsync();
                if (!string.IsNullOrEmpty(detected))
                {
                    ServerAddress = detected;
                    ConnectionConfig.Save(new ConnectionConfig { ServerAddress = ServerAddress });
                }
                else if (ServerAddress.Contains("localhost"))
                {
                    ServerAddress = string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(ServerAddress))
                return;

            var cachedUsers = await LoadUsersFromDiskAsync();
            foreach (var user in cachedUsers)
            {
                user.IsOnline = false;
                user.Rooms.Clear();
                user.Avatar = ToClientAvatar(user.Avatar);
                ConnectedUsers.Add(user);
            }
            UpdateSpecialUsers();

            var color = AppSettings.Get("ColorUserName", "Black");
            var avatar = AppSettings.Get("Avatar", "ms-appx:///Assets/earth.png");
            await ConnectAsync(App.UserName, avatar, RoomName, color);


            var serverUsers = await GetAllUsersAsync();
            if (serverUsers.Count > 0)
            {
                var processed = serverUsers.Select(u =>
                {
                    u.Avatar = ToClientAvatar(u.Avatar);
                    return u;
                }).ToList();
                var finalList = await BuildUserListWithGroupsAsync(processed);
                ConnectedUsers.Clear();
                foreach (var u in finalList)
                    ConnectedUsers.Add(u);
                UpdateSpecialUsers();
            }

            Messages.Clear();
            Messages.Add(new LoadMorePlaceholder());

            var cached = await LoadTodayMessagesFromDiskAsync();
            foreach (var msg in cached)
            {
                msg.Avatar = ToClientAvatar(msg.Avatar);
                Messages.Add(msg);
            }

            var result = await LoadTodayMessagesAsync(App.UserName);
            if (result.Success)
            {
                foreach (var item in Messages.OfType<ChatMessageModel>().ToList())
                    Messages.Remove(item);
                foreach (var msg in result.Value)
                    Messages.Add(msg);

                _historyLoaded = true;
                await SaveTodayMessagesToDiskAsync();
            }
            var patients = await GetPatientsAsync();
            Patients.Clear();
            foreach (var p in patients)
                Patients.Add(p);
        }

        public async Task ConnectAsync(string username, string avatar, string room, string color)
        {
            if (_isConnecting)
                return;
            _isConnecting = true;
            EnableReconnect = true;

            _username = username;
            _avatar = avatar;
            _color = color;

            if (Connection != null)
            {
                try
                {
                    await Connection.StopAsync();
                    await Connection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error disposing previous connection: {ex.Message}");
                }
            }

            Connection = new HubConnectionBuilder()
                .WithUrl($"{ServerAddress}/chatHub")
                .Build();

            Connection.Closed += async (error) =>
            {
                if (EnableReconnect)
                {
                    try
                    {
                        AppNotification notification = new AppNotificationBuilder()
                            .AddText("Serveur indisponible")
                            .AddText("Le serveur est actuellement injoignable.")
                            .SetDuration(AppNotificationDuration.Long)
                            .BuildNotification();

                        AppNotificationManager.Default.Show(notification);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"❌ Toast error: {ex.Message}");
                    }
                }

                foreach (var u in ConnectedUsers)
                {
                    u.IsOnline = false;
                    u.Rooms.Clear();
                }
                UpdateSpecialUsers();
                await SaveUsersToDiskAsync();
                await Task.Delay(3000);
                if (EnableReconnect)
                    StartReconnectTimer();
            };

            Connection.On<UserInfo>("UserConnected", user =>
            {
                Dispatcher?.TryEnqueue(async () =>
                {
                    user.Avatar = ToClientAvatar(user.Avatar);
                    var existing = ConnectedUsers.FirstOrDefault(u => u.Username == user.Username);
                    if (existing != null)
                    {
                        var index = ConnectedUsers.IndexOf(existing);
                        ConnectedUsers[index] = user;
                    }
                    else
                    {
                        ConnectedUsers.Add(user);
                    }
                    UpdateSpecialUsers();
                    Debug.WriteLine($"✅ User connecté : {user.Username}");
                    await SaveUsersToDiskAsync();
                });
            });


            Connection.On<List<UserInfo>>("UserListUpdated", users =>
            {
                Dispatcher?.TryEnqueue(async () =>
                {
                    var processed = users.Select(u =>
                    {
                        u.Avatar = ToClientAvatar(u.Avatar);
                        return u;
                    }).ToList();

                    if (AreUserListsEqual(processed))
                        return;

                    _lastServerUserList = processed.ToList();

                    var finalList = await BuildUserListWithGroupsAsync(processed);

                    ConnectedUsers.Clear();
                    foreach (var user in finalList)
                    {
                        ConnectedUsers.Add(user);
                        Debug.WriteLine($"✅ User list ajoutée : {user.Username} ({string.Join(", ", user.Rooms)})");
                    }
                    UpdateSpecialUsers();

                    foreach (var msg in Messages.OfType<ChatMessageModel>())
                    {
                        msg.SenderColor = ConnectedUsers.FirstOrDefault(u => u.Username == msg.Sender)?.ColorUserName ?? "Black";
                        msg.DestinataireColor = ConnectedUsers.FirstOrDefault(u => u.Username == msg.Destinataire)?.ColorUserName ?? "Black";
                    }

                    await SaveUsersToDiskAsync();
                });
            });

            Connection.On<List<string>>("UserListOrder", order =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    var sorted = ConnectedUsers
                        .OrderBy(u =>
                        {
                            int index = order.IndexOf(u.Username);
                            return index >= 0 ? index : int.MaxValue;
                        }).ToList();

                    ConnectedUsers.Clear();
                    foreach (var u in sorted)
                        ConnectedUsers.Add(u);
                });
            });

            Connection.On<int, string, string, string, string, string, DateTime>("ReceiveMessage", (id, user, room, destinataire, msg, avatar, time) =>
            {
                var senderColor = ConnectedUsers.FirstOrDefault(u => u.Username == user)?.ColorUserName ?? "Black";
                var destColor = ConnectedUsers.FirstOrDefault(u => u.Username == destinataire)?.ColorUserName ?? "Black";
                var chat = new ChatMessageModel
                {
                    Id = id,
                    Sender = user,
                    Destinataire = destinataire,
                    Room = room,
                    Content = msg,
                    Timestamp = time,
                    Avatar = ToClientAvatar(avatar),
                    SenderColor = senderColor,
                    DestinataireColor = destColor
                };

                Debug.WriteLine($"✅ Message reçu de {user} dans la salle {room} : {msg} ({time})");

                Dispatcher?.TryEnqueue(async () =>
                {
                    Messages.Add(chat);
                    OnMessageReceived?.Invoke(chat);
                    await SaveTodayMessagesToDiskAsync();
                });
            });

            Connection.On<string, string>("ReceiveCall", (caller, room) =>
            {
                Dispatcher?.TryEnqueue(async () =>
                {
                    if (OnCallReceived != null)
                        await OnCallReceived(caller, room);
                });
            });

            Connection.On<int>("MessageDeleted", id =>
            {
                Dispatcher?.TryEnqueue(async () =>
                {
                    var message = Messages.OfType<ChatMessageModel>().FirstOrDefault(m => m.Id == id);
                    if (message != null)
                        Messages.Remove(message);

                    await SaveTodayMessagesToDiskAsync();
                });
            });


            Connection.On<Patient>("NewPatient", patient =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    Patients.Add(patient);
                    OnNewPatient?.Invoke(patient);
                });
            });

            Connection.On<string>("PatientRemoved", id =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    var existing = Patients.FirstOrDefault(p => p.Id == id);
                    if (existing != null)
                        Patients.Remove(existing);
                    OnPatientRemoved?.Invoke(id);
                });
            });

            Connection.On<Patient>("PatientUpdated", patient =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    var existing = Patients.FirstOrDefault(p => p.Id == patient.Id);
                    if (existing != null)
                    {
                        var index = Patients.IndexOf(existing);
                        Patients[index] = patient;
                    }
                    else
                    {
                        Patients.Add(patient);
                    }
                    OnPatientUpdated?.Invoke(patient);
                });
            });

            Connection.On<IEnumerable<ExamOption>>("ExamOptionsUpdated", opts =>
            {
                ExamOptionsUpdated?.Invoke(opts);
            });

            Connection.On<IEnumerable<string>>("RoomsUpdated", rooms =>
            {
                RoomsUpdated?.Invoke(rooms);
            });

            Connection.On<string, string>("UserSettingsUpdated", (username, json) =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    try
                    {
                        var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                        Directory.CreateDirectory(appFolder);
                        var path = Path.Combine(appFolder, $"{username}_settings.json");
                        File.WriteAllText(path, json);

                        if (username == App.UserName)
                        {
                            AppSettings.Import(json);
                            if (App.MainWindow?.Content is FrameworkElement root)
                                App.ApplySavedAppearance(root);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erreur MAJ paramètres : {ex.Message}");
                    }
                });
            });

            try
            {
                await Connection.StartAsync();
                await Connection.InvokeAsync("RegisterUser", username, ToServerAvatar(avatar), room, color);
                StopReconnectTimer();
            }
            catch (Exception ex)
            {
                try
                {
                    AppNotification notification = new AppNotificationBuilder()
                        .AddText("Eyechat")
                        .AddText($"Erreur de connexion au serveur SignalR {ex.Message}")
                        .BuildNotification();

                    AppNotificationManager.Default.Show(notification);
                }
                catch (Exception toastEx)
                {
                    Debug.WriteLine($"❌ Toast error: {toastEx.Message}");
                }
                if (EnableReconnect)
                    StartReconnectTimer();
            }

            _isConnecting = false;

        }

        public async Task SendMessage(string sender, string roomname, string destinataire, string message, string avatar, DateTime timemessage)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");
            await Connection.InvokeAsync("SendMessage", sender, roomname, destinataire, message, ToServerAvatar(avatar), timemessage);

        }

        public async Task CallUser(string sender, string roomname, string destinataire)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");
            await Connection.InvokeAsync("CallUser", sender, roomname, destinataire);
        }

        public async Task<Result<List<ChatMessageModel>>> LoadTodayMessagesAsync(string username)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected)
                    return Result<List<ChatMessageModel>>.Fail("Serveur injoignable.");
            }

            try
            {
                var raw = await Connection.InvokeAsync<List<ChatMessageModel>>("GetTodayMessages", username);

                var messages = raw.Select(m => new ChatMessageModel
                {
                    Id = m.Id,
                    Sender = m.Sender,
                    Destinataire = m.Destinataire,
                    Room = m.Room,
                    Content = m.Content,
                    Avatar = ToClientAvatar(m.Avatar),
                    Timestamp = m.Timestamp,
                    IsDeleted = m.IsDeleted,
                    SenderColor = ConnectedUsers.FirstOrDefault(u => u.Username == m.Sender)?.ColorUserName ?? "Black",
                    DestinataireColor = ConnectedUsers.FirstOrDefault(u => u.Username == m.Destinataire)?.ColorUserName ?? "Black"
                }).ToList();

                return Result<List<ChatMessageModel>>.Ok(messages);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erreur lors du chargement des messages : {ex.Message}");
                ShowToast("Impossible de récupérer les messages du jour.");
                return Result<List<ChatMessageModel>>.Fail("Erreur interne.");
            }
        }
        public async Task<Result<List<ChatMessageModel>>> LoadMessagesForDateAsync(string username, DateTime date)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                return Result<List<ChatMessageModel>>.Fail("Déconnecté");

            try
            {
                var raw = await Connection.InvokeAsync<List<ChatMessageModel>>("GetMessagesForDate", username, date);

                return Result<List<ChatMessageModel>>.Ok(
                    raw.Select(m => new ChatMessageModel
                    {
                        Id = m.Id,
                        Sender = m.Sender,
                        Destinataire = m.Destinataire,
                        Room = m.Room,
                        Content = m.Content,
                        Avatar = ToClientAvatar(m.Avatar),
                        Timestamp = m.Timestamp,
                        IsDeleted = m.IsDeleted,
                        SenderColor = ConnectedUsers.FirstOrDefault(u => u.Username == m.Sender)?.ColorUserName ?? "Black",
                        DestinataireColor = ConnectedUsers.FirstOrDefault(u => u.Username == m.Destinataire)?.ColorUserName ?? "Black"
                    }).ToList()
                );
            }
            catch (Exception ex)
            {
                return Result<List<ChatMessageModel>>.Fail(ex.Message);
            }
        }
        private async Task<bool> TryReconnectAsync()
        {
            if (!EnableReconnect)
                return false;
            try
            {
                if (Connection == null)
                    return false;

                if (Connection.State == HubConnectionState.Disconnected)
                {
                    await Connection.StartAsync();
                    Debug.WriteLine("🔄 Reconnexion réussie.");
                }

                return Connection.State == HubConnectionState.Connected;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Reconnexion échouée : {ex.Message}");
                return false;
            }
        }

        private void StartReconnectTimer()
        {
            if (!EnableReconnect)
                return;
            _reconnectTimer?.Dispose();
            _reconnectCountdownTimer?.Dispose();

            _reconnectCountdown = 10;
            ReconnectCountdownChanged?.Invoke(_reconnectCountdown);

            _reconnectCountdownTimer = new Timer(_ =>
            {
                _reconnectCountdown--;
                if (_reconnectCountdown >= 0)
                    ReconnectCountdownChanged?.Invoke(_reconnectCountdown);
                if (_reconnectCountdown <= 0)
                    _reconnectCountdown = 10;
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            _reconnectTimer = new Timer(async _ =>
            {
                await ReconnectAsync();
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        private void StopReconnectTimer()
        {
            _reconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            _reconnectCountdownTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _reconnectCountdownTimer?.Dispose();
            _reconnectCountdownTimer = null;
            ReconnectCountdownChanged?.Invoke(0);
        }

        public async Task ReconnectAsync()
        {
            if (!EnableReconnect || _isConnecting || string.IsNullOrEmpty(_username))
                return;

            await ConnectAsync(_username, _avatar, RoomName, _color);
        }
        private void ShowToast(string message)
        {
            var payload = $"""
    <AppNotification>
        <Text>{System.Security.SecurityElement.Escape(message)}</Text>
    </AppNotification>
    """;

            try
            {
                AppNotificationManager.Default.Register();
                AppNotificationManager.Default.Show(new AppNotification(payload));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Toast error: {ex.Message}");
            }
        }

        public async Task DeclarePatient(Patient p)
        {
            await Connection.InvokeAsync("DeclarePatient", p);
        }

        public async Task RemovePatientAsync(string id)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
                await Connection.InvokeAsync("RemovePatient", id);
        }

        public async Task SetPatientTakenAsync(string id, bool isTaken)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
                await Connection.InvokeAsync("UpdatePatientIsTaken", id, isTaken);
        }

        public async Task UpdatePatientHoldTimeAsync(string id, DateTime newTime)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
                await Connection.InvokeAsync("UpdatePatientHoldTime", id, newTime);
        }

        public async Task UpdatePatientAsync(Patient patient)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
                await Connection.InvokeAsync("UpdatePatient", patient);
        }

        public async Task DeleteMessageAsync(int id)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
                await Connection.InvokeAsync("DeleteMessage", id);
        }
        public async Task<List<ChatMessageModel>> LoadTodayMessagesFromDiskAsync()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", $"chat_{DateTime.Today:yyyyMMdd}.json");
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    var list = JsonConvert.DeserializeObject<List<ChatMessageModel>>(json) ?? new();
                    foreach (var m in list)
                    {
                        m.SenderColor = ConnectedUsers.FirstOrDefault(u => u.Username == m.Sender)?.ColorUserName ?? "Black";
                        m.DestinataireColor = ConnectedUsers.FirstOrDefault(u => u.Username == m.Destinataire)?.ColorUserName ?? "Black";
                    }
                    return list;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur lecture cache local : {ex.Message}");
            }
            return new();
        }

        public async Task SaveTodayMessagesToDiskAsync()
        {
            try
            {
                var messages = Messages.OfType<ChatMessageModel>().ToList();
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", $"chat_{DateTime.Today:yyyyMMdd}.json");
                var json = JsonConvert.SerializeObject(messages, Formatting.Indented);
                await File.WriteAllTextAsync(path, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur sauvegarde cache local : {ex.Message}");
            }
        }

        public async Task<List<UserInfo>> LoadUsersFromDiskAsync()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "users.json");
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    var users = JsonConvert.DeserializeObject<List<UserInfo>>(json) ?? new();
                    foreach (var u in users)
                    {
                        if (u.Rooms.Count == 0)
                            u.IsOnline = false;
                    }
                    return users;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur lecture users cache : {ex.Message}");
            }
            return new();
        }

        public async Task SaveUsersToDiskAsync()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "users.json");
                var json = JsonConvert.SerializeObject(ConnectedUsers.ToList(), Formatting.Indented);
                await File.WriteAllTextAsync(path, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur sauvegarde users cache : {ex.Message}");
            }
        }

        public void ClearLocalData()
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat");
                if (Directory.Exists(folder))
                {
                    foreach (var file in Directory.GetFiles(folder, "chat_*.json"))
                        File.Delete(file);

                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur nettoyage cache : {ex.Message}");
            }

            Dispatcher?.TryEnqueue(() =>
            {
                ConnectedUsers.Clear();
                Patients.Clear();
                Messages.Clear();
                Messages.Add(new LoadMorePlaceholder());
            });
        }
        public async Task SendExamOptionsAsync(IEnumerable<ExamOption> options)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");

            try
            {
                await Connection.InvokeAsync("SaveExamOptions", options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur envoi examens : {ex.Message}");
            }
        }

        public async Task SendExamOptionsSilentAsync(IEnumerable<ExamOption> options)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");

            try
            {
                await Connection.InvokeAsync("SaveExamOptionsSilent", options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur envoi examens silencieux : {ex.Message}");
            }
        }

        public async Task SendRoomsAsync(IEnumerable<string> rooms)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");

            try
            {
                await Connection.InvokeAsync("SaveRooms", rooms);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur envoi salles : {ex.Message}");
            }
        }

        public async Task SendRoomsSilentAsync(IEnumerable<string> rooms)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");

            try
            {
                await Connection.InvokeAsync("SaveRoomsSilent", rooms);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur envoi salles silencieux : {ex.Message}");
            }
        }

        public async Task SendReminderAsync(ReminderConfig config)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected)
                {
                    Debug.WriteLine("Impossible d'envoyer le rappel : connexion SignalR non établie.");
                    return;
                }
            }

            try
            {
                await Connection.InvokeAsync("SaveReminder", config);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur envoi rappel : {ex.Message}");
            }
        }

        public async Task<ReminderConfig?> GetReminderAsync()
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return null;
            }

            try
            {
                return await Connection.InvokeAsync<ReminderConfig>("GetReminder");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération rappel : {ex.Message}");
                return null;
            }
        }
        public async Task<List<ExamOption>> GetExamOptionsAsync()
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new List<ExamOption>();
            }

            try
            {
                return await Connection.InvokeAsync<List<ExamOption>>("GetExamOptions");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération examens : {ex.Message}");
                return new List<ExamOption>();
            }
        }

        public async Task<List<string>> GetRoomsAsync()
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new List<string>();
            }

            try
            {
                return await Connection.InvokeAsync<List<string>>("GetRooms");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération salles : {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<List<UserInfo>> GetAllUsersAsync()
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new List<UserInfo>();
            }

            try
            {
                return await Connection.InvokeAsync<List<UserInfo>>("GetAllUsers");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération utilisateurs : {ex.Message}");
                return new List<UserInfo>();
            }
        }

        public async Task SaveUserSettingsAsync(string username, string json)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");

            try
            {
                await Connection.InvokeAsync("SaveUserSettings", username, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur envoi paramètres : {ex.Message}");
            }
        }

        public async Task<string> GetUserSettingsAsync(string username)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return string.Empty;
            }

            try
            {
                return await Connection.InvokeAsync<string?>("GetUserSettings", username) ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération paramètres : {ex.Message}");
                return string.Empty;
            }
        }

        public async Task<Dictionary<string, string>> GetMissingUserSettingsAsync(IEnumerable<string> knownUsers)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new Dictionary<string, string>();
            }

            try
            {
                return await Connection.InvokeAsync<Dictionary<string, string>>("GetMissingUserSettings", knownUsers);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération paramètres manquants : {ex.Message}");
                return new Dictionary<string, string>();
            }
        }


        public async Task<List<Patient>> GetPatientsAsync()
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new List<Patient>();
            }

            try
            {
                return await Connection.InvokeAsync<List<Patient>>("GetPatients");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération patients : {ex.Message}");
                return new List<Patient>();
            }
        }

        public async Task UpdateRoomNameAsync(string roomName)
        {
            RoomName = roomName;
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await Connection.InvokeAsync("SetRoomName", roomName);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur mise à jour nom de salle : {ex.Message}");
                }
            }
        }

        public async Task UpdateColorUserNameAsync(string color)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await Connection.InvokeAsync("SetColorUserName", color);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur mise à jour couleur utilisateur : {ex.Message}");
                }
            }
        }

        public async Task<string?> UploadAvatarAsync(string fileName, string base64)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    return await Connection.InvokeAsync<string>("UploadAvatar", fileName, base64);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur envoi avatar : {ex.Message}");
                }
            }
            return null;
        }

        public async Task<List<string>> GetAvailableAvatarsAsync()
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    var avatars = await Connection.InvokeAsync<List<string>>("GetAvailableAvatars");
                    return avatars.Select(ToClientAvatar).ToList();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur récupération avatars : {ex.Message}");
                }
            }
            return new List<string>();
        }

        public async Task UpdateAvatarAsync(string avatar)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await Connection.InvokeAsync("SetAvatar", ToServerAvatar(avatar));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur mise à jour avatar : {ex.Message}");
                }
            }
        }

        public async Task ArchiveTakenPatientsAsync()
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await Connection.InvokeAsync("ArchiveTakenPatients");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur archivage patients : {ex.Message}");
                }
            }
        }
        public async Task UnarchiveAllPatientsAsync()
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await Connection.InvokeAsync("UnarchiveAllPatients");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur désarchivage patients : {ex.Message}");
                }
            }
        }

        public async Task<List<Patient>> GetArchivedPatientsAsync()
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new List<Patient>();
            }

            try
            {
                return await Connection.InvokeAsync<List<Patient>>("GetArchivedPatients");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération archives : {ex.Message}");
                return new List<Patient>();
            }
        }

        public async Task UnarchivePatientAsync(string id)
        {
            if (Connection != null && Connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await Connection.InvokeAsync("UnarchivePatient", id);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur désarchivage patient : {ex.Message}");
                }
            }
        }
        public async Task<List<PatientLog>> GetPatientLogsAsync(string patientId)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new List<PatientLog>();
            }

            try
            {
                return await Connection.InvokeAsync<List<PatientLog>>("GetPatientLogs", patientId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur récupération historique patient : {ex.Message}");
                return new List<PatientLog>();
            }
        }


        public async Task<Dictionary<string, List<string>>> GetAllGroupsAsync()
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return new Dictionary<string, List<string>>();
            }

            try
            {
                return await Connection.InvokeAsync<Dictionary<string, List<string>>>("GetAllGroupMembers");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur r\u00e9cup\u00e9ration groupes : {ex.Message}");
                return new Dictionary<string, List<string>>();
            }
        }

        public async Task RenameGroupAsync(string oldName, string newName)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return;
            }

            try
            {
                await Connection.InvokeAsync("RenameGroup", oldName, newName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur renommage groupe : {ex.Message}");
            }
        }

        public async Task ChangeGroupPasswordAsync(string groupName, string password)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return;
            }

            try
            {
                await Connection.InvokeAsync("ChangeGroupPassword", groupName, password);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur changement mot de passe groupe : {ex.Message}");
            }
        }

        public async Task RemoveUserFromGroupAsync(string groupName, string username)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
            {
                var connected = await TryReconnectAsync();
                if (!connected) return;
            }

            try
            {
                await Connection.InvokeAsync("RemoveUserFromGroup", groupName, username);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur suppression utilisateur groupe : {ex.Message}");
            }
        }

        private bool AreUserListsEqual(List<UserInfo> newList)
        {
            if (_lastServerUserList.Count != newList.Count)
                return false;

            for (int i = 0; i < _lastServerUserList.Count; i++)
            {
                var existing = _lastServerUserList[i];
                var incoming = newList[i];
                if (existing.Username != incoming.Username ||
                    existing.IsOnline != incoming.IsOnline ||
                    existing.Rooms.Count != incoming.Rooms.Count ||
                    !existing.Rooms.SequenceEqual(incoming.Rooms))
                    return false;
            }
            return true;
        }

        private void UpdateSpecialUsers()
        {
            var realUsers = ConnectedUsers.Where(u =>
                u.Username != "A Tous" &&
                u.Username != "Secrétariat" &&
                (!string.IsNullOrEmpty(u.ConnectionId) || !u.IsOnline)).ToList();

            int totalKnown = realUsers.Count;
            int totalOnline = realUsers.Count(u => u.IsOnline);

            var allUser = ConnectedUsers.FirstOrDefault(u => u.Username == "A Tous");
            if (allUser != null)
            {
                allUser.Rooms.Clear();
                allUser.Rooms.Add($"{totalOnline}/{totalKnown}");
            }

            var secretariat = ConnectedUsers.FirstOrDefault(u => u.Username == "Secrétariat");
            if (secretariat != null)
            {
                secretariat.Rooms.Clear();
                secretariat.Rooms.Add(string.Empty);
            }
        }

        private async Task<List<UserInfo>> BuildUserListWithGroupsAsync(IEnumerable<UserInfo> baseList)
        {
            var result = baseList.ToList();
            var groups = await GetAllGroupsAsync();

            foreach (var kvp in groups)
            {
                var name = kvp.Key;
                if (name == "A Tous")
                    continue;

                bool visibleToAll = name == "Secrétariat";
                if (visibleToAll || kvp.Value.Contains(_username))
                {
                    if (!result.Any(u => u.Username == name))
                    {
                        result.Add(new UserInfo
                        {
                            ConnectionId = string.Empty,
                            Username = name,
                            Avatar = visibleToAll ? "ms-appx:///Assets/secretaria.png" : "ms-appx:///Assets/earth.png",
                            Rooms = new ObservableCollection<string>(),
                            DisplayName = name,
                            ColorUserName = visibleToAll ? "Blue" : "Green",
                            IsOnline = true,
                            Note = string.Empty
                        });
                    }
                }
            }

            return result;
        }

        public async Task DisconnectAsync(bool disableReconnect = true)
        {
            if (disableReconnect)
                EnableReconnect = false;
            StopReconnectTimer();
            if (Connection != null)
            {
                try
                {
                    await Connection.StopAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur déconnexion : {ex.Message}");
                }
            }
            _initialized = false;
        }

        public async Task RefreshGroupsAsync()
        {
            if (_lastServerUserList.Count == 0)
                return;

            var finalList = await BuildUserListWithGroupsAsync(_lastServerUserList);

            Dispatcher?.TryEnqueue(() =>
            {
                ConnectedUsers.Clear();
                foreach (var u in finalList)
                    ConnectedUsers.Add(u);
                UpdateSpecialUsers();

                foreach (var msg in Messages.OfType<ChatMessageModel>())
                {
                    msg.SenderColor = ConnectedUsers.FirstOrDefault(c => c.Username == msg.Sender)?.ColorUserName ?? "Black";
                    msg.DestinataireColor = ConnectedUsers.FirstOrDefault(c => c.Username == msg.Destinataire)?.ColorUserName ?? "Black";
                }
            });

            await SaveUsersToDiskAsync();
        }
    }
}
