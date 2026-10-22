using System.Text.Json.Serialization;

namespace Client.SmsMail.Models
{
    public class SmsJob
    {
        [JsonPropertyName("receivers")]
        public string[] Receivers { get; set; } = System.Array.Empty<string>();

        [JsonPropertyName("sender")]
        public string Sender { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("noStopClause")]
        public bool NoStopClause { get; set; }

        [JsonPropertyName("differedPeriod")]
        public int? DifferedPeriod { get; set; }
    }
}

