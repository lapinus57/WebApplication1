using Client.Models;
using Client.Helpers;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
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

        private bool _initialized;
        private bool _historyLoaded;

        public bool IsHistoryLoaded => _historyLoaded;

        public string ServerAddress { get; set; } = "http://localhost:5000";

        public SignalRService()
        {
            var cfg = ConnectionConfig.Load();
            ServerAddress = cfg.ServerAddress;
        }

        public event Action<ChatMessageModel>? OnMessageReceived;
        public event Action<Patient>? OnNewPatient;
        public event Action<IEnumerable<ExamOption>>? ExamOptionsUpdated;
        public event Action<IEnumerable<string>>? RoomsUpdated;

        private static readonly JsonSerializerSettings CamelCaseSettings = new()
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        };

        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            _initialized = true;

            await ConnectAsync("Moi", @"E:\benoit.png", "RDC");

            Messages.Clear();
            Messages.Add(new LoadMorePlaceholder());

            var cached = await LoadTodayMessagesFromDiskAsync();
            foreach (var msg in cached)
                Messages.Add(msg);

            var result = await LoadTodayMessagesAsync("Moi");
            if (result.Success)
            {
                foreach (var item in Messages.OfType<ChatMessageModel>().ToList())
                    Messages.Remove(item);
                foreach (var msg in result.Value)
                    Messages.Add(msg);

                _historyLoaded = true;
                await SaveTodayMessagesToDiskAsync();
            }
        }

        public async Task ConnectAsync(string username, string avatar, string room)
        {
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
                .WithAutomaticReconnect()
                .Build();

            Connection.Closed += async (error) =>
            {
                try
                {
                    AppNotification notification = new AppNotificationBuilder()
                        .AddText("Serveur erreur")
                        .AddText("Explore interactive samples and discover the power of modern Windows UI.")
                        .BuildNotification();

                    AppNotificationManager.Default.Show(notification);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Toast error: {ex.Message}");
                }

                await Task.Delay(3000);
            };

            Connection.On<UserInfo>("UserConnected", user =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    if (!ConnectedUsers.Any(u => u.Username == user.Username))
                    {
                        ConnectedUsers.Add(user);
                        Debug.WriteLine($"✅ User connecté : {user.Username}");
                    }
                });
            });


            Connection.On<List<UserInfo>>("UserListUpdated", users =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    ConnectedUsers.Clear();

                    foreach (var user in users)
                    {
                        ConnectedUsers.Add(user);
                        Debug.WriteLine($"✅ User list ajoutée : {user.Username} ({user.Room})");
                    }
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

            Connection.On<string, string, string, string, string, DateTime>("ReceiveMessage", (user, room, destinataire, msg, avatar, time) =>
            {
                var chat = new ChatMessageModel
                {
                    Sender = user,
                    Destinataire = destinataire,
                    Room = room,
                    Content = msg,
                    Timestamp = time,
                    Avatar = avatar
                };

                Debug.WriteLine($"✅ Message reçu de {user} dans la salle {room} : {msg} ({time})");

                Dispatcher?.TryEnqueue(async () =>
                {
                    Messages.Add(chat);
                    OnMessageReceived?.Invoke(chat);
                    await SaveTodayMessagesToDiskAsync();
                });
            });


            Connection.On<Patient>("NewPatient", patient =>
            {
                OnNewPatient?.Invoke(patient);
            });

            Connection.On<IEnumerable<ExamOption>>("ExamOptionsUpdated", opts =>
            {
                ExamOptionsUpdated?.Invoke(opts);
            });

            Connection.On<IEnumerable<string>>("RoomsUpdated", rooms =>
            {
                RoomsUpdated?.Invoke(rooms);
            });

            try
            {
                await Connection.StartAsync();
                await Connection.InvokeAsync("RegisterUser", username, avatar, room);
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
            }
            
        }

        public async Task SendMessage(string sender, string roomname, string destinataire, string message, string avatar, DateTime timemessage)
        {
            if (Connection is null || Connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("Connexion SignalR non établie.");
            await Connection.InvokeAsync("SendMessage", sender, roomname, destinataire, message, avatar, timemessage);

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
                    Sender = m.Sender,
                    Destinataire = m.Destinataire,
                    Room = m.Room,
                    Content = m.Content,
                    Avatar = m.Avatar,
                    Timestamp = m.Timestamp
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
                        Sender = m.Sender,
                        Destinataire = m.Destinataire,
                        Room = m.Room,
                        Content = m.Content,
                        Avatar = m.Avatar,
                        Timestamp = m.Timestamp
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
        public async Task<List<ChatMessageModel>> LoadTodayMessagesFromDiskAsync()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", $"chat_{DateTime.Today:yyyyMMdd}.json");
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    return JsonConvert.DeserializeObject<List<ChatMessageModel>>(json) ?? new();
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
    }
}
