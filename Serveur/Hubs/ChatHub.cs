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
                ColorUserName = "Red",
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
                ColorUserName = "Blue",
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

        public async Task RegisterUser(string username, string avatar, string room, string color)
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
                    ColorUserName = color,
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
                Timestamp = timestamp,
                IsDeleted = false
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            if (destinataire == "A Tous")
            {
                await Clients.All.SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
            }
            else if (destinataire == "Secrétariat")
            {
                await Clients.Group("Secrétariat").SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);

                if (!GroupMembers.TryGetValue("Secrétariat", out var membres) || !membres.Contains(sender))
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", message.Id, sender, room, "Secrétariat", content, avatar, timestamp);
                }
            }
            else if (_userToConnectionId.TryGetValue(destinataire, out var targetConnectionId))
            {
                await Clients.Client(targetConnectionId).SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
                await Clients.Caller.SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
            }
            else
            {
                await Clients.Group(destinataire).SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
                await Clients.Caller.SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
            }
            Console.WriteLine($"[SERVER] Message sent from {sender} to {destinataire} in room {room}: {content}");  
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
                .Where(m => !m.IsDeleted &&
                            (m.Sender == username || m.Destinataire == username || m.Destinataire == "A Tous") &&
                            m.Timestamp.Date == today)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
        }

        public async Task<List<ChatMessage>> GetMessagesForDate(string username, DateTime date)
        {
            var day = date.Date;

            return await _db.Messages
                .Where(m => !m.IsDeleted &&
                            (m.Sender == username || m.Destinataire == username || m.Destinataire == "A Tous") &&
                            m.Timestamp.Date == day)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
        }

        public async Task DeleteMessage(int id)
        {
            var message = await _db.Messages.FindAsync(id);
            if (message != null)
            {
                message.IsDeleted = true;
                _db.Messages.Update(message);
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("MessageDeleted", id);
            }
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

        public async Task SetColorUserName(string color)
        {
            if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
            {
                user.ColorUserName = color;
                AllUsers[user.Username] = user;

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
            }
        }

        public async Task SaveUserSettings(string username, string json)
        {
            var setting = await _db.UserSettings.FirstOrDefaultAsync(s => s.Username == username);
            if (setting == null)
            {
                setting = new UserSetting { Username = username };
                _db.UserSettings.Add(setting);
            }

            setting.SettingsJson = json;
            await _db.SaveChangesAsync();
        }

        public async Task<string?> GetUserSettings(string username)
        {
            var setting = await _db.UserSettings.FirstOrDefaultAsync(s => s.Username == username);
            return setting?.SettingsJson;
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
            patient.IsArchived = false;
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
                .Where(p => p.HoldTime.Date == today && !p.IsArchived)
                .OrderBy(p => p.HoldTime)
                .ToListAsync();
        }

        public async Task<List<Patient>> GetArchivedPatients()
        {
            return await _db.Patients
                .Where(p => p.IsArchived)
                .OrderBy(p => p.HoldTime)
                .ToListAsync();
        }

        public async Task ArchiveTakenPatients()
        {
            var patients = await _db.Patients.Where(p => p.IsTaken && !p.IsArchived).ToListAsync();
            if (patients.Any())
            {
                foreach (var p in patients)
                    p.IsArchived = true;
                _db.Patients.UpdateRange(patients);
                await _db.SaveChangesAsync();
                foreach (var p in patients)
                    await Clients.All.SendAsync("PatientUpdated", p);
            }
        }

        public async Task UnarchiveAllPatients()
        {
            var patients = await _db.Patients.Where(p => p.IsArchived).ToListAsync();
            if (patients.Any())
            {
                foreach (var p in patients)
                    p.IsArchived = false;
                _db.Patients.UpdateRange(patients);
                await _db.SaveChangesAsync();
                foreach (var p in patients)
                    await Clients.All.SendAsync("PatientUpdated", p);
            }
        }

        public async Task UnarchivePatient(string id)
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient != null)
            {
                patient.IsArchived = false;
                _db.Patients.Update(patient);
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("PatientUpdated", patient);
            }
        }

        public Task<Dictionary<string, List<string>>> GetAllGroupMembers()
        {
            var result = GroupMembers.ToDictionary(g => g.Key, g => g.Value.ToList());
            return Task.FromResult(result);
        }

        public async Task RenameGroup(string oldName, string newName)
        {
            var secure = await _db.SecureGroups.FirstOrDefaultAsync(g => g.Name == oldName);
            if (secure != null)
            {
                secure.Name = newName;
                _db.SecureGroups.Update(secure);
            }

            var memberships = await _db.GroupMemberships.Where(m => m.GroupName == oldName).ToListAsync();
            foreach (var m in memberships)
                m.GroupName = newName;
            if (memberships.Count > 0)
                _db.GroupMemberships.UpdateRange(memberships);

            await _db.SaveChangesAsync();

            if (GroupMembers.TryGetValue(oldName, out var members))
            {
                GroupMembers.Remove(oldName);
                GroupMembers[newName] = members;

                foreach (var user in members)
                {
                    if (_userToConnectionId.TryGetValue(user, out var conn))
                    {
                        await Groups.RemoveFromGroupAsync(conn, oldName);
                        await Groups.AddToGroupAsync(conn, newName);
                    }
                }
            }
        }

        public async Task ChangeGroupPassword(string groupName, string password)
        {
            var group = await _db.SecureGroups.FirstOrDefaultAsync(g => g.Name == groupName);
            if (group == null)
            {
                group = new SecureGroup { Name = groupName, PasswordHash = HashPassword(password) };
                _db.SecureGroups.Add(group);
            }
            else
            {
                group.PasswordHash = HashPassword(password);
                _db.SecureGroups.Update(group);
            }

            await _db.SaveChangesAsync();
        }

        public async Task RemoveUserFromGroup(string groupName, string username)
        {
            var member = await _db.GroupMemberships.FirstOrDefaultAsync(m => m.GroupName == groupName && m.Username == username);
            if (member != null)
            {
                _db.GroupMemberships.Remove(member);
                await _db.SaveChangesAsync();
            }

            if (GroupMembers.TryGetValue(groupName, out var list))
                list.Remove(username);

            if (_userToConnectionId.TryGetValue(username, out var conn))
                await Groups.RemoveFromGroupAsync(conn, groupName);
        }
    }
}
