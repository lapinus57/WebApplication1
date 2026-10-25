using System.Net;
using System.Security.Claims;
using ChatServeur;
using ChatServeur.Fundus;
using EyeChat.Fundus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

int count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
var day = new DateTime(2026, 10, 22, 9, 0, 0);
FoAnalysis? Analyze(params (int Days, string Text)[] rows) => FundusAnalysis.Analyze(rows.Select((r, i) => new FoConsultation(i.ToString(), day.AddDays(r.Days), r.Text)), day);
foreach (var text in new[] { "FO prochaine fois", "FO au prochain contrôle", "Contrôler les FO la prochaine fois", "Fond d'œil à prévoir", "FO à faire", "Revoir pour FO", "Dilatation au prochain RDV", "À dilater", "Prévoir FO", "Faire FO au prochain passage", "prévoir fond d’oeil", "F.O. au prochain contrôle", "fond d'OEIL À PRÉVOIR", "FO proch. contrôle" })
    Check(Analyze((-5, text))?.Status == FoStatus.Planned, "Request: " + text);
foreach (var text in new[] { "FO non nécessaire", "pas de FO prévu", "FO réalisé", "FO effectué", "FO fait", "FO normal", "Photo du patient", "FO ancien normal", "Dilatation réalisée", "FO réalisé en 2020", "Folie", "information" })
    Check(Analyze((-5, text)) == null, "Negative/history: " + text);
foreach (var text in new[] { "FO à prévoir si baisse visuelle", "FO prochaine fois ?", "FO à faire éventuellement", "FO non réalisé", "FO à confirmer", "FO peut-être réalisé", "FO sera réalisé", "ne pas faire de FO prochain contrôle" })
    Check(Analyze((-5, text))?.Status == FoStatus.Verify, "Ambiguous: " + text);
Check(Analyze((-8, "FO prochaine fois"), (-1, "RAS"))?.Status == FoStatus.Verify, "Later silence remains uncertain");
Check(Analyze((-8, "FO prochaine fois"), (-1, "FO réalisé"))?.Status == FoStatus.ProbablyPerformed, "Later completion");
Check(Analyze((-8, "FO prochaine fois"), (-1, "pas de FO réalisé"))?.Status == FoStatus.Verify, "Negated completion must not close");
Check(Analyze((-8, "FO prochaine fois"), (-1, "FO non nécessaire")) == null, "Cancelled request");
Check(Analyze((-8, "FO prochaine fois"), (-1, "pas de FO prévu")) == null, "No FO planned anymore");
Check(Analyze((-8, "FO prochaine fois"), (-1, "FO réalisé en 2020"))?.Status == FoStatus.Verify, "Historical result cannot close a recent request");
Check(Analyze((-8, "FO réalisé"), (-1, "FO prochaine fois"))?.Status == FoStatus.Planned, "Old completion before request");
Check(Analyze((-8, "FO prochaine fois"), (-3, "FO fait"), (-1, "Prévoir FO"))?.SourceId == "2", "New episode");
Check(Analyze((-8, "FO prochaine fois"), (2, "FO réalisé"))?.Status == FoStatus.Planned, "Future consultation ignored");
Check(Analyze((-8, "FO prochaine fois"), (-3, "FO réalisé"), (-1, "RAS"))?.Status == FoStatus.ProbablyPerformed, "Silence after explicit completion");
Check(Analyze((-8, "FO prochaine fois"), (-8, "FO réalisé"))?.Status == FoStatus.Verify, "Tied timestamps must not establish chronology");
Check(Analyze((-8, "FO prochaine fois"), (-8, "FO non nécessaire"))?.Status == FoStatus.Verify, "Tied cancellation is uncertain");
Check(Analyze((-1, "FO réalisé. Prévoir FO au prochain contrôle."))?.Status == FoStatus.Planned, "New request in same visit");
Check(Analyze((-1, "FO à prévoir ; FO à faire")) != null, "Repeated instructions yield one alert");
Check(Analyze((-1, "FO normal, cataracte à prévoir")) == null, "Future plan in another clause is not FO instruction");
Check(Analyze((-5, "FO prochaine fois"), (-1, "FO normal et OCT réalisé"))?.Status == FoStatus.Verify, "Other examination completion does not close FO");
Check(Analyze((-1, "FO réalisé et cataracte à prévoir")) == null, "Other future plan cannot reopen completed FO");
Check(Analyze((-1, "FO   au prochain contrôle"))?.Status == FoStatus.Planned, "Whitespace variations");
const string exact = "Contrôler les FO la prochaine fois";
Check(Analyze((-1, exact))?.Excerpt == exact, "Exact evidence preserves accents");
Check(Analyze((-1, "Pas de FO prévu. Prévoir FO au prochain contrôle."))?.Status == FoStatus.Planned, "Negation scoped to sentence");
Check(Analyze((-1, "FO prochaine fois"))?.EvidenceHash != Analyze((-1, "FO prochaine fois."))?.EvidenceHash, "Edited source invalidates review");

Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "", "FO")], day) == null, "Bare FO in REFRACTION is not a future instruction");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "", "FO prochaine fois")], day)?.Status == FoStatus.Planned, "Future instruction in REFRACTION");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "", "Pas de FO prévu")], day) == null, "Negated instruction in REFRACTION");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "FO")], day) == null, "Bare observation is not an instruction");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "FO prochaine fois", "FO réalisé")], day)?.Status == FoStatus.Planned, "Current FO in REFRACTION does not close a future instruction in Observation");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "FO réalisé", "FO prochaine fois")], day)?.Status == FoStatus.Planned, "Future instruction works regardless of the clinical field");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "FO non nécessaire", "FO prochaine fois")], day)?.Status == FoStatus.Verify, "Contradictory clinical fields need verification");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-2), "FO prochaine fois"), new("2", day.AddDays(-1), "", "FO réalisé")], day)?.Status == FoStatus.ProbablyPerformed, "Later completion in REFRACTION");
var realSchema = new[] { new FoField("Consultation", "N° consultation", 3), new("Consultation", "Code patient", 3), new("Consultation", "Date", 7), new("Consultation", "Observation", 130), new("Consultation", "ProchainRDV", 130), new("Consultation", "REFRACTION", 130) };
var suggested = FoMapping.Suggest(realSchema);
Check(suggested.ConsultationId == "N° consultation" && suggested.ConsultationPatientId == "Code patient", "Confirmed consultation key suggestions");
Check(suggested.ObservationFields.SequenceEqual(new[] { "Observation", "REFRACTION" }), "Confirmed text fields suggested");
Check(suggested.PatientId == "" && suggested.AppointmentDate == "", "Never invent fields absent from inspected schema");

var map = new FoMapping { PatientsTable = "P", PatientId = "Id", LastName = "Nom", FirstName = "Prénom", ConsultationsTable = "C", ConsultationId = "Id", ConsultationPatientId = "Patient", ConsultationDate = "Date", ObservationFields = ["Observation", "REFRACTION"], AppointmentsTable = "A", AppointmentPatientId = "Patient", AppointmentDate = "Date", AppointmentTime = "Heure", NoCancellationColumn = true };
var fields = new[] { new FoField("P", "Id", 3), new("P", "Nom", 130), new("P", "Prénom", 130), new("C", "Id", 3), new("C", "Patient", 3), new("C", "Date", 7), new("C", "Observation", 130), new("C", "REFRACTION", 130), new("A", "Patient", 3), new("A", "Date", 7), new("A", "Heure", 7) };
map.SelectClinicalFields(fields);
Check(map.ObservationFields.SequenceEqual(new[] { "Observation", "REFRACTION" }), "Only the two clinical fields are selected");
try { map.SelectClinicalFields(fields.Where(f => f.Name != "REFRACTION").ToList()); Check(false, "Missing REFRACTION must fail"); } catch (FoDataException) { count++; }
map.SelectClinicalFields(fields);
map.Validate(fields); count++;
try { map.Validate(fields.Where(f => f.Name != "Observation").ToList()); Check(false, "Unknown observation should fail"); } catch (FoDataException) { count++; }
map.NoCancellationColumn = false;
try { map.Validate(fields); Check(false, "Cancellation cannot be silently assumed absent"); } catch (FoDataException) { count++; }
map.NoCancellationColumn = true;
try { FoStore.ValidateRange(day.Date.AddDays(-1), day.Date.AddDays(1), day.Date); Check(false, "Past range"); } catch (FoDataException) { count++; }
try { FoStore.ValidateRange(day.Date, day.Date.AddDays(94), day.Date); Check(false, "Excessive range"); } catch (FoDataException) { count++; }

using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ChatDbContext>().UseSqlite(connection).Options;
await using var db = new ChatDbContext(options);
// Test upgrade of an existing database: unrelated tables and data survive.
await db.Database.ExecuteSqlRawAsync("CREATE TABLE ExistingData (Value TEXT); INSERT INTO ExistingData VALUES ('keep');");
FoSchema.Ensure(db); FoSchema.Ensure(db);
using (var command = connection.CreateCommand()) { command.CommandText = "SELECT Value FROM ExistingData"; Check((string?)await command.ExecuteScalarAsync() == "keep", "Idempotent migration preserves data"); }
var reader = new FakeReader { Time = day, Fields = fields };
var store = new FoStore(db, reader);
await store.SaveMappingAsync(map, default);
reader.Patients = [new("42", "Patient TEST", day.AddHours(2), [new("12", day.AddDays(-10), "FO prochaine fois")])];
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
var alert = await db.FoAlerts.SingleAsync();
Check(alert.Status == FoStatus.Planned && alert.Excerpt == "FO prochaine fois", "Initial persistence");
// Simulate an installed legacy configuration and approval before the field correction.
var settings = await db.FoSettings.SingleAsync();
var legacyMap = System.Text.Json.JsonSerializer.Deserialize<FoMapping>(settings.MappingJson)!;
legacyMap.ObservationFields = ["Observation", "ProchainRDV"];
settings.MappingJson = System.Text.Json.JsonSerializer.Serialize(legacyMap);
settings.SourceKey = reader.SourceKey(legacyMap);
alert.SourceKey = settings.SourceKey;
alert.ManualStatus = FoStatus.Verify;
alert.ReviewedBy = "admin-test";
await db.SaveChangesAsync();
reader.Fail = true;
try { await store.RefreshAsync(day.Date, day.Date.AddDays(1), default); Check(false, "Failed migration must not change settings"); } catch (FoDataException) { count++; }
Check((await db.FoSettings.AsNoTracking().SingleAsync()).MappingJson.Contains("ProchainRDV"), "Settings upgrade waits for a complete successful read");
reader.Fail = false;
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(reader.LastFields.SequenceEqual(new[] { "Observation", "REFRACTION" }), "Legacy configuration never reads ProchainRDV");
Check(!settings.MappingJson.Contains("ProchainRDV") && settings.MappingJson.Contains("REFRACTION"), "Successful refresh persists corrected field selection");
Check(await db.FoAlerts.CountAsync() == 1 && alert.ManualStatus == FoStatus.Verify, "Relevant manual review survives field migration without duplication");
reader.SourceName = "another-database";
try { await store.RefreshAsync(day.Date, day.Date.AddDays(1), default); Check(false, "Source change must still require configuration"); } catch (FoDataException) { count++; }
reader.SourceName = "source";
await store.ReviewAsync(alert.Id, alert.EvidenceHash, FoStatus.Verify, "admin-test", default);
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(await db.FoAlerts.CountAsync() == 1 && alert.ManualStatus == FoStatus.Verify, "Refresh is deduplicated and preserves review");
reader.Patients = [reader.Patients[0] with { Appointment = day.AddHours(3) }];
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(alert.ManualStatus == FoStatus.Verify && alert.NextAppointment == day.AddHours(3), "Appointment change preserves relevant review");
var oldHash = alert.EvidenceHash;
reader.Patients = [reader.Patients[0] with { Consultations = [new("12", day.AddDays(-10), "FO prochaine fois"), new("13", day.AddDays(-1), "", "FO réalisé")] }];
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(alert.Status == FoStatus.ProbablyPerformed && alert.ManualStatus == null, "New consultation clears obsolete review");
try { await store.ReviewAsync(alert.Id, oldHash, FoStatus.Planned, "admin-test", default); Check(false, "Stale review"); } catch (FoDataException) { count++; }
reader.Fail = true;
try { await store.RefreshAsync(day.Date, day.Date.AddDays(1), default); Check(false, "Access failure"); } catch (FoDataException) { count++; }
Check(await db.FoAlerts.CountAsync() == 1 && alert.NextAppointment == day.AddHours(3), "Read failure leaves old snapshot intact");
reader.Fail = false;
reader.Patients = [];
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(alert.NextAppointment == null, "Cancelled or moved-out appointment removed from window");
reader.Patients = [new("42", "Patient TEST", day.AddHours(2), [new("12", day.AddDays(-10), "FO prochaine fois"), new("14", day.AddDays(-1), "FO non nécessaire")])];
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(await db.FoAlerts.CountAsync() == 0, "Closed instruction removed");

// Real Razor routing, authentication and anti-forgery enforcement with a synthetic database.
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseTestServer();
builder.Services.AddDbContext<ChatDbContext>(o => o.UseSqlite(connection));
builder.Services.AddSingleton<IFoAccessReader>(reader);
builder.Services.AddScoped<FoStore>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddAuthorization();
builder.Services.AddRazorPages();
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapRazorPages();
app.MapPost("/test-login", async (HttpContext context) => await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin-test")], CookieAuthenticationDefaults.AuthenticationScheme))));
await app.StartAsync();
var http = app.GetTestClient();
var unauthorized = await http.GetAsync("/Fundus");
Check(unauthorized.StatusCode == HttpStatusCode.Redirect, "Anonymous access is denied");
var login = await http.PostAsync("/test-login", null);
http.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
var page = await http.GetAsync("/Fundus?configure=true");
var html = await page.Content.ReadAsStringAsync();
Check(page.IsSuccessStatusCode && html.Contains("fo-mapping"), "Authenticated Razor page renders real schema");
Check(page.Headers.CacheControl?.NoStore == true, "Medical results are not cached");
Check(html.Contains("__RequestVerificationToken"), "Forms include CSRF token");
var csrf = await http.PostAsync("/Fundus?handler=Refresh", new FormUrlEncodedContent(new Dictionary<string,string>()));
Check(csrf.StatusCode == HttpStatusCode.BadRequest, "Mutation without antiforgery token rejected");
var tokenMatch = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
Check(tokenMatch.Success, "Read rendered antiforgery token");
var token = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value);
var antiCookie = page.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(".AspNetCore.Antiforgery")).Split(';')[0];
var authCookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];
http.DefaultRequestHeaders.Remove("Cookie");
http.DefaultRequestHeaders.Add("Cookie", authCookie + "; " + antiCookie);
reader.Patients = [new("42", "<script>Patient TEST</script>", day.AddHours(2), [new("12", day.AddDays(-10), "FO prochaine fois <img src=x onerror=alert(1)>")])];
var refresh = await http.PostAsync("/Fundus?handler=Refresh", new FormUrlEncodedContent(new Dictionary<string,string> { ["From"] = "2026-10-22", ["To"] = "2026-10-22", ["__RequestVerificationToken"] = token }));
var refreshed = await refresh.Content.ReadAsStringAsync();
Check(refresh.IsSuccessStatusCode && refreshed.Contains("Analyse actualis"), "Valid refresh through real form binding");
Check(refreshed.Contains("&lt;script&gt;Patient TEST&lt;/script&gt;") && !refreshed.Contains("<img src=x"), "Medical text and patient names HTML-encoded");
var persisted = await db.FoAlerts.AsNoTracking().SingleAsync();
var reviewResponse = await http.PostAsync("/Fundus?handler=Review", new FormUrlEncodedContent(new Dictionary<string,string> { ["id"] = persisted.Id.ToString(), ["evidenceHash"] = persisted.EvidenceHash, ["review"] = "Verify", ["From"] = "2026-10-22", ["To"] = "2026-10-22", ["__RequestVerificationToken"] = token }));
Check(reviewResponse.IsSuccessStatusCode && (await reviewResponse.Content.ReadAsStringAsync()).Contains("Validation manuelle"), "Valid review through real form binding");
var sourceResponse = await http.GetAsync("/Fundus?from=2026-10-22&to=2026-10-22&source=" + persisted.Id);
Check(sourceResponse.IsSuccessStatusCode && (await sourceResponse.Content.ReadAsStringAsync()).Contains("consultation-source"), "Source text read on demand");

// Opening the page schedules one protected POST; a GET itself must not mutate stored analysis.
int loadsBefore = reader.LoadCount;
var landing = await http.GetStringAsync("/Fundus");
Check(landing.Contains("data-auto-search=\"true\"") && landing.Contains("value=\"2026-10-28\""), "Opening shows automatic search and defaults to seven days");
Check(reader.LoadCount == loadsBefore, "GET does not run a database refresh");
Check(html.Contains("data-auto-search=\"false\""), "Settings view does not trigger automatic search");
Check(refreshed.Contains("data-auto-search=\"false\""), "Refresh response cannot loop into another automatic refresh");
Check((await sourceResponse.Content.ReadAsStringAsync()).Contains("data-auto-search=\"false\""), "Source reading does not refresh the entire list");

reader.Patients = [
    new("early", "Premier TEST", day.AddHours(1), [new("1", day.AddDays(-1), "", "FO prochaine fois")]),
    new("late", "Second TEST", day.AddDays(2), [new("2", day.AddDays(-3), "FO prochaine fois"), new("3", day.AddDays(-1), "RAS")]),
    new("done", "Termine TEST", day.AddDays(1), [new("4", day.AddDays(-3), "FO prochaine fois"), new("5", day.AddDays(-1), "", "FO réalisé")])
];
async Task<string> SearchPage(string status = "") => await (await http.PostAsync("/Fundus?handler=Refresh", new FormUrlEncodedContent(new Dictionary<string,string> {
    ["From"] = "2026-10-22", ["To"] = "2026-10-28", ["Status"] = status, ["__RequestVerificationToken"] = token
}))).Content.ReadAsStringAsync();
var candidates = await SearchPage();
Check(candidates.Contains("Premier TEST") && candidates.Contains("Second TEST") && !candidates.Contains("Termine TEST"), "Default list includes both planned and uncertain patients, excluding completed ones");
Check(candidates.IndexOf("Premier TEST", StringComparison.Ordinal) < candidates.IndexOf("Second TEST", StringComparison.Ordinal), "Patients ordered by appointment");
Check(candidates.Contains("fo-patient-search") && candidates.Contains("REFRACTION"), "Name search and actual source fields visible");
var completed = await SearchPage("ProbablyPerformed");
Check(completed.Contains("Termine TEST") && !completed.Contains("Premier TEST"), "Completed cases remain available in their own filter");
reader.Fail = true;
var failurePage = await SearchPage();
Check(failurePage.Contains("Premier TEST") && failurePage.Contains("fo-error") && !failurePage.Contains("Aucun patient avec un rendez-vous"), "Failed refresh keeps prior results and displays failure, not a false empty result");
Check(failurePage.Contains("data-auto-search=\"false\""), "Failure cannot trigger an automatic retry loop");
reader.Fail = false;
reader.Patients = [];
var empty = await SearchPage();
Check(empty.Contains("Aucun patient avec un rendez-vous futur") && !empty.Contains("Premier TEST"), "Successful empty agenda clears the visible list");
reader.Patients = [new("none", "Sans consigne TEST", day.AddHours(1), [new("6", day.AddDays(-1), "RAS", "Correction stable")])];
var noInstruction = await SearchPage();
Check(noInstruction.Contains("Aucune consigne de FO retenue") && !noInstruction.Contains("Aucun patient avec un rendez-vous futur"), "No instruction and empty agenda have distinct messages");
var invalidDate = await http.GetStringAsync("/Fundus?From=invalide");
Check(invalidDate.Contains("fo-error") && !invalidDate.Contains("data-auto-search=\"true\""), "Invalid dates do not auto-search silently");
// The configuration form supplies identifiers, not an arbitrary list of medical fields.
var mappingValues = new Dictionary<string,string> {
    ["__RequestVerificationToken"] = token, ["Mapping.PatientsTable"] = "P", ["Mapping.PatientId"] = "Id",
    ["Mapping.LastName"] = "Nom", ["Mapping.FirstName"] = "Prénom", ["Mapping.ConsultationsTable"] = "C",
    ["Mapping.ConsultationId"] = "Id", ["Mapping.ConsultationPatientId"] = "Patient", ["Mapping.ConsultationDate"] = "Date",
    ["Mapping.AppointmentsTable"] = "A", ["Mapping.AppointmentPatientId"] = "Patient", ["Mapping.AppointmentDate"] = "Date",
    ["Mapping.AppointmentTime"] = "Heure", ["Mapping.NoCancellationColumn"] = "true",
    ["Mapping.ObservationFields"] = "ProchainRDV"
};
var configuredPage = await (await http.PostAsync("/Fundus?handler=Mapping", new FormUrlEncodedContent(mappingValues))).Content.ReadAsStringAsync();
Check(configuredPage.Contains("data-auto-search=\"true\"") && !configuredPage.Contains("id=\"fo-mapping\""), "Saving settings transitions directly to patient search");
Check(!(await db.FoSettings.AsNoTracking().SingleAsync()).MappingJson.Contains("ProchainRDV"), "Posted legacy text selection cannot override the required clinical fields");
NativeChecks.Run(Check, day);
MedicalTextChecks.Run(Check, day);
ClinicalWordingChecks.Run(Check, day);
AnnualFollowUpChecks.Run(Check, day);
await PatientRegistryChecks.RunAsync(Check, day);
TimelineCcamChecks.Run(Check);
ExamLearningChecks.Run(Check);
ExamReviewChecks.Run(Check);
Console.WriteLine($"{count} FO checks passed.");

sealed class FakeReader : IFoAccessReader
{
    public DateTime Time { get; set; }
    public DateTime Now => Time;
    public IReadOnlyList<FoField> Fields { get; set; } = [];
    public IReadOnlyList<FoPatientData> Patients { get; set; } = [];
    public bool Fail { get; set; }
    public string SourceName { get; set; } = "source";
    public string[] LastFields { get; private set; } = [];
    public int LoadCount { get; private set; }
    public IReadOnlyList<FoField> Inspect() => Fields;
    public string SourceKey(FoMapping mapping) => FundusAnalysis.Hash(SourceName + System.Text.Json.JsonSerializer.Serialize(mapping));
    public FoDataSet Load(FoMapping mapping, DateTime from, DateTime until, CancellationToken ct)
    {
        LoadCount++;
        LastFields = mapping.ObservationFields.ToArray();
        if (Fail) throw new FoDataException("Simulated failure");
        return new(SourceKey(mapping), Patients.Where(p => p.Appointment >= from && p.Appointment < until && p.Appointment >= Now).ToList());
    }
    public FoConsultation ReadConsultation(FoMapping mapping, string patientId, string consultationId) => Patients.Single(p => p.Id == patientId).Consultations.Single(c => c.Id == consultationId);
}
