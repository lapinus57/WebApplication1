using ChatServeur.Startup;

namespace ChatServeur
{
    /// <summary>Vérifie toutes les heures que la sauvegarde du jour de chat.db existe, et la crée sinon.</summary>
    public sealed class DatabaseBackupService : BackgroundService
    {
        private readonly ILogger<DatabaseBackupService> _logger;
        private readonly string _databasePath;
        private readonly string _backupFolder;

        public DatabaseBackupService(ILogger<DatabaseBackupService> logger)
        {
            _logger = logger;
            var dataFolder = Path.Combine(AppContext.BaseDirectory, "data");
            _databasePath = Path.Combine(dataFolder, "chat.db");
            _backupFolder = Path.Combine(dataFolder, "sauvegardes");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Laisse le démarrage (migrations) se terminer avant la première copie.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var created = DatabaseBackup.RunIfDue(_databasePath, _backupFolder, DateTime.Today);
                    if (created != null)
                        _logger.LogInformation("Sauvegarde quotidienne de la base créée : {Path}", created);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SER20: La sauvegarde quotidienne de la base a échoué.");
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
