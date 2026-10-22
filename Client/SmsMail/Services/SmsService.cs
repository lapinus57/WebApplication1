using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services
{
    public sealed class SmsService
    {
        private readonly HttpClient Http;
        private readonly SmsSettings _settings;
        public SmsService(SmsSettings settings, HttpClient? http = null)
        {
            _settings = settings;
            Http = http ?? SharedHttp;
        }
        private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public async Task<decimal> GetSmsBalanceAsync()
        {
            var settings = _settings;
            Validate();
            var timestamp = await Http.GetStringAsync(settings.TimeEndpoint).ConfigureAwait(false);
            timestamp = timestamp.Trim();

            var url = $"{settings.BalanceEndpoint}/{settings.ServiceName}";
            var signature = CreateSignature(settings, "GET", url, string.Empty, timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthenticationHeaders(request, settings, signature, timestamp);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"[{response.StatusCode}] {payload}");
            }

            using var document = JsonDocument.Parse(payload);
            return document.RootElement.GetProperty("creditsLeft").GetDecimal();
        }

        public async Task<SmsApiResponse> SendSmsAsync(string phoneNumber, string message, DateTime? deferredDate = null)
        {
            var settings = _settings;
            Validate();

            var job = new SmsJob
            {
                Receivers = new[] { phoneNumber },
                Sender = settings.Sender,
                Message = message,
                NoStopClause = true,
                DifferedPeriod = deferredDate.HasValue
                    ? (int)Math.Round((deferredDate.Value.ToUniversalTime() - DateTime.UtcNow).TotalMinutes)
                    : null
            };

            var body = JsonSerializer.Serialize(job, SerializerOptions);
            var timestamp = (await Http.GetStringAsync(settings.TimeEndpoint).ConfigureAwait(false)).Trim();

            var url = $"{settings.BalanceEndpoint}/{settings.ServiceName}/jobs";
            var signature = CreateSignature(settings, "POST", url, body, timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            AddAuthenticationHeaders(request, settings, signature, timestamp);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"[{response.StatusCode}] {payload}");
            }

            var apiResponse = JsonSerializer.Deserialize<SmsApiResponse>(payload) ?? new SmsApiResponse();

            return apiResponse;
        }

        public async Task<SmsOutgoingResponse> GetSmsOutgoingAsync(long smsId)
        {
            var settings = _settings;
            Validate();
            var timestamp = (await Http.GetStringAsync(settings.TimeEndpoint).ConfigureAwait(false)).Trim();
            var url = $"{settings.BalanceEndpoint}/{settings.ServiceName}/outgoing/{smsId}";
            var signature = CreateSignature(settings, "GET", url, string.Empty, timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthenticationHeaders(request, settings, signature, timestamp);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"[{response.StatusCode}] {payload}");
            }

            return JsonSerializer.Deserialize<SmsOutgoingResponse>(payload) ?? new SmsOutgoingResponse();
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(_settings.ApplicationKey) || string.IsNullOrWhiteSpace(_settings.ApplicationSecret)
                || string.IsNullOrWhiteSpace(_settings.ConsumerKey) || string.IsNullOrWhiteSpace(_settings.ServiceName)
                || string.IsNullOrWhiteSpace(_settings.Sender))
                throw new InvalidOperationException("Complétez les paramètres du compte SMS OVH.");
            if (!Uri.TryCreate(_settings.BalanceEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https"
                || !Uri.TryCreate(_settings.TimeEndpoint, UriKind.Absolute, out var time) || time.Scheme != "https")
                throw new InvalidOperationException("Les adresses API OVH doivent utiliser HTTPS.");
        }

        private static string CreateSignature(SmsSettings settings, string method, string url, string body, string timestamp)
        {
            var toSign = $"{settings.ApplicationSecret}+{settings.ConsumerKey}+{method}+{url}+{body}+{timestamp}";
            return "$1$" + Sha1Hex(toSign);
        }

        private static void AddAuthenticationHeaders(HttpRequestMessage request, SmsSettings settings, string signature, string timestamp)
        {
            request.Headers.TryAddWithoutValidation("X-Ovh-Application", settings.ApplicationKey);
            request.Headers.TryAddWithoutValidation("X-Ovh-Consumer", settings.ConsumerKey);
            request.Headers.TryAddWithoutValidation("X-Ovh-Signature", signature);
            request.Headers.TryAddWithoutValidation("X-Ovh-Timestamp", timestamp);
        }

        private static string Sha1Hex(string text)
        {
            using var sha = SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}

