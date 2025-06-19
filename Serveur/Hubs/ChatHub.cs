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
        private static readonly Dictionary<string, HashSet<string>> GroupMembers = new();

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
                await Clients.All.SendAsync("UserDisconnected", user.Username);
                await Clients.All.SendAsync("UserListUpdated", ConnectedUsers.Values);
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

                await Groups.AddToGroupAsync(Context.ConnectionId, "A Tous");

                var groups = await _db.GroupMemberships
                    .Where(g => g.Username == username)
                    .Select(g => g.GroupName)
                    .ToListAsync();

                foreach (var group in groups)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, group);
                }

                await Clients.All.SendAsync("UserConnected", user);
                var baseUsers = new List<UserInfo>
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

                var userList = baseUsers.Concat(ConnectedUsers.Values).ToList();

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
                await Clients.Caller.SendAsync("ReceiveMessage", sender, room, "Secrétariat", content, avatar, timestamp);

                var membres = GetGroupMembers("Secrétariat");
                foreach (var user in membres)
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", sender, room, user, content, avatar, timestamp);
                }
            }
            else if (_userToConnectionId.TryGetValue(destinataire, out var targetConnectionId))
            {
                await Clients.Client(targetConnectionId).SendAsync("ReceiveMessage", sender, room, content, avatar, timestamp);
            }
            else
            {
                await Clients.Group(destinataire).SendAsync("ReceiveMessage", sender, room, content, avatar, timestamp);
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

        public async Task SaveExamOptions(object options)
        {
            var json = JsonSerializer.Serialize(options);
            var config = await _db.ServerConfigs.FirstOrDefaultAsync();
            if (config == null)
            {
                config = new ServerConfig();
                _db.ServerConfigs.Add(config);
            }

            config.ExamOptionsJson = json;
            await _db.SaveChangesAsync();
        }

        public async Task SaveRooms(IEnumerable<string> rooms)
        {
            var json = JsonSerializer.Serialize(rooms);
            var config = await _db.ServerConfigs.FirstOrDefaultAsync();
            if (config == null)
            {
                config = new ServerConfig();
                _db.ServerConfigs.Add(config);
            }
            config.RoomsJson = json;
            await _db.SaveChangesAsync();
        }
    }
}
