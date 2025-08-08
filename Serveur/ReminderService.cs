using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChatServeur
{
    public class ReminderService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<ChatHub> _hub;
        private readonly ILogger<ReminderService> _logger;
        private readonly HashSet<string> _sent = new();

        public ReminderService(IServiceScopeFactory scopeFactory, IHubContext<ChatHub> hub, ILogger<ReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _hub = hub;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckRemindersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur service rappels");
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task CheckRemindersAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var config = await db.ServerConfigs.SingleOrDefaultAsync();
            if (config == null || string.IsNullOrEmpty(config.ReminderJson))
                return;

            ReminderConfig? reminder;
            try
            {
                reminder = JsonSerializer.Deserialize<ReminderConfig>(config.ReminderJson);
            }
            catch
            {
                return;
            }
            if (reminder == null || !reminder.IsEnabled)
                return;

            var now = DateTime.Now;
            foreach (var item in reminder.Reminders)
            {
                foreach (var t in item.Times)
                {
                    if (!TimeSpan.TryParse(t, out var span))
                        continue;
                    var scheduled = now.Date.Add(span);
                    if (Math.Abs((scheduled - now).TotalMinutes) < 1)
                    {
                        var key = $"{item.Message}-{scheduled:yyyyMMddHHmm}";
                        if (_sent.Add(key))
                        {
                            var message = new ChatMessage
                            {
                                Sender = "Rappel",
                                Destinataire = "A Tous",
                                Room = string.Empty,
                                Content = item.Message,
                                Avatar = "/Assets/secretaria.png",

                                Timestamp = now,
                                IsDeleted = false
                            };
                            db.Messages.Add(message);
                            await db.SaveChangesAsync();
                            await _hub.Clients.All.SendAsync("ReceiveMessage", message.Id, message.Sender, message.Room, message.Destinataire, message.Content, message.Avatar, message.Timestamp);
                        }
                    }
                }
            }

            var todayPrefix = now.ToString("yyyyMMdd");
            _sent.RemoveWhere(k => !k.Contains(todayPrefix));
        }
    }
}
