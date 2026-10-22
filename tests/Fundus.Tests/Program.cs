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

Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "", "FO")], day)?.Status == FoStatus.Verify, "Bare FO in confirmed follow-up field");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "", "FO prochaine fois")], day)?.Status == FoStatus.Planned, "Follow-up instruction");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "", "Pas de FO prévu")], day) == null, "Negated follow-up instruction");
Check(FundusAnalysis.Analyze([new("1", day.AddDays(-1), "FO")], day) == null, "Bare observation is not an instruction");
var realSchema = new[] { new FoField("Consultation", "N° consultation", 3), new("Consultation", "Code patient", 3), new("Consultation", "Date", 7), new("Consultation", "Observation", 130), new("Consultation", "ProchainRDV", 130) };
var suggested = FoMapping.Suggest(realSchema);
Check(suggested.ConsultationId == "N° consultation" && suggested.ConsultationPatientId == "Code patient", "Confirmed consultation key suggestions");
Check(suggested.ObservationFields.SequenceEqual(new[] { "Observation", "ProchainRDV" }), "Confirmed text fields suggested");
Check(suggested.PatientId == "" && suggested.AppointmentDate == "", "Never invent fields absent from inspected schema");

var map = new FoMapping { PatientsTable = "P", PatientId = "Id", LastName = "Nom", FirstName = "Prénom", ConsultationsTable = "C", ConsultationId = "Id", ConsultationPatientId = "Patient", ConsultationDate = "Date", ObservationFields = ["Texte"], AppointmentsTable = "A", AppointmentPatientId = "Patient", AppointmentDate = "Date", AppointmentTime = "Heure", NoCancellationColumn = true };
var fields = new[] { new FoField("P", "Id", 3), new("P", "Nom", 130), new("P", "Prénom", 130), new("C", "Id", 3), new("C", "Patient", 3), new("C", "Date", 7), new("C", "Texte", 130), new("A", "Patient", 3), new("A", "Date", 7), new("A", "Heure", 7) };
map.Validate(fields); count++;
try { map.Validate(fields.Where(f => f.Name != "Texte").ToList()); Check(false, "Unknown observation should fail"); } catch (FoDataException) { count++; }
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
await store.ReviewAsync(alert.Id, alert.EvidenceHash, FoStatus.Verify, "admin-test", default);
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(await db.FoAlerts.CountAsync() == 1 && alert.ManualStatus == FoStatus.Verify, "Refresh is deduplicated and preserves review");
reader.Patients = [reader.Patients[0] with { Appointment = day.AddHours(3) }];
await store.RefreshAsync(day.Date, day.Date.AddDays(1), default);
Check(alert.ManualStatus == FoStatus.Verify && alert.NextAppointment == day.AddHours(3), "Appointment change preserves relevant review");
var oldHash = alert.EvidenceHash;
reader.Patients = [reader.Patients[0] with { Consultations = [new("12", day.AddDays(-10), "FO prochaine fois"), new("13", day.AddDays(-1), "FO réalisé")] }];
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
Console.WriteLine($"{count} FO checks passed.");

sealed class FakeReader : IFoAccessReader
{
    public DateTime Time { get; set; }
    public DateTime Now => Time;
    public IReadOnlyList<FoField> Fields { get; set; } = [];
    public IReadOnlyList<FoPatientData> Patients { get; set; } = [];
    public bool Fail { get; set; }
    public IReadOnlyList<FoField> Inspect() => Fields;
    public string SourceKey(FoMapping mapping) => "test-source";
    public FoDataSet Load(FoMapping mapping, DateTime from, DateTime until, CancellationToken ct) => Fail ? throw new FoDataException("Simulated failure") : new("test-source", Patients);
    public FoConsultation ReadConsultation(FoMapping mapping, string patientId, string consultationId) => Patients.Single(p => p.Id == patientId).Consultations.Single(c => c.Id == consultationId);
}
