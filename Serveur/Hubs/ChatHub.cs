using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static ChatServeur.PasswordHelper;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.Extensions.Logging;

namespace ChatServeur
{
    public class ChatHub : Hub
    {
        private static readonly Dictionary<string, UserInfo> ConnectedUsers = new();
        private static readonly Dictionary<string, HashSet<string>> _userToConnectionId = new();
        private static readonly Dictionary<string, UserInfo> AllUsers = new();
        private static readonly Dictionary<string, HashSet<string>> GroupMembers = new();
        private static bool _usersLoaded;

        private static readonly List<UserInfo> BaseUsers = new()
        {
            new UserInfo
            {
                ConnectionId = string.Empty,
                Username = "A Tous",
                Avatar = "ms-appx:///Assets/earth.png",
                Rooms = new List<string>(),
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
                Rooms = new List<string>(),
                DisplayName = "Secrétariat",
                ColorUserName = "Blue",
                IsOnline = true,
                Note = string.Empty
            }
        };

        private static bool IsProtectedUser(string username)
            => BaseUsers.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        private static bool TryGetUserEntry(string username, out string actualKey, out UserInfo user)
        {
            foreach (var pair in AllUsers)
            {
                if (string.Equals(pair.Key, username, StringComparison.OrdinalIgnoreCase))
                {
                    actualKey = pair.Key;
                    user = pair.Value;
                    return true;
                }
            }

            actualKey = string.Empty;
            user = null!;
            return false;
        }

        private static bool TryGetConnectionEntry(string username, out string actualKey, out HashSet<string> connections)
        {
            foreach (var pair in _userToConnectionId)
            {
                if (string.Equals(pair.Key, username, StringComparison.OrdinalIgnoreCase))
                {
                    actualKey = pair.Key;
                    connections = pair.Value;
                    return true;
                }
            }

            actualKey = string.Empty;
            connections = null!;
            return false;
        }

        private static string? FindMember(HashSet<string> members, string username)
        {
            foreach (var member in members)
            {
                if (string.Equals(member, username, StringComparison.OrdinalIgnoreCase))
                    return member;
            }

            return null;
        }

        private void EnsureUsersLoaded()
        {
            if (_usersLoaded)
                return;

            foreach (var u in _db.KnownUsers)
            {
                AllUsers[u.Username] = new UserInfo
                {
                    ConnectionId = u.ConnectionId,
                    Username = u.Username,
                    Avatar = ToRelativeAvatar(u.Avatar),
                    Rooms = ParseRooms(u.Room),
                    DisplayName = u.DisplayName,
                    ColorUserName = u.ColorUserName,
                    IsOnline = u.IsOnline,
                    Note = u.Note
                };
            }

            _usersLoaded = true;
        }

        private readonly ChatDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(ChatDbContext db, IWebHostEnvironment env, ILogger<ChatHub> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        private string ToRelativeAvatar(string avatar)
        {
            if (string.IsNullOrWhiteSpace(avatar))
                return avatar;
            try
            {
                var uri = new Uri(avatar, UriKind.RelativeOrAbsolute);
                if (uri.IsAbsoluteUri)
                {
                    if (string.Equals(uri.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(uri.Scheme, "ms-appdata", StringComparison.OrdinalIgnoreCase))
                        return avatar;

                    return uri.PathAndQuery;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SER10: Failed to normalize avatar path {Avatar}.", avatar);
            }
            return avatar;
        }

        private static List<string> ParseRooms(string rooms)
        {
            return string.IsNullOrWhiteSpace(rooms)
                ? new List<string>()
                : rooms.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim()).ToList();
        }

        private string GetUsername()
        {
            return ConnectedUsers.TryGetValue(Context.ConnectionId, out var user)
                ? user.Username
                : "Unknown";
        }

        private void AddPatientLog(string patientId, string action, string details)
        {
            _db.PatientLogs.Add(new PatientLog
            {
                PatientId = patientId,
                Username = GetUsername(),
                Action = action,
                Details = details,
                Timestamp = DateTime.Now
            });
        }

        public override Task OnConnectedAsync()
        {
            return base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? ex)
        {
            EnsureUsersLoaded();
            if (ConnectedUsers.Remove(Context.ConnectionId, out var user))
            {
                if (_userToConnectionId.TryGetValue(user.Username, out var connections))
                {
                    connections.Remove(Context.ConnectionId);
                    if (connections.Count == 0)
                    {
                        _userToConnectionId.Remove(user.Username);

                        user.IsOnline = false;
                        user.Rooms.Clear();
                        AllUsers[user.Username] = user;

                        var dbUser = await _db.KnownUsers.FirstOrDefaultAsync(u => u.Username == user.Username);
                        if (dbUser != null)
                        {
                            dbUser.ConnectionId = string.Empty;
                            dbUser.Room = "Hors ligne";
                            dbUser.IsOnline = false;
                            _db.KnownUsers.Update(dbUser);
                            await _db.SaveChangesAsync();
                        }

                        await Clients.All.SendAsync("UserDisconnected", user.Username);

                        var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                        await Clients.All.SendAsync("UserListUpdated", userList);

                        foreach (var group in GroupMembers.Keys)
                        {
                            GroupMembers[group].Remove(user.Username);
                        }
                    }
                }
            }

            await base.OnDisconnectedAsync(ex);
        }

        public async Task RegisterUser(string username, string avatar, string room, string color)
        {
            try
            {
                avatar = ToRelativeAvatar(avatar);
                if (string.IsNullOrWhiteSpace(avatar))
                    avatar = "/Assets/utilisateur.png";
                EnsureUsersLoaded();
                _logger.LogInformation("RegisterUser invoked for {Username}.", username);

                var rooms = new List<string>();
                if (AllUsers.TryGetValue(username, out var existing) && existing.Rooms.Any())
                    rooms = existing.Rooms.Where(r => r != "Hors ligne").ToList();
                if (!rooms.Contains(room))
                    rooms.Add(room);

                var user = new UserInfo
                {
                    ConnectionId = Context.ConnectionId,
                    Username = username,
                    Avatar = avatar,
                    Rooms = rooms,
                    DisplayName = username,
                    ColorUserName = color,
                    IsOnline = true,
                    Note = string.Empty
                };

                ConnectedUsers[Context.ConnectionId] = user;
                if (!_userToConnectionId.TryGetValue(username, out var set))
                {
                    set = new HashSet<string>();
                    _userToConnectionId[username] = set;
                }
                set.Add(Context.ConnectionId);
                AllUsers[username] = user;

                var dbUser = await _db.KnownUsers.FirstOrDefaultAsync(u => u.Username == username);
                if (dbUser == null)
                {
                    dbUser = new KnownUser { Username = username };
                    _db.KnownUsers.Add(dbUser);
                }
                dbUser.ConnectionId = Context.ConnectionId;
                dbUser.Avatar = avatar;
                dbUser.Room = string.Join(",", user.Rooms);
                dbUser.DisplayName = username;
                dbUser.ColorUserName = color;
                dbUser.IsOnline = true;
                dbUser.Note = string.Empty;
                await _db.SaveChangesAsync();

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
                _logger.LogError(ex, "SER11: Failed to register user {Username}.", username);
                throw;
            }
        }


        public async Task SendMessage(string sender, string room, string destinataire, string content, string avatar, DateTime timestamp)
        {
            avatar = ToRelativeAvatar(avatar);
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
            else if (_userToConnectionId.TryGetValue(destinataire, out var targetConnectionIds))
            {
                await Clients.Clients(targetConnectionIds).SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
                await Clients.Caller.SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
            }
            else
            {
                await Clients.Group(destinataire).SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
                await Clients.Caller.SendAsync("ReceiveMessage", message.Id, sender, room, destinataire, content, avatar, timestamp);
            }
            _logger.LogInformation("Message sent from {Sender} to {Recipient} in room {Room}.", sender, destinataire, room);
        }

        public async Task GenerateSampleMessages()
        {
            EnsureUsersLoaded();

            var username = GetUsername();
            if (string.Equals(username, "Unknown", StringComparison.OrdinalIgnoreCase))
                return;

            var userAvatar = "ms-appx:///Assets/utilisateur.png";
            var userRoom = "A Tous";

            if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var caller))
            {
                if (!string.IsNullOrWhiteSpace(caller.Avatar))
                    userAvatar = caller.Avatar;

                var firstRoom = caller.Rooms.FirstOrDefault(r => !string.Equals(r, "Hors ligne", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(firstRoom))
                    userRoom = firstRoom;
            }
            else if (AllUsers.TryGetValue(username, out var known))
            {
                if (!string.IsNullOrWhiteSpace(known.Avatar))
                    userAvatar = known.Avatar;

                var firstRoom = known.Rooms.FirstOrDefault(r => !string.Equals(r, "Hors ligne", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(firstRoom))
                    userRoom = firstRoom;
            }

            userAvatar = ToRelativeAvatar(userAvatar);
            var secretariatAvatar = ToRelativeAvatar(BaseUsers.FirstOrDefault(u => u.Username == "Secrétariat")?.Avatar ?? "ms-appx:///Assets/secretaria.png");

            var messages = new List<(string Sender, string Room, string Dest, string Content, string Avatar)>
            {
                (username, userRoom, "A Tous", "Bonjour à tous, ceci est un message de démonstration.", userAvatar),
                (username, userRoom, "A Tous", "Utilisez l'icône « + » pour ajouter un patient au planning.", userAvatar),
                (username, userRoom, "A Tous", "Pour mettre votre en-tête, appuyez sur la touche [[F5]].", userAvatar),
                ("Secrétariat", "Secrétariat", "A Tous", "Pensez à prévenir l'équipe lorsqu'un examen est terminé.", secretariatAvatar),
                (username, userRoom, "A Tous", "Sélectionnez un destinataire pour envoyer un message privé.", userAvatar),
                ("Secrétariat", "Secrétariat", "A Tous", "Tapez /clearallmessageday pour remettre le fil à zéro.", secretariatAvatar)
            };

            foreach (var (sender, room, destinataire, content, avatar) in messages)
            {
                await SendMessage(sender, room, destinataire, content, avatar, DateTime.Now);
            }
        }

        public async Task CallUser(string caller, string room, string destinataire)
        {
            if (_userToConnectionId.TryGetValue(destinataire, out var targetConnectionIds))
            {
                await Clients.Clients(targetConnectionIds).SendAsync("ReceiveCall", caller, room);
            }
        }

        public async Task JoinRoom(string roomName)
        {
            var username = ConnectedUsers[Context.ConnectionId].Username;

            if (!GroupMembers.ContainsKey(roomName))
                GroupMembers[roomName] = new HashSet<string>();

            GroupMembers[roomName].Add(username);

            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);

            if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
            {
                if (!user.Rooms.Contains(roomName))
                    user.Rooms.Add(roomName);
                AllUsers[user.Username] = user;
                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
            }
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
                if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
                {
                    if (!user.Rooms.Contains(groupName))
                        user.Rooms.Add(groupName);
                    AllUsers[user.Username] = user;
                    var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                    await Clients.All.SendAsync("UserListUpdated", userList);
                }
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
                    if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
                    {
                        if (!user.Rooms.Contains(groupName))
                            user.Rooms.Add(groupName);
                        AllUsers[user.Username] = user;
                        var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                        await Clients.All.SendAsync("UserListUpdated", userList);
                    }
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

        public async Task ClearTodayMessages()
        {
            var today = DateTime.Today;
            var messages = await _db.Messages
                .Where(m => !m.IsDeleted && m.Timestamp.Date == today)
                .ToListAsync();

            if (!messages.Any())
                return;

            foreach (var message in messages)
            {
                message.IsDeleted = true;
            }

            _db.Messages.UpdateRange(messages);
            await _db.SaveChangesAsync();

            foreach (var message in messages)
            {
                await Clients.All.SendAsync("MessageDeleted", message.Id);
            }
        }

        public async Task SetRoomName(string roomName)
        {
            EnsureUsersLoaded();
            if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
            {
                user.Rooms = new List<string> { roomName };
                AllUsers[user.Username] = user;

                var dbUser = await _db.KnownUsers.FirstOrDefaultAsync(u => u.Username == user.Username);
                if (dbUser != null)
                {
                    dbUser.Room = string.Join(",", user.Rooms);
                    _db.KnownUsers.Update(dbUser);
                    await _db.SaveChangesAsync();
                }

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
            }
        }

        public async Task SetColorUserName(string color)
        {
            EnsureUsersLoaded();
            if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
            {
                user.ColorUserName = color;
                AllUsers[user.Username] = user;

                var dbUser = await _db.KnownUsers.FirstOrDefaultAsync(u => u.Username == user.Username);
                if (dbUser != null)
                {
                    dbUser.ColorUserName = color;
                    _db.KnownUsers.Update(dbUser);
                    await _db.SaveChangesAsync();
                }

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
            }
        }

        public async Task SetAvatar(string avatar)
        {
            avatar = ToRelativeAvatar(avatar);
            EnsureUsersLoaded();
            if (ConnectedUsers.TryGetValue(Context.ConnectionId, out var user))
            {
                user.Avatar = avatar;
                AllUsers[user.Username] = user;

                var dbUser = await _db.KnownUsers.FirstOrDefaultAsync(u => u.Username == user.Username);
                if (dbUser != null)
                {
                    dbUser.Avatar = avatar;
                    _db.KnownUsers.Update(dbUser);
                    await _db.SaveChangesAsync();
                }

                await _db.Messages
                    .Where(m => m.Sender == user.Username)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Avatar, avatar));

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
            }
        }

        public async Task<string> UploadAvatar(string fileName, string base64)
        {
            var commaIndex = base64.IndexOf(',');
            if (commaIndex >= 0)
                base64 = base64.Substring(commaIndex + 1);

            var bytes = Convert.FromBase64String(base64);

            var webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
                webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

            var avatarsPath = Path.Combine(webRoot, "avatars");
            Directory.CreateDirectory(avatarsPath);
            var extension = Path.GetExtension(fileName);
            var safeName = Path.ChangeExtension(Path.GetRandomFileName(), extension);
            var filePath = Path.Combine(avatarsPath, safeName);
            await File.WriteAllBytesAsync(filePath, bytes);
            //return $"/avatars/{fileName}";
            return $"/avatars/{safeName}";
        }

        public Task<List<string>> GetAvailableAvatars()
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
                webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var avatarsPath = Path.Combine(webRoot, "avatars");
            if (!Directory.Exists(avatarsPath))
                return Task.FromResult(new List<string>());
            var files = Directory.GetFiles(avatarsPath)
                .Select(f => $"/avatars/{Path.GetFileName(f)}")
                .ToList();
            return Task.FromResult(files);
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

            await Clients.Others.SendAsync("UserSettingsUpdated", username, json);
        }

        public async Task<string?> GetUserSettings(string username)
        {
            var setting = await _db.UserSettings.FirstOrDefaultAsync(s => s.Username == username);
            return setting?.SettingsJson;
        }

        public async Task<Dictionary<string, string>> GetMissingUserSettings(IEnumerable<string> knownUsers)
        {
            return await _db.UserSettings
                .Where(s => !knownUsers.Contains(s.Username))
                .ToDictionaryAsync(s => s.Username, s => s.SettingsJson);
        }

        public Task<List<UserInfo>> GetAllUsers()
        {
            EnsureUsersLoaded();
            var userList = BaseUsers.Concat(AllUsers.Values).ToList();
            return Task.FromResult(userList);
        }

        public async Task<bool> RenameKnownUser(string oldName, string newName)
        {
            EnsureUsersLoaded();

            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return false;

            oldName = oldName.Trim();
            newName = newName.Trim();

            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (IsProtectedUser(oldName) || IsProtectedUser(newName))
                return false;

            if (!TryGetUserEntry(oldName, out var actualOldKey, out var user))
                return false;

            if (TryGetUserEntry(newName, out _, out _))
                return false;

            var originalDisplay = user.DisplayName;

            if (TryGetConnectionEntry(actualOldKey, out var connectionKey, out var connections) && connections.Count > 0)
            {
                _userToConnectionId.Remove(connectionKey);
                _userToConnectionId[newName] = connections;

                foreach (var conn in connections)
                {
                    if (ConnectedUsers.TryGetValue(conn, out var connectedUser))
                    {
                        connectedUser.Username = newName;
                        if (string.IsNullOrWhiteSpace(connectedUser.DisplayName) || string.Equals(connectedUser.DisplayName, actualOldKey, StringComparison.OrdinalIgnoreCase))
                            connectedUser.DisplayName = newName;
                        ConnectedUsers[conn] = connectedUser;
                    }
                }
            }

            AllUsers.Remove(actualOldKey);
            user.Username = newName;
            if (string.IsNullOrWhiteSpace(originalDisplay) || string.Equals(originalDisplay, actualOldKey, StringComparison.OrdinalIgnoreCase))
                user.DisplayName = newName;
            AllUsers[newName] = user;

            foreach (var key in GroupMembers.Keys.ToList())
            {
                var members = GroupMembers[key];
                var existing = FindMember(members, actualOldKey);
                if (existing != null)
                {
                    members.Remove(existing);
                    members.Add(newName);
                }
            }

            var dbUser = await _db.KnownUsers.FirstOrDefaultAsync(u => u.Username == actualOldKey);
            if (dbUser != null)
            {
                dbUser.Username = newName;
                if (string.IsNullOrWhiteSpace(dbUser.DisplayName) || string.Equals(dbUser.DisplayName, actualOldKey, StringComparison.OrdinalIgnoreCase))
                    dbUser.DisplayName = newName;
                _db.KnownUsers.Update(dbUser);
            }

            var memberships = await _db.GroupMemberships.Where(m => m.Username == actualOldKey).ToListAsync();
            if (memberships.Count > 0)
            {
                foreach (var membership in memberships)
                    membership.Username = newName;
                _db.GroupMemberships.UpdateRange(memberships);
            }

            var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.Username == actualOldKey);
            if (settings != null)
            {
                settings.Username = newName;
                _db.UserSettings.Update(settings);
            }

            await _db.SaveChangesAsync();

            var userList = BaseUsers.Concat(AllUsers.Values).ToList();
            await Clients.All.SendAsync("UserListUpdated", userList);

            return true;
        }

        public async Task<bool> DeleteKnownUser(string username)
        {
            EnsureUsersLoaded();

            if (string.IsNullOrWhiteSpace(username))
                return false;

            username = username.Trim();

            if (IsProtectedUser(username))
                return false;

            if (!TryGetUserEntry(username, out var actualKey, out _))
                return false;

            if (TryGetConnectionEntry(actualKey, out _, out var connections) && connections.Count > 0)
                return false;

            AllUsers.Remove(actualKey);
            _userToConnectionId.Remove(actualKey);

            foreach (var key in GroupMembers.Keys.ToList())
            {
                var members = GroupMembers[key];
                var existing = FindMember(members, actualKey);
                if (existing != null)
                    members.Remove(existing);
            }

            var dbUser = await _db.KnownUsers.FirstOrDefaultAsync(u => u.Username == actualKey);
            if (dbUser != null)
                _db.KnownUsers.Remove(dbUser);

            var memberships = await _db.GroupMemberships.Where(m => m.Username == actualKey).ToListAsync();
            if (memberships.Count > 0)
                _db.GroupMemberships.RemoveRange(memberships);

            var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.Username == actualKey);
            if (settings != null)
                _db.UserSettings.Remove(settings);

            await _db.SaveChangesAsync();

            var userList = BaseUsers.Concat(AllUsers.Values).ToList();
            await Clients.All.SendAsync("UserListUpdated", userList);

            return true;
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
                var opt = opts.FirstOrDefault(o => string.Equals(o.Name, p.Exams, StringComparison.OrdinalIgnoreCase));
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
                var opt = opts.FirstOrDefault(o => string.Equals(o.Name, p.Exams, StringComparison.OrdinalIgnoreCase));
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "SER12: Failed to deserialize exam options configuration.");
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "SER13: Failed to deserialize rooms configuration.");
                return new List<string>();
            }
        }

        public async Task SaveReminder(ReminderConfig config)
        {
            var json = JsonSerializer.Serialize(config);
            var cfg = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (cfg == null)
            {
                cfg = new ServerConfig();
                _db.ServerConfigs.Add(cfg);
            }
            cfg.ReminderJson = json;
            await _db.SaveChangesAsync();
        }

        public async Task<ReminderConfig?> GetReminder()
        {
            var cfg = await _db.ServerConfigs.SingleOrDefaultAsync();
            if (cfg == null || string.IsNullOrEmpty(cfg.ReminderJson))
                return new ReminderConfig();
            try
            {
                return JsonSerializer.Deserialize<ReminderConfig>(cfg.ReminderJson) ?? new ReminderConfig();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SER14: Failed to deserialize reminder configuration.");
                return new ReminderConfig();
            }
        }

        public async Task DeclarePatient(Patient patient)
        {
            patient.IsArchived = false;
            _db.Patients.Add(patient);
            AddPatientLog(patient.Id, "Create", "Patient créé");
            await _db.SaveChangesAsync();
            await Clients.All.SendAsync("NewPatient", patient);
        }

        public async Task RemovePatient(string id)
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient != null)
            {
                _db.Patients.Remove(patient);
                AddPatientLog(patient.Id, "Delete", "Patient supprimé");
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("PatientRemoved", id);
            }
        }

        public async Task ClearTodayPatients()
        {
            var today = DateTime.Today;
            var patients = await _db.Patients
                .Where(p => p.HoldTime.Date == today && !p.IsArchived)
                .ToListAsync();

            if (!patients.Any())
                return;

            foreach (var patient in patients)
            {
                AddPatientLog(patient.Id, "DeleteDay", "Suppression via clearallpatientday");
            }

            _db.Patients.RemoveRange(patients);
            await _db.SaveChangesAsync();

            foreach (var patient in patients)
            {
                await Clients.All.SendAsync("PatientRemoved", patient.Id);
            }
        }

        public async Task UpdatePatientIsTaken(string id, bool isTaken)
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient != null)
            {
                var oldValue = patient.IsTaken;
                patient.IsTaken = isTaken;
                patient.PickUpTime = isTaken ? DateTime.Now : null;
                AddPatientLog(patient.Id, "IsTaken", $"IsTaken: {oldValue} -> {isTaken}");
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
                var oldTime = patient.HoldTime;
                patient.HoldTime = newTime;
                AddPatientLog(patient.Id, "HoldTime", $"HoldTime: {oldTime:HH:mm} -> {newTime:HH:mm}");
                _db.Patients.Update(patient);
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("PatientUpdated", patient);
            }
        }

        public async Task UpdatePatient(Patient updated)
        {
            var patient = await _db.Patients.FindAsync(updated.Id);
            if (patient != null)
            {
                var changes = new List<string>();
                if (patient.Title != updated.Title) { changes.Add($"Titre: {patient.Title} -> {updated.Title}"); patient.Title = updated.Title; }
                if (patient.LastName != updated.LastName) { changes.Add($"Nom: {patient.LastName} -> {updated.LastName}"); patient.LastName = updated.LastName; }
                if (patient.FirstName != updated.FirstName) { changes.Add($"Prénom: {patient.FirstName} -> {updated.FirstName}"); patient.FirstName = updated.FirstName; }
                if (patient.Exams != updated.Exams) { changes.Add($"Examen: {patient.Exams} -> {updated.Exams}"); patient.Exams = updated.Exams; }
                if (patient.Eye != updated.Eye) { changes.Add($"Œil: {patient.Eye} -> {updated.Eye}"); patient.Eye = updated.Eye; }
                if (patient.Annotation != updated.Annotation) { changes.Add($"Commentaire: {patient.Annotation} -> {updated.Annotation}"); patient.Annotation = updated.Annotation; }
                if (patient.Position != updated.Position) { changes.Add($"Salle: {patient.Position} -> {updated.Position}"); patient.Position = updated.Position; }
                if (patient.Colors != updated.Colors) { changes.Add("Couleurs modifiées"); patient.Colors = updated.Colors; }
                if (patient.HoldTime != updated.HoldTime) { changes.Add($"Heure: {patient.HoldTime:HH:mm} -> {updated.HoldTime:HH:mm}"); patient.HoldTime = updated.HoldTime; }
                if (changes.Count > 0)
                {
                    _db.Patients.Update(patient);
                    AddPatientLog(patient.Id, "Update", string.Join(", ", changes));
                    await _db.SaveChangesAsync();
                    await Clients.All.SendAsync("PatientUpdated", patient);
                }
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
                {
                    p.IsArchived = true;
                    AddPatientLog(p.Id, "Archive", "Archivé");
                }
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
                {
                    p.IsArchived = false;
                    AddPatientLog(p.Id, "UnarchiveAll", "Désarchivé");
                }
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
                AddPatientLog(patient.Id, "Unarchive", "Désarchivé");
                _db.Patients.Update(patient);
                await _db.SaveChangesAsync();
                await Clients.All.SendAsync("PatientUpdated", patient);
            }
        }

        public async Task<List<PatientLog>> GetPatientLogs(string patientId)
        {
            return await _db.PatientLogs
                .Where(l => l.PatientId == patientId)
                .OrderBy(l => l.Timestamp)
                .ToListAsync();
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
                    if (_userToConnectionId.TryGetValue(user, out var conns))
                    {
                        foreach (var conn in conns)
                        {
                            await Groups.RemoveFromGroupAsync(conn, oldName);
                            await Groups.AddToGroupAsync(conn, newName);
                        }
                    }
                    if (AllUsers.TryGetValue(user, out var info))
                    {
                        if (info.Rooms.Remove(oldName))
                            info.Rooms.Add(newName);
                    }
                }

                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
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

            if (_userToConnectionId.TryGetValue(username, out var conns))
            {
                foreach (var conn in conns)
                    await Groups.RemoveFromGroupAsync(conn, groupName);
            }

            if (AllUsers.TryGetValue(username, out var user))
            {
                user.Rooms.Remove(groupName);
                AllUsers[username] = user;
                var userList = BaseUsers.Concat(AllUsers.Values).ToList();
                await Clients.All.SendAsync("UserListUpdated", userList);
            }
        }
    }
}
