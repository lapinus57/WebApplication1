using System;
using System.Text.Json.Serialization;

namespace Client.SmsMail.Models
{
    public class SmsOutgoingResponse
    {
        [JsonPropertyName("batchID")]
        public string? BatchId { get; set; }

        [JsonPropertyName("creationDatetime")]
        public DateTime CreationDatetime { get; set; }

        [JsonPropertyName("credits")]
        public decimal? Credits { get; set; }

        [JsonPropertyName("deliveredAt")]
        public DateTime? DeliveredAt { get; set; }

        [JsonPropertyName("deliveryReceipt")]
        public int? DeliveryReceipt { get; set; }

        [JsonPropertyName("differedDelivery")]
        public int? DifferedDelivery { get; set; }

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("messageID")]
        public string? MessageId { get; set; }

        [JsonPropertyName("messageLength")]
        public int? MessageLength { get; set; }

        [JsonPropertyName("numberOfSms")]
        public int? NumberOfSms { get; set; }

        [JsonPropertyName("ptt")]
        public int? Ptt { get; set; }

        [JsonPropertyName("receiver")]
        public string Receiver { get; set; } = string.Empty;

        [JsonPropertyName("sender")]
        public string Sender { get; set; } = string.Empty;

        [JsonPropertyName("sentAt")]
        public DateTime? SentAt { get; set; }

        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("tariffCode")]
        public string? TariffCode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }
}


