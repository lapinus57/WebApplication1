using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;

var day = new DateTime(2026, 10, 23);
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
RdvInfo Person(string name, string phone, string email = "") => new()
{
    Nom = "Famille", Prenom = name, DateRDV = day, HeureRDV = day.AddHours(9), Tel1 = phone, Email = email
};
var settings = new SmsMailSettings { Cabinet = "Cabinet & associés" };
var alice = Person("Alice", "06 12 34 56 78", "parent@example.com");
var bob = Person("Bob", "+33612345678", "PARENT@example.com");
var cancelled = Person("Annulé", "0611223344"); cancelled.IsCancelled = true;
var wrongDay = Person("Demain", "0655443322"); wrongDay.DateRDV = day.AddDays(1);
var messages = MessagePreparation.Prepare(new[] { alice, bob, cancelled, wrongDay }, day, settings);
Check(messages.Count == 1 && messages[0].Channel == "Mail", "Fallback excludes SMS, cancelled RDVs and other dates");
Check(messages[0].Family.Contains("Alice") && messages[0].Family.Contains("Bob"), "Shared contact is grouped");
Check(messages[0].Body.Contains("Cabinet &amp; associ") && !messages[0].Body.Contains("Cabinet & associés"), "HTML is encoded");
settings.SmsOnlyWhenNoEmail = false;
messages = MessagePreparation.Prepare(new[] { alice, bob }, day, settings);
Check(messages.Count == 2 && messages.Single(m => m.Channel == "SMS").Contact == "+33612345678", "Normalised numbers are deduplicated");
var key = messages.Single(m => m.Channel == "SMS").Key;
settings.SmsTemplate = "Modèle modifié [PRENOMS]";
Check(MessagePreparation.Prepare(new[] { alice }, day, settings).Single(m => m.Channel == "SMS").Key == key, "Template changes cannot bypass duplicate guard");
var invalidMail = Person("Sans mail valide", "0612345678", "invalide");
settings.SmsOnlyWhenNoEmail = true;
Check(MessagePreparation.Prepare(new[] { invalidMail }, day, settings).Single().Channel == "SMS", "Invalid email permits SMS fallback");
Check(MessagePreparation.NormalizePhone("0033612345678 (parent)") == "+33612345678", "International phone and notes");
Check(MessagePreparation.NormalizePhone("0123456789") == "", "French landlines cannot receive mobile SMS");
Check(MessagePreparation.SmsMetrics(new string('a', 160)).Contains("1 SMS"), "GSM single segment");
Check(MessagePreparation.SmsMetrics(new string('a', 161)).Contains("2 SMS"), "GSM multipart");
Check(MessagePreparation.SmsMetrics(new string('^', 81)).Contains("2 SMS"), "Extended GSM consumes two septets");
Check(MessagePreparation.SmsMetrics(new string('漢', 71)).Contains("2 SMS · Unicode"), "Unicode multipart");
alice.Prenom = "<script>[CABINET]</script>";
var rendered = MessagePreparation.Render("[PRENOMS] [LISTE_PERSONNES]", new[] { alice }, settings.Cabinet, true);
Check(!rendered.Contains("<script>") && rendered.Contains("[CABINET]"), "Patient input cannot become markup or nested template tokens");

var databasePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
try
{
    var history = new MessageHistory(databasePath);
    var history2 = new MessageHistory(databasePath);
    var prepared = messages[0];
    Check(await history.ReserveAsync(prepared), "First send reserves key before submission");
    Check(!await history2.ReserveAsync(prepared), "Another client cannot submit the reserved key");
    var record = (await history2.LoadAsync()).Single();
    Check(record.Status.Contains("à vérifier"), "Interrupted sends remain uncertain");
    await history.UpdateAsync(prepared.Key, "Accepté par OVH", 42);
    Check((await history2.LoadAsync()).Single().ProviderId == 42, "Provider ID survives reopening");
    Check(!await history2.ReserveAsync(prepared), "Accepted sends remain blocked across restarts");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    File.Delete(databasePath);
}

var smsSettings = new SmsSettings
{
    ApplicationKey = "test-app", ApplicationSecret = "test-secret", ConsumerKey = "test-consumer",
    ServiceName = "test-service", Sender = "Cabinet"
};
var handler = new FakeOvhHandler(smsSettings);
var sms = new SmsService(smsSettings, new HttpClient(handler));
Check(await sms.GetSmsBalanceAsync() == 12.5m, "Decimal OVH credits are supported");
var response = await sms.SendSmsAsync("+33612345678", "Bonjour é");
Check(response.Ids.Single() == 42 && response.TotalCreditsRemoved == 0.5m, "OVH acceptance parsed without a separate SQL logger");
Check(handler.ValidatedRequests == 2, "Signed balance and send requests validated");
var receipt = await sms.GetSmsOutgoingAsync(42);
Check(receipt.DeliveryReceipt == 1 && receipt.Credits == 0.5m, "Delivery receipt parsed");
handler.Reject = true;
try { await sms.SendSmsAsync("+33612345678", "Bonjour"); throw new Exception("Rejection was ignored"); }
catch (InvalidOperationException) { checks++; }
Check(handler.Submissions == 2, "No automatic retry after rejected submission");
Console.WriteLine($"{checks} checks passed; no real SMS or mail sent.");

sealed class FakeOvhHandler(SmsSettings settings) : HttpMessageHandler
{
    public bool Reject { get; set; }
    public int ValidatedRequests { get; private set; }
    public int Submissions { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/auth/time")) return Reply("1700000000");
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var text = $"{settings.ApplicationSecret}+{settings.ConsumerKey}+{request.Method}+{request.RequestUri}+{body}+1700000000";
        var signature = "$1$" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (request.Headers.GetValues("X-Ovh-Signature").Single() != signature) throw new Exception("Invalid OVH signature");
        if (request.Headers.GetValues("X-Ovh-Application").Single() != settings.ApplicationKey) throw new Exception("Invalid app key");
        ValidatedRequests++;
        if (request.Method == HttpMethod.Post)
        {
            Submissions++;
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.GetProperty("receivers")[0].GetString() != "+33612345678") throw new Exception("Invalid recipient");
            return Reject ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("rejected") }
                : Reply("{\"ids\":[42],\"validReceivers\":[\"+33612345678\"],\"invalidReceivers\":[],\"totalCreditsRemoved\":0.5}");
        }
        return request.RequestUri.AbsolutePath.Contains("/outgoing/")
            ? Reply("{\"id\":42,\"deliveryReceipt\":1,\"credits\":0.5}") : Reply("{\"creditsLeft\":12.5}");
    }
    static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
}
