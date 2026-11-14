namespace ChatServeur
{
    public class ServerConfig
    {
        public int Id { get; set; }
        public string ExamOptionsJson { get; set; } = string.Empty;
        public string RoomsJson { get; set; } = string.Empty;
        public string ReminderJson { get; set; } = string.Empty;
        public string AppointmentSearchJson { get; set; } = string.Empty;
        public string DeploymentConfigurationJson { get; set; } = string.Empty;
        public int IdleRoomTimeoutSeconds { get; set; } = 180;
        /// <summary>Réglages Access saisis dans l'administration (voir ServerAccessSettings).</summary>
        public string AccessSettingsJson { get; set; } = string.Empty;
    }
}
