using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ChatServeur;
using ChatServeur.Registry;
using EyeChat.Fundus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal static class PatientRegistryChecks
{
    public static async Task RunAsync(Action<bool, string> check, DateTime day)
    {
        PatientConditionResult Analyze(string text, string refraction = "") => PatientConditions.Analyze(new[] { new FoConsultation("1", day.AddDays(-1), text, refraction) }, day);
        foreach (var text in new[] { "DIABÈTE", "Patient diabétique", "Diabète ancien", "Diabète non insulinodépendant", "DID", "DNID", "Antécédent personnel de diabète" })
            check(Analyze(text).Diabetes.State == PatientConditionState.Present, "Registry detects personal diabetes: " + text);
        foreach (var text in new[] { "HTO", "Hypertension oculaire", "H.T.O.", "Hypertonie oculaire traitée", "Tension oculaire élevée" })
            check(Analyze("", text).OcularHypertension.State == PatientConditionState.Present, "Registry detects HTO in REFRACTION: " + text);
        foreach (var text in new[] { "Non diabétique", "Pas de diabète", "Diabète exclu", "Absence d'antécédent de diabète" })
            check(Analyze(text).Diabetes.State == PatientConditionState.Absent, "Explicit negative is distinguished from silence: " + text);
        foreach (var text in new[] { "HTO ?", "HTO non confirmée", "Suspicion d'HTO", "HTO non exclue" })
            check(Analyze(text).OcularHypertension.State == PatientConditionState.ToReview, "Suspected/unconfirmed HTO is not a negative diagnosis: " + text);
        foreach (var text in new[] { "RAS", "HTA", "TONUS MESURÉ PAR AIR", "Traitement hypotenseur", "Mère diabétique", "ATCD FAMILIAUX :\nDIABETE : OUI\nHTO : OUI", "Pré-diabétique", "Diabète insipide" })
            check(Analyze(text).Diabetes.State == PatientConditionState.Unknown && Analyze(text).OcularHypertension.State == PatientConditionState.Unknown, "No inference from absent/unrelated/family context: " + text);
        check(Analyze("PAS DE RÉTINOPATHIE DIABÉTIQUE").Diabetes.State == PatientConditionState.ToReview, "Absence of retinopathy does not establish or exclude diabetes");
        check(Analyze("DIABETE", "Non diabétique").Diabetes.State == PatientConditionState.ToReview, "Contradictory clinical fields require review");
        var visits = new[] { new FoConsultation("1", day.AddDays(-10), "Diabète"), new FoConsultation("2", day.AddDays(-1), "FO réalisé\nPas de FO prochain contrôle") };
        check(PatientConditions.Analyze(visits, day).Diabetes.State == PatientConditionState.Present, "FO completion/cancellation does not erase a chronic condition in registry");
        check(PatientConditions.Analyze(visits.Append(new("3", day, "Non diabétique")), day).Diabetes.State == PatientConditionState.Absent, "Later explicit correction supersedes old condition");
        check(PatientConditions.Analyze(visits.Append(new("3", day.AddDays(1), "Non diabétique")), day).Diabetes.State == PatientConditionState.Present, "Future consultation cannot change current registry status");
        check(PatientConditions.Analyze(visits.Append(new("3", day, "Pas de rétinopathie diabétique")), day).Diabetes.State == PatientConditionState.Present, "Complication context cannot erase a previous explicit condition");
        check(Analyze("", "HTO").OcularHypertension.Evidence.Contains("REFRACTION"), "Registry evidence identifies the actual source field");
        var longA = "Diabète " + new string('a', 1000);
        check(Analyze(longA).Diabetes.EvidenceHash != Analyze(longA + "b").Diabetes.EvidenceHash, "Full evidence changes invalidate review even after excerpt clipping");
        check(Analyze(longA).Diabetes.Evidence.Length <= 600, "Registry persists only bounded evidence");
        check(Analyze(FoMedicalText.Read("<p>Diabète</p><p>HTO</p>")).OcularHypertension.State == PatientConditionState.Present, "Registry consumes clinical HTML converted to text");

        var folder = Path.Combine(Path.GetTempPath(), "patient-registry-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "chat.db");
            var options = new DbContextOptionsBuilder<ChatDbContext>().UseSqlite("Data Source=" + path).Options;
            // Mimic an existing chat database: schema installation must be additive and repeatable.
            await using (var raw = new SqliteConnection("Data Source=" + path))
            {
                await raw.OpenAsync(); using var command = raw.CreateCommand();
                command.CommandText = "CREATE TABLE ExistingData (Value TEXT); INSERT INTO ExistingData VALUES ('keep')"; await command.ExecuteNonQueryAsync();
            }
            var reader = new Reader { Now = day };
            var map = Mapping(); map.SelectClinicalFields(reader.Inspect()); map.ValidateClinical(reader.Inspect());
            check(true, "Registry mapping validates without an appointment table");
            await using (var db = new ChatDbContext(options))
            {
                PatientRegistrySchema.Ensure(db); PatientRegistrySchema.Ensure(db);
                var store = new PatientRegistryStore(db, reader); await store.SaveMappingAsync(map, default);
            }
            async Task<int> Refresh(Action<int>? progress = null, CancellationToken ct = default)
            { await using var db = new ChatDbContext(options); return await new PatientRegistryStore(db, reader).RefreshAllAsync(progress, ct); }
            async Task<List<PatientRegistryEntry>> Rows()
            { await using var db = new ChatDbContext(options); return await db.PatientRegistry.AsNoTracking().OrderBy(p => p.AccessPatientId).ToListAsync(); }
            async Task Review(PatientRegistryEntry row, PatientConditionState? diabetes, PatientConditionState? hto)
            { await using var db = new ChatDbContext(options); await new PatientRegistryStore(db, reader).ReviewAsync(row.Id, row.Revision, diabetes, hto, "admin-test", default); }
            reader.Patients = new[] { new RegistryPatient("1", "Patient TEST", "Un", new[] { new FoConsultation("1", day.AddDays(-10), "Diabète", "HTO") }), new RegistryPatient("2", "Sans consultation TEST", "Deux", Array.Empty<FoConsultation>()) };
            check(await Refresh() == 2, "All patients are imported even with no appointment or consultation");
            var rows = await Rows();
            check(rows.Count == 2 && rows[1].Diabetes == PatientConditionState.Unknown && rows[1].Hto == PatientConditionState.Unknown, "No consultation produces unknown flags, never false negatives");
            await Review(rows[0], PatientConditionState.Absent, PatientConditionState.Present);
            var reviewed = (await Rows())[0];
            check(reviewed.Diabetes == PatientConditionState.Absent && reviewed.DiabetesDetected == PatientConditionState.Present && reviewed.DiabetesReviewedBy == "admin-test", "Manual correction retains automatic evidence and reviewer");
            check(!reviewed.DiabetesNeedsReview && reviewed.DiabetesReviewedUtc.HasValue, "New manual review captures evidence and timestamp");
            await Refresh();
            var unchanged = (await Rows())[0];
            check(unchanged.Diabetes == PatientConditionState.Absent && !unchanged.DiabetesNeedsReview && unchanged.DiabetesReviewedUtc == reviewed.DiabetesReviewedUtc, "Unchanged refresh preserves manual status and audit information");
            try { await Review(reviewed, PatientConditionState.Present, null); check(false, "Stale form must fail"); } catch (FoDataException) { check(true, "Stale form rejected after refresh"); }
            reader.Patients = new[] { reader.Patients[0] with { Consultations = new[] { new FoConsultation("new", day.AddDays(-1), "Patient diabétique confirmé", "HTO") } }, reader.Patients[1] };
            await Refresh(); var changed = (await Rows())[0];
            check(changed.Diabetes == PatientConditionState.Absent && changed.DiabetesNeedsReview, "New evidence flags re-review without silently replacing manual correction");
            await Review(changed, null, null); var automatic = (await Rows())[0];
            check(automatic.Diabetes == PatientConditionState.Present && automatic.DiabetesReviewedBy == "" && !automatic.DiabetesReviewedUtc.HasValue, "Returning to automatic clears manual reviewer only");
            await Review(automatic, PatientConditionState.Present, null);
            reader.Patients = new[] { reader.Patients[1] }; await Refresh();
            check(!(await Rows())[0].IsActive && (await Rows())[0].DiabetesManual == PatientConditionState.Present, "Patient absent from a successful import is retained with manual history");
            try { await Review((await Rows())[0], null, null); check(false, "Inactive patient must not be reviewed as current"); } catch (FoDataException) { check(true, "Inactive patient review rejected"); }
            var stable = (await Rows())[1].Revision;
            reader.Patients = new[] { new RegistryPatient("3", "New TEST", "Three", Array.Empty<FoConsultation>()) }; reader.FailAfterFirst = true;
            try { await Refresh(); check(false, "Partial Access read must fail"); } catch (FoDataException) { check(true, "Partial read error surfaced"); }
            check((await Rows()).Count == 2 && (await Rows())[1].Revision == stable, "Failed import neither inserts partial patients nor changes existing rows");
            reader.FailAfterFirst = false;
            using (var cancellation = new CancellationTokenSource())
            {
                try { await Refresh(_ => cancellation.Cancel(), cancellation.Token); check(false, "Cancelled import must fail"); } catch (OperationCanceledException) { check(true, "Cancellation stops import"); }
            }
            check((await Rows())[1].Revision == stable, "Cancelled scan preserves previous complete registry");
            reader.Patients = new[] { reader.Patients[0], reader.Patients[0] };
            try { await Refresh(); check(false, "Duplicate patient ids must fail"); } catch (FoDataException) { check(true, "Duplicate identifiers rejected before transaction"); }
            check((await Rows()).Count == 2, "Duplicate import cannot duplicate existing patient rows");
            reader.Patients = Array.Empty<RegistryPatient>(); await Refresh();
            check((await Rows()).All(r => !r.IsActive), "Successful empty source deactivates previous list without deleting reviews");
            reader.Patients = new[] { new RegistryPatient("1", "Retour TEST", "Un", new[] { new FoConsultation("1", day.AddDays(-1), "Diabète") }) }; await Refresh();
            check((await Rows())[0].IsActive && (await Rows())[0].DiabetesManual == PatientConditionState.Present, "Reappearing patient keeps identity and manual decision");
            reader.SourceName = "other";
            try { await Refresh(); check(false, "Changed Access source must not be silently imported"); } catch (FoDataException) { check(true, "Source change requires mapping confirmation"); }
            await using (var db = new ChatDbContext(options)) await new PatientRegistryStore(db, reader).SaveMappingAsync(map, default);
            await Refresh();
            check((await Rows()).Count == 3 && (await Rows()).Single(r => r.SourceKey == reader.RegistrySourceKey(map)).DiabetesManual == null, "Separate Access sources never share an override based on matching id alone");
            await using (var raw = new SqliteConnection("Data Source=" + path))
            {
                await raw.OpenAsync(); using var command = raw.CreateCommand(); command.CommandText = "SELECT Value FROM ExistingData";
                check((string?)await command.ExecuteScalarAsync() == "keep", "Existing server data survives schema setup and imports");
            }

            await CheckPageAsync(check, options, reader);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }

    private static async Task CheckPageAsync(Action<bool, string> check, DbContextOptions<ChatDbContext> options, Reader reader)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(options); builder.Services.AddScoped<ChatDbContext>();
        builder.Services.AddSingleton<IRegistryAccessReader>(reader); builder.Services.AddScoped<PatientRegistryStore>(); builder.Services.AddSingleton<PatientRegistryImport>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(); builder.Services.AddAuthorization(); builder.Services.AddRazorPages();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapRazorPages();
        app.MapPost("/test-login", async (HttpContext context) => await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "admin-test") }, CookieAuthenticationDefaults.AuthenticationScheme))));
        await app.StartAsync(); var http = app.GetTestClient();
        check((await http.GetAsync("/PatientRegistry")).StatusCode == HttpStatusCode.Redirect, "Registry page denies anonymous access");
        check((await http.GetAsync("/PatientRegistry?handler=Progress")).StatusCode == HttpStatusCode.Redirect, "Import progress also requires authentication");
        var login = await http.PostAsync("/test-login", null); var auth = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        http.DefaultRequestHeaders.Add("Cookie", auth);
        int before = reader.ReadCount;
        var page = await http.GetAsync("/PatientRegistry"); var html = await page.Content.ReadAsStringAsync();
        check(page.IsSuccessStatusCode && html.Contains("Diabète et tension oculaire"), "Registry Razor page renders");
        check(reader.ReadCount == before, "Opening registry does not start an import or read all medical records");
        check(page.Headers.CacheControl?.NoStore == true, "Registry is not cached");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        check(token.Length > 0, "Registry forms include antiforgery tokens");
        var anti = page.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(".AspNetCore.Antiforgery")).Split(';')[0];
        http.DefaultRequestHeaders.Remove("Cookie"); http.DefaultRequestHeaders.Add("Cookie", auth + "; " + anti);
        foreach (var handler in new[] { "Import", "Cancel", "Review", "Mapping" })
            check((await http.PostAsync("/PatientRegistry?handler=" + handler, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode == HttpStatusCode.BadRequest, "Registry mutation requires CSRF: " + handler);
        Task<HttpResponseMessage> Post(string handler, Dictionary<string, string>? values = null)
        { values ??= new(); values["__RequestVerificationToken"] = token; return http.PostAsync("/PatientRegistry?handler=" + handler, new FormUrlEncodedContent(values)); }
        reader.Patients = Enumerable.Range(1, 105).Select(i => new RegistryPatient(i.ToString(), i == 1 ? "<script>Patient TEST</script>" : $"TEST {i:000}", "Prénom", new[] { new FoConsultation(i.ToString(), reader.Now.AddDays(-1), "Diabète <img src=x onerror=alert(1)>", i % 2 == 0 ? "HTO" : "") })).ToArray();
        var started = await Post("Import");
        check(started.IsSuccessStatusCode && (await started.Content.ReadAsStringAsync()).Contains("Import d"), "Authenticated POST starts background import");
        var coordinator = app.Services.GetRequiredService<PatientRegistryImport>();
        for (int i = 0; i < 1000 && coordinator.Status.Running; i++) await Task.Delay(10);
        check(!coordinator.Status.Running && !coordinator.Status.Failed && coordinator.Status.Processed == 105, "Background import completes all patients");
        var listed = await http.GetStringAsync("/PatientRegistry");
        check(listed.Contains("105 patient") && listed.Contains("page 1 sur 2"), "Full patient catalogue is paginated");
        check(listed.Contains("&lt;script&gt;Patient TEST&lt;/script&gt;") && !listed.Contains("<script>Patient TEST"), "Patient names are HTML encoded");
        var searched = await (await Post("Search", new() { ["Query"] = "TEST 105", ["PageNumber"] = "1" })).Content.ReadAsStringAsync();
        check(searched.Contains("TEST 105") && !searched.Contains("TEST 104"), "Name search uses protected POST and returns matching registry records");
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var key = reader.RegistrySourceKey(Mapping());
        var row = await db.PatientRegistry.AsNoTracking().SingleAsync(p => p.SourceKey == key && p.AccessPatientId == "1");
        var details = await (await Post("Details", new() { ["id"] = row.Id.ToString() })).Content.ReadAsStringAsync();
        check(details.Contains("registry-details") && details.Contains("&lt;img src=x") && !details.Contains("<img src=x"), "Clinical evidence is encoded in details");
        var review = await (await Post("Review", new() { ["id"] = row.Id.ToString(), ["revision"] = row.Revision, ["diabetesManual"] = "Absent", ["htoManual"] = "ToReview" })).Content.ReadAsStringAsync();
        check(WebUtility.HtmlDecode(review).Contains("Vérification enregistrée") && (await db.PatientRegistry.AsNoTracking().SingleAsync(p => p.Id == row.Id)).DiabetesManual == PatientConditionState.Absent, "Manual flags persist through real form binding");
        var stale = await (await Post("Review", new() { ["id"] = row.Id.ToString(), ["revision"] = row.Revision, ["diabetesManual"] = "Present" })).Content.ReadAsStringAsync();
        check(WebUtility.HtmlDecode(stale).Contains("changé depuis"), "Stale page cannot overwrite a newer manual review");
        reader.FailAfterFirst = true; await Post("Import");
        for (int i = 0; i < 1000 && coordinator.Status.Running; i++) await Task.Delay(10);
        check(coordinator.Status.Failed && await db.PatientRegistry.CountAsync(p => p.SourceKey == key && p.IsActive) == 105, "Background failure keeps last complete registry and reports error");
        reader.FailAfterFirst = false;
        using (var entered = new ManualResetEventSlim())
        {
            reader.BeforeRead = ct => { entered.Set(); ct.WaitHandle.WaitOne(); ct.ThrowIfCancellationRequested(); };
            check(coordinator.Start(), "Background import starts after a failure");
            check(entered.Wait(TimeSpan.FromSeconds(5)) && !coordinator.Start(), "Concurrent background imports are rejected");
            await Post("Cancel");
            for (int i = 0; i < 1000 && coordinator.Status.Running; i++) await Task.Delay(10);
            check(!coordinator.Status.Running && !coordinator.Status.Failed && coordinator.Status.Message.Contains("interrompu"), "Authenticated cancellation stops background scan cleanly");
            check(await db.PatientRegistry.CountAsync(p => p.SourceKey == key && p.IsActive) == 105, "Background cancellation retains complete catalogue");
            reader.BeforeRead = null;
        }
        var config = await http.GetStringAsync("/PatientRegistry?configure=true");
        check(config.Contains("registry-mapping") && !config.Contains("Mapping.AppointmentDate"), "Registry setup needs no appointment mapping");
    }

    private static FoMapping Mapping() => new() { PatientsTable = "P", PatientId = "Id", LastName = "Nom", FirstName = "Prénom", ConsultationsTable = "C", ConsultationId = "Id", ConsultationPatientId = "Patient", ConsultationDate = "Date", ObservationFields = new[] { "Observation", "REFRACTION" } };
    private sealed class Reader : IRegistryAccessReader
    {
        public DateTime Now { get; set; }
        public string SourceName = "test";
        public RegistryPatient[] Patients = Array.Empty<RegistryPatient>();
        public bool FailAfterFirst;
        public int ReadCount;
        public Action<CancellationToken>? BeforeRead;
        public IReadOnlyList<FoField> Inspect() => new[] { new FoField("P", "Id", 3), new("P", "Nom", 130), new("P", "Prénom", 130), new("C", "Id", 3), new("C", "Patient", 3), new("C", "Date", 7), new("C", "Observation", 130), new("C", "REFRACTION", 130) };
        public string RegistrySourceKey(FoMapping mapping) => FundusAnalysis.Hash(SourceName + "|" + mapping.PatientsTable + "|" + mapping.PatientId);
        public IEnumerable<RegistryPatient> ReadRegistryPatients(FoMapping mapping, CancellationToken ct)
        {
            ReadCount++;
            BeforeRead?.Invoke(ct);
            foreach (var patient in Patients) { ct.ThrowIfCancellationRequested(); yield return patient; if (FailAfterFirst) throw new FoDataException("Échec de lecture simulé."); }
        }
    }
}
