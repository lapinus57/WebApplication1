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
                    _logger.LogError(ex, "SER15: Reminder service loop failed.");
                }

                var now = DateTime.Now;
                var delay = TimeSpan.FromMinutes(1) - TimeSpan.FromSeconds(now.Second) - TimeSpan.FromMilliseconds(now.Millisecond);
                await Task.Delay(delay, stoppingToken);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "SER16: Failed to deserialize reminder configuration payload.");
                return;
            }
            if (reminder == null || !reminder.IsEnabled)
                return;

            var now = DateTime.Now;
            foreach (var item in reminder.Reminders)
            {
                if (item.Days != null && item.Days.Count > 0 && !item.Days.Contains(now.DayOfWeek))
                    continue;
                foreach (var t in item.Times)
                {
                    if (!TimeSpan.TryParse(t, out var span))
                        continue;
                    var scheduled = now.Date.Add(span);
                    if (Math.Abs((scheduled - now).TotalMinutes) < 1)
                    {
                        var key = $"{item.Title}-{item.Message}-{scheduled:yyyyMMddHHmm}";
                        if (_sent.Add(key))
                        {
                            var message = new ChatMessage
                            {
                                Sender = "Rappel",
                                Destinataire = "A Tous",
                                Room = item.Title ?? string.Empty,
                                Content = item.Message,
                                Avatar = "/Assets/secretaria.png",

                                Timestamp = now,
                                IsDeleted = false
                            };
                            db.Messages.Add(message);
                            try
                            {
                                await db.SaveChangesAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "SER17: Failed to persist reminder message {Key}.", key);
                                continue;
                            }

                            try
                            {
                                await _hub.Clients.All.SendAsync("ReceiveMessage", message.Id, message.Sender, message.Room, message.Destinataire, message.Content, message.Avatar, message.Timestamp);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "SER18: Failed to broadcast reminder message {Key}.", key);
                            }
                        }
                    }
                }
            }

            var todayPrefix = now.ToString("yyyyMMdd");
            _sent.RemoveWhere(k => !k.Contains(todayPrefix));
        }
    }
}
