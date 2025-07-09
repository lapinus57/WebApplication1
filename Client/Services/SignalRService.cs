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
        public string RoomName { get; set; } = string.Empty;

        private bool _initialized;
        private bool _historyLoaded;

        public bool IsHistoryLoaded => _historyLoaded;

        public string ServerAddress { get; set; } = "http://localhost:5000";

        public SignalRService()
        {
            var cfg = ConnectionConfig.Load();
            ServerAddress = cfg.ServerAddress;
            var machine = MachineConfig.Load();
            RoomName = machine.RoomName;
        }

        public event Action<ChatMessageModel>? OnMessageReceived;
        public event Action<Patient>? OnNewPatient;
        public event Action<string>? OnPatientRemoved;
        public event Action<Patient>? OnPatientUpdated;
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

            var cachedUsers = await LoadUsersFromDiskAsync();
            foreach (var user in cachedUsers)
            {
                if (user.Username == App.UserName)
                    continue;
                user.IsOnline = false;
                user.Room = "Hors ligne";
                ConnectedUsers.Add(user);
            }

            await ConnectAsync(App.UserName, @"E:\benoit.png", RoomName);

            Messages.Clear();
            Messages.Add(new LoadMorePlaceholder());

            var cached = await LoadTodayMessagesFromDiskAsync();
            foreach (var msg in cached)
                Messages.Add(msg);

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

                foreach (var u in ConnectedUsers)
                {
                    u.IsOnline = false;
                    u.Room = "Hors ligne";
                }
                await SaveUsersToDiskAsync();
                await Task.Delay(3000);
            };

            Connection.On<UserInfo>("UserConnected", user =>
            {
                Dispatcher?.TryEnqueue(async () =>
                {
                    if (user.Username == App.UserName)
                        return;
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
                    Debug.WriteLine($"✅ User connecté : {user.Username}");
                    await SaveUsersToDiskAsync();
                });
            });


            Connection.On<List<UserInfo>>("UserListUpdated", users =>
            {
                Dispatcher?.TryEnqueue(async () =>
                {
                    ConnectedUsers.Clear();

                    foreach (var user in users)
                    {
                        if (user.Username == App.UserName)
                            continue;
                        ConnectedUsers.Add(user);
                        Debug.WriteLine($"✅ User list ajoutée : {user.Username} ({user.Room})");
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
                var chat = new ChatMessageModel
                {
                    Id = id,
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

            Connection.On<int>("MessageDeleted", id =>
            {
                Dispatcher?.TryEnqueue(() =>
                {
                    var message = Messages.OfType<ChatMessageModel>().FirstOrDefault(m => m.Id == id);
                    if (message != null)
                        Messages.Remove(message);
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
                    Id = m.Id,
                    Sender = m.Sender,
                    Destinataire = m.Destinataire,
                    Room = m.Room,
                    Content = m.Content,
                    Avatar = m.Avatar,
                    Timestamp = m.Timestamp,
                    IsDeleted = m.IsDeleted
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
                        Avatar = m.Avatar,
                        Timestamp = m.Timestamp,
                        IsDeleted = m.IsDeleted
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

        public async Task<List<UserInfo>> LoadUsersFromDiskAsync()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "users.json");
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    var list = JsonConvert.DeserializeObject<List<UserInfo>>(json) ?? new();
                    list.RemoveAll(u => u.Username == App.UserName);
                    return list;
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
                var json = JsonConvert.SerializeObject(ConnectedUsers.Where(u => u.Username != App.UserName).ToList(), Formatting.Indented);
                await File.WriteAllTextAsync(path, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur sauvegarde users cache : {ex.Message}");
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
    }
}
