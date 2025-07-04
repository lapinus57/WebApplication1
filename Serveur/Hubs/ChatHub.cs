using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static ChatServeur.PasswordHelper;

namespace ChatServeur
{
    public class ChatHub : Hub
    {
        private static readonly Dictionary<string, UserInfo> ConnectedUsers = new();
        private static readonly Dictionary<string, string> _userToConnectionId = new();
        private static readonly Dictionary<string, UserInfo> AllUsers = new();
        private static readonly Dictionary<string, HashSet<string>> GroupMembers = new();

        private static readonly List<UserInfo> BaseUsers = new()
        {
            new UserInfo
            {
                ConnectionId = string.Empty,
                Username = "A Tous",
                Avatar = "ms-appx:///Assets/earth.png",
                Room = string.Empty,
                DisplayName = "A Tous",
                IsOnline = true,
                Note = string.Empty
            },
            new UserInfo
            {
                ConnectionId = string.Empty,
                Username = "Secrétariat",
                Avatar = "ms-appx:///Assets/secretaria.png",
                Room = string.Empty,
                DisplayName = "Secrétariat",
                IsOnline = true,
                Note = string.Empty
            }
        };

        private readonly ChatDbContext _db;

        public ChatHub(ChatDbContext db)
        {
            _db = db;
        }

        public override Task OnConnectedAsync()
        {
            return base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? ex)
        {
            if (ConnectedUsers.Remove(Context.ConnectionId, out var user))
            {
                user.IsOnline = false;
                user.Room = "Hors ligne";
                AllUsers[user.Username] = user;

                await Clients.All.SendAsync("UserDisconnected", user.Username);

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);

                _userToConnectionId.Remove(user.Username);

                foreach (var group in GroupMembers.Keys)
                {
                    GroupMembers[group].Remove(user.Username);
                }
            }

            await base.OnDisconnectedAsync(ex);
        }

        public async Task RegisterUser(string username, string avatar, string room)
        {
            try
            {
                Console.WriteLine($"[SERVER] RegisterUser : {username}");

                var user = new UserInfo
                {
                    ConnectionId = Context.ConnectionId,
                    Username = username,
                    Avatar = avatar,
                    Room = room,
                    DisplayName = username,
                    IsOnline = true,
                    Note = string.Empty
                };

                ConnectedUsers[Context.ConnectionId] = user;
                _userToConnectionId[username] = Context.ConnectionId;
                AllUsers[username] = user;

                await Groups.AddToGroupAsync(Context.ConnectionId, "A Tous");
                if (!GroupMembers.ContainsKey("A Tous"))
                    GroupMembers["A Tous"] = new HashSet<string>();
                GroupMembers["A Tous"].Add(username);

                var groups = await _db.GroupMemberships
                    .Where(g => g.Username == username)
                    .Select(g => g.GroupName)
                    .ToListAsync();

                foreach (var group in groups)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, group);
                    if (!GroupMembers.ContainsKey(group))
                        GroupMembers[group] = new HashSet<string>();
                    GroupMembers[group].Add(username);
                }

                await Clients.All.SendAsync("UserConnected", user);

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();

                await Clients.All.SendAsync("UserListUpdated", userList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER ERROR] RegisterUser : {ex.Message}");
                throw;
            }
        }


        public async Task SendMessage(string sender, string room, string destinataire, string content, string avatar, DateTime timestamp)
        {
            timestamp = DateTime.Now;
            var message = new ChatMessage
            {
                Sender = sender,
                Destinataire = destinataire,
                Room = room,
                Content = content,
                Avatar = avatar,
                Timestamp = timestamp
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            if (destinataire == "A Tous")
            {
                await Clients.All.SendAsync("ReceiveMessage", sender, room, destinataire, content, avatar, timestamp);
            }
            else if (destinataire == "Secrétariat")
            {
                await Clients.Group("Secrétariat").SendAsync("ReceiveMessage", sender, room, destinataire, content, avatar, timestamp);

                if (!GroupMembers.TryGetValue("Secrétariat", out var membres) || !membres.Contains(sender))
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", sender, room, "Secrétariat", content, avatar, timestamp);
                }
            }
            else if (_userToConnectionId.TryGetValue(destinataire, out var targetConnectionId))
            {
                await Clients.Client(targetConnectionId).SendAsync("ReceiveMessage", sender, room, destinataire, content, avatar, timestamp);
                await Clients.Caller.SendAsync("ReceiveMessage", sender, room, destinataire, content, avatar, timestamp);
            }
            else
            {
                await Clients.Group(destinataire).SendAsync("ReceiveMessage", sender, room, destinataire, content, avatar, timestamp);
                await Clients.Caller.SendAsync("ReceiveMessage", sender, room, destinataire, content, avatar, timestamp);
            }
        }

        public async Task JoinRoom(string roomName)
        {
            var username = ConnectedUsers[Context.ConnectionId].Username;

            if (!GroupMembers.ContainsKey(roomName))
                GroupMembers[roomName] = new HashSet<string>();

            GroupMembers[roomName].Add(username);

            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        }

        public async Task<string> JoinProtectedGroup(string groupName, string password)
        {
            var username = ConnectedUsers[Context.ConnectionId].Username;

            var group = await _db.SecureGroups.FirstOrDefaultAsync(g => g.Name == groupName);

            if (group == null)
            {
                var newGroup = new SecureGroup
                {
                    Name = groupName,
                    PasswordHash = HashPassword(password)
                };
                _db.SecureGroups.Add(newGroup);
                _db.GroupMemberships.Add(new GroupMembership { Username = username, GroupName = groupName });
                await _db.SaveChangesAsync();

                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                if (!GroupMembers.ContainsKey(groupName))
                    GroupMembers[groupName] = new HashSet<string>();
                GroupMembers[groupName].Add(username);
                return "created";
            }
            else
            {
                if (VerifyPassword(password, group.PasswordHash))
                {
                    var alreadyIn = await _db.GroupMemberships.AnyAsync(g => g.Username == username && g.GroupName == groupName);
                    if (!alreadyIn)
                        _db.GroupMemberships.Add(new GroupMembership { Username = username, GroupName = groupName });

                    await _db.SaveChangesAsync();
                    await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                    if (!GroupMembers.ContainsKey(groupName))
                        GroupMembers[groupName] = new HashSet<string>();
                    GroupMembers[groupName].Add(username);
                    return "joined";
                }
                else
                {
                    return "wrong_password";
                }
            }
        }

        public async Task SendToRoom(string roomName, string username, string message)
        {
            await Clients.Group(roomName).SendAsync("ReceiveMessage", username, message);
        }

        private List<string> GetGroupMembers(string groupName)
        {
            if (GroupMembers.TryGetValue(groupName, out var members))
            {
                return members.ToList();
            }
            return new List<string>();
        }

        public async Task<List<ChatMessage>> GetTodayMessages(string username)
        {
            var today = DateTime.Today;

            return await _db.Messages
                .Where(m => (m.Sender == username || m.Destinataire == username || m.Destinataire == "A Tous") && m.Timestamp.Date == today)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
        }

        public async Task<List<ChatMessage>> GetMessagesForDate(string username, DateTime date)
        {
            var day = date.Date;

            return await _db.Messages
                .Where(m => (m.Sender == username || m.Destinataire == username || m.Destinataire == "A Tous") && m.Timestamp.Date == day)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
        }

        public async Task SetRoomName(string roomName)
        {
            if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
            {
                user.Room = roomName;
                AllUsers[user.Username] = user;

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
            }
        }

        public async Task SaveExamOptions(IEnumerable<ExamOption> options)
        {
            var json = JsonSerializer.Serialize(options);
            var config = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (config == null)
            {
                config = new ServerConfig();
                _db.ServerConfigs.Add(config);
            }

            config.ExamOptionsJson = json;
            await _db.SaveChangesAsync();

            // Update patients colors according to new exam options
            var opts = options.ToList();
            var patients = await _db.Patients.ToListAsync();
            var changed = new List<Patient>();
            foreach (var p in patients)
            {
                var opt = opts.SingleOrDefault(o => o.Name == p.Exams);
                if (opt != null && p.Colors != opt.Color)
                {
                    p.Colors = opt.Color;
                    changed.Add(p);
                }
            }

            if (changed.Any())
            {
                _db.Patients.UpdateRange(changed);
                await _db.SaveChangesAsync();
                foreach (var c in changed)
                    await Clients.All.SendAsync("PatientUpdated", c);
            }
            //await Clients.All.SendAsync("ExamOptionsUpdated", options);
        }

        public async Task SaveExamOptionsSilent(IEnumerable<ExamOption> options)
        {
            var json = JsonSerializer.Serialize(options);
            var config = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (config == null)
            {
                config = new ServerConfig();
                _db.ServerConfigs.Add(config);
            }

            config.ExamOptionsJson = json;
            await _db.SaveChangesAsync();
            // Apply new colors to existing patients without notification
            var opts = options.ToList();
            var patients = await _db.Patients.ToListAsync();
            var changed = new List<Patient>();
            foreach (var p in patients)
            {
                var opt = opts.SingleOrDefault(o => o.Name == p.Exams);
                if (opt != null && p.Colors != opt.Color)
                {
                    p.Colors = opt.Color;
                    changed.Add(p);
                }
            }

            if (changed.Any())
            {
                _db.Patients.UpdateRange(changed);
                await _db.SaveChangesAsync();
                foreach (var c in changed)
                    await Clients.All.SendAsync("PatientUpdated", c);
                // No broadcast in silent mode
            }
        }

        public async Task SaveRooms(IEnumerable<string> rooms)
        {
            var json = JsonSerializer.Serialize(rooms);
            var config = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (config == null)
            {
                config = new ServerConfig();
                _db.ServerConfigs.Add(config);
            }
            config.RoomsJson = json;
            await _db.SaveChangesAsync();
            //await Clients.All.SendAsync("RoomsUpdated", rooms);
        }

        public async Task SaveRoomsSilent(IEnumerable<string> rooms)
        {
            var json = JsonSerializer.Serialize(rooms);
            var config = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (config == null)
            {
                config = new ServerConfig();
                _db.ServerConfigs.Add(config);
            }
            config.RoomsJson = json;
            await _db.SaveChangesAsync();
        }

        public async Task<List<ExamOption>> GetExamOptions()
        {
            var config = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (config == null || string.IsNullOrEmpty(config.ExamOptionsJson))
                return new List<ExamOption>();
            try
            {
                return JsonSerializer.Deserialize<List<ExamOption>>(config.ExamOptionsJson) ?? new List<ExamOption>();
            }
            catch
            {
                return new List<ExamOption>();
            }
        }

        public async Task<List<string>> GetRooms()
        {
            var config = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (config == null || string.IsNullOrEmpty(config.RoomsJson))
                return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(config.RoomsJson) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task DeclarePatient(Patient patient)
        {
            _db.Patients.Add(patient);
            await _db.SaveChangesAsync();
            await Clients.All.SendAsync("NewPatient", patient);
        }

        public async Task RemovePatient(string id)
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient != null)
            {
                _db.Patients.Remove(patient);
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("PatientRemoved", id);
            }
        }

        public async Task UpdatePatientIsTaken(string id, bool isTaken)
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient != null)
            {
                patient.IsTaken = isTaken;
                patient.PickUpTime = isTaken ? DateTime.Now : null;
                _db.Patients.Update(patient);
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("PatientUpdated", patient);
            }
        }

        public async Task UpdatePatientHoldTime(string id, DateTime newTime)
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient != null)
            {
                patient.HoldTime = newTime;
                _db.Patients.Update(patient);
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("PatientUpdated", patient);
            }
        }

        public async Task<List<Patient>> GetPatients()
        {
            var today = DateTime.Today;
            return await _db.Patients
                .Where(p => p.HoldTime.Date == today)
                .OrderBy(p => p.HoldTime)
                .ToListAsync();
        }
    }
}
