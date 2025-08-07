using System;

namespace Client.Models
{
    public class PatientLog
    {
        public int Id { get; set; }
        public string PatientId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
