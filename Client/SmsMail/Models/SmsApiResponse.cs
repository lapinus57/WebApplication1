using System.Text.Json.Serialization;

namespace Client.SmsMail.Models
{
    public class SmsApiResponse
    {
        [JsonPropertyName("totalCreditsRemoved")]
        public decimal TotalCreditsRemoved { get; set; }

        [JsonPropertyName("ids")]
        public long[] Ids { get; set; } = System.Array.Empty<long>();

        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("invalidReceivers")]
        public string[] InvalidReceivers { get; set; } = System.Array.Empty<string>();

        [JsonPropertyName("validReceivers")]
        public string[] ValidReceivers { get; set; } = System.Array.Empty<string>();
    }
}

