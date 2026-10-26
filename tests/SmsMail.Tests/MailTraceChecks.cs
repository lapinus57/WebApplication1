using System.Text.Json;
using Client.SmsMail.Configuration;
using Client.SmsMail.Models;
using Client.SmsMail.Services;
using Client.SmsMail.Services.Grouping;
using ChatServeur.Messaging;
using EyeChat.Messaging;
using Microsoft.Data.Sqlite;

internal static class MailTraceChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); count++; }
        var day = new DateTime(2026, 10, 23);
        RdvInfo Person(int id, string email = "") => new() { Id = id, Prenom = "Test" + (char)('A' + id), Nom = "Nom" + (char)('A' + id), DateRDV = day, HeureRDV = day.AddHours(9).AddMinutes(id * 10), Email = email };
        var settings = new SmsMailSettings { UseEyeEmailFamilies = true, SmsOnlyWhenNoEmail = false, EmailTemplate = "<html><body><p>[LISTE_PERSONNES]</p></body></html>" };
        PreparedMessage Mail(params RdvInfo[] people) => MessagePreparation.Prepare(people, day, settings).First(m => m.Channel == "Mail");
        var parent = Person(1, "parent@example.com"); parent.Tel1 = "0612345678";
        var child = Person(2); child.Tel1 = "+33612345678";
        var prepared = Mail(parent, child);
        var trace = MailTrace.Parse(prepared.TraceJson);
        Check(MailReference.IsValid(prepared.Reference), "New mail has a random numeric reference");
        Check(prepared.Body.IndexOf(MailReference.Display(prepared.Reference), StringComparison.Ordinal) < prepared.Body.IndexOf("</body>", StringComparison.Ordinal), "Reference is inside the HTML body");
        Check(MailTextPreview.Convert(prepared.Body).Contains(MailReference.Display(prepared.Reference)), "Reference is readable in text preview");
        Check(MailReference.Matches(prepared.Reference, MailReference.Display(prepared.Reference).Replace('-', ' ')), "Reference search accepts spaces instead of hyphens");
        Check(!MailReference.Matches(prepared.Reference, "---") && !MailReference.Matches(prepared.Reference, "a"), "Empty or malformed reference query does not match");
        Check(trace.People.Count == 2 && trace.People.Single(p => p.PatientId == 2).DestinationReason.Contains("patient 1"), "Child inclusion explains which family member supplied destination");
        Check(trace.People.All(p => p.GroupingReasons.Any(r => r.StartsWith("Téléphone 1 commun"))), "Actual telephone grouping is recorded for both members");
        Check(trace.People.Single(p => p.PatientId == 1).DestinationReason.Contains("ses coordonnées du rendez-vous Access"), "Direct contact inclusion is distinguished");
        var phoneTrace = prepared.TraceJson;
        child.Nom = "CHANGÉ"; parent.Tel1 = child.Tel1 = ""; child.Email = "elsewhere@example.com";
        Mail(parent, child);
        Check(prepared.TraceJson == phoneTrace && !prepared.TraceJson.Contains("CHANG"), "Prepared audit is immutable after source regrouping");
        Check(child.GroupingReasons.Count == 0, "New grouping clears old reasons");
        var regenerated = Mail(parent);
        Check(prepared.Reference != regenerated.Reference && prepared.Key == regenerated.Key, "Regeneration changes random reference without changing duplicate guard");
        var sms = MessagePreparation.Prepare(new[] { SetPhone(Person(7)) }, day, settings).Single();
        Check(sms.Reference == "" && sms.TraceJson == "" && !sms.Body.Contains("Référence"), "SMS size and content remain unaffected");

        var byEmail1 = Person(3, "same@example.com"); var byEmail2 = Person(4, "same@example.com");
        Check(MailTrace.Parse(Mail(byEmail1, byEmail2).TraceJson).People.All(p => p.GroupingReasons.Any(r => r.StartsWith("Adresse mail commune"))), "Common mail rule is captured");
        var byAddress1 = Person(5, "address@example.com"); var byAddress2 = Person(6);
        byAddress1.Adresse = byAddress2.Adresse = "12 rue du TEST"; byAddress1.Ville = byAddress2.Ville = "VILLE TEST";
        Check(MailTrace.Parse(Mail(byAddress1, byAddress2).TraceJson).People.All(p => p.GroupingReasons.Any(r => r.StartsWith("Adresse et ville"))), "Address grouping is captured only when effective");
        var known1 = Person(7, "known@example.com"); var known2 = Person(8); known1.ReferenceFamilyNumber = known2.ReferenceFamilyNumber = 42;
        known1.EyeEmailEmails.Add(known1.Email);
        var knownTrace = MailTrace.Parse(Mail(known1, known2).TraceJson);
        Check(knownTrace.People.All(p => p.ReferenceFamilyNumber == 42 && p.GroupingReasons.Any(r => r.Contains("EyeEmail 42"))), "Known family id and membership rule are recorded");
        Check(knownTrace.People.All(p => p.DestinationReason.Contains("base familles EyeEmail")), "EyeEmail contact origin is retained");
        var remark1 = Person(9, "remark@example.com"); var remark2 = Person(10); remark1.Remarque = "avec " + remark2.Prenom + " " + remark2.Nom;
        var remarkTrace = MailTrace.Parse(Mail(remark1, remark2).TraceJson);
        Check(remarkTrace.People.Count == 2 && remarkTrace.People.All(p => p.GroupingReasons.Any(r => r.Contains("mention « avec »"))), "Remark inclusion records actual merged people");
        var first = Person(11, "name@example.com"); var second = Person(12); second.Nom = first.Nom;
        Check(MailTrace.Parse(Mail(first, second).TraceJson).People.All(p => p.GroupingReasons.Any(r => r.StartsWith("Nom et proximité"))), "Name/time grouping is captured");
        settings.UseEyeEmailFamilies = false;
        var noGrouping = MailTrace.Parse(Mail(byEmail1, byEmail2).TraceJson);
        Check(!noGrouping.GroupingEnabled && noGrouping.People.Count == 2 && noGrouping.People.All(p => p.GroupingReasons.Count == 0), "Same destination merges independent appointments without inventing family rule");
        settings.UseEyeEmailFamilies = true;
        known1.HeureRDV = day.AddHours(11.5); known2.HeureRDV = day.AddHours(12);
        settings.AdjustReminderTimes = true;
        settings.ReminderTimeRules = new() { new() { From = TimeSpan.FromHours(11.5), To = TimeSpan.FromHours(11).Add(TimeSpan.FromMinutes(20)) } };
        var shifted = MailTrace.Parse(Mail(known1, known2).TraceJson).People.Single(p => p.PatientId == 7);
        Check(shifted.AppointmentAt.Hour == 11 && shifted.AppointmentAt.Minute == 30 && shifted.AnnouncedAt.Minute == 20 && known1.HeureRDV.Minute == 30, "Audit separates real and announced time without mutating appointment");
        var example = TestMessagePreparation.Create(settings, "test@example.com", "", day).Single();
        var exampleTrace = MailTrace.Parse(example.TraceJson);
        Check(exampleTrace.IsTest && exampleTrace.People.Single().DestinationReason.Contains("formulaire de test"), "Test mail is traced as fictitious and explicit destination");
        Check(example.Body.Contains(MailReference.Display(example.Reference)), "Test mail includes its own reference");
        Check(MailTracePreparation.Describe("").Contains("ne peuvent pas être reconstituées"), "Legacy mail does not invent historical reasons");
        Check(!prepared.Body.Contains("Téléphone 1 commun") && !prepared.Body.Contains("patient 1"), "Internal grouping reasons are never sent to recipient");
        settings.Email.FromAddress = settings.Email.SenderAddress = "test@example.com";
        using (var mime = new MailService(settings.Email).CreateMessage(prepared))
        {
            Check(mime.Headers["X-EyeChat-Reference"] == prepared.Reference, "MIME carries the same opaque reference");
            Check(mime.AlternateViews.Any(v => v.ContentType.MediaType == "text/html") && mime.Attachments.Any(a => a.Name == "rendez-vous.ics"), "HTML MIME and invitation remain available");
        }

        // Manual confirmation is recorded at the accepted decision, not from a trial match.
        var singleton = Person(13); singleton.DatePriseRDV = day.AddDays(-1).AddHours(8);
        var family = new RdvFamille { Numero = 1 }; family.RDVs.Add(first); family.RDVs.Add(second);
        first.DatePriseRDV = second.DatePriseRDV = singleton.DatePriseRDV; family.DatePriseRDV.Add(first.DatePriseRDV); family.UpdateStatistics();
        int number = 2;
        GroupingByDate.Regrouper(new() { singleton }, new() { family }, ref number, new Dictionary<int, List<(DateTime, DateTime?, DateTime?)>>(), (_, _) => false);
        Check(singleton.GroupingReasons.Count == 0, "Rejected manual proposal produces no reason");
        GroupingByDate.Regrouper(new() { singleton }, new() { family }, ref number, new Dictionary<int, List<(DateTime, DateTime?, DateTime?)>>(), (_, _) => true);
        Check(singleton.GroupingReasons.Any(r => r.StartsWith("Ajout confirmé par l’utilisateur")), "Accepted manual proposal produces its own reason");

        var folder = Path.Combine(Path.GetTempPath(), "mail-trace-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var db = Path.Combine(folder, "server.db");
            // Existing deployed schema must survive the additive trace table.
            await using (var legacy = new SqliteConnection("Data Source=" + db))
            {
                await legacy.OpenAsync(); using var command = legacy.CreateCommand();
                command.CommandText = "CREATE TABLE SharedMessageHistory (MessageKey TEXT PRIMARY KEY,Channel TEXT NOT NULL,Contact TEXT NOT NULL,Body TEXT NOT NULL,Status TEXT NOT NULL,CreatedAt TEXT NOT NULL,ProviderId INTEGER NULL,ProviderService TEXT NOT NULL,Version INTEGER NOT NULL,Workstation TEXT NOT NULL); INSERT INTO SharedMessageHistory VALUES ('old','Mail','test@example.com','old body','Archive','2026-01-01T00:00:00Z',NULL,'',1,'OLD')";
                await command.ExecuteNonQueryAsync();
            }
            var store = new SharedMessageHistoryStore(db);
            var apiA = new Api(store, "POSTE A"); var apiB = new Api(new(db), "POSTE B");
            var localPath = Path.Combine(folder, "a.db");
            var a = new SharedMessageHistory(localPath, apiA); var b = new SharedMessageHistory(Path.Combine(folder, "b.db"), apiB);
            Check((await b.ReadDetailsAsync("old")).Reference == "", "Old schema and untraced message remain readable");
            Check(await a.ReserveAsync(prepared), "Reference and audit reserved before sending");
            var row = (await b.LoadAsync()).Single(e => e.Key == prepared.Key);
            Check(row.Reference == prepared.Reference && row.ReferenceDisplay.Contains(MailReference.Display(prepared.Reference)), "Second workstation lists searchable reference");
            Check(row.TraceJson.Length == 0 && row.Body.Length == 0, "List does not transfer full audit or body");
            var details = await b.ReadDetailsAsync(row.Key);
            Check(details.TraceJson == phoneTrace && details.Subject == prepared.Subject && details.Body == prepared.Body, "Other workstation gets frozen reasons and original mail");
            await a.UpdateAsync(row.Key, "Accepté SMTP");
            Check((await b.ReadDetailsAsync(row.Key)).TraceJson == phoneTrace, "Delivery update cannot rewrite inclusion reasons");
            var regeneratedSameContact = Mail(parent);
            Check(!await b.ReserveAsync(regeneratedSameContact), "Different random number cannot bypass contact deduplication");
            Check((await b.ReadDetailsAsync(row.Key)).Reference == prepared.Reference, "Duplicate attempt preserves sent reference");
            var duplicate = TestMessagePreparation.Create(settings, "collision@example.com", "", day).Single();
            duplicate.Body = duplicate.Body.Replace(MailReference.Display(duplicate.Reference), MailReference.Display(prepared.Reference)); duplicate.Reference = prepared.Reference;
            try { await b.ReserveAsync(duplicate); Check(false, "Duplicate reference must be refused"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("déjà utilisée"), "Collision explicitly refused before sending"); }
            Check(await store.GetAsync(duplicate.Key) == null && (await store.FindReservedAsync(new() { duplicate.Key })).Count == 0, "Reference collision rolls back message and guards");
            duplicate.Reference = MailTracePreparation.CreateReference(); duplicate.Body = MailTracePreparation.Footer("<p>TEST</p>", duplicate.Reference);
            Check(await b.ReserveAsync(duplicate), "Fresh reference succeeds after collision rollback");
            var invalid = new HistoryTransfer { Entry = new() { Key = "badtrace", Channel = "Mail", Contact = "test@example.com", Reference = "12345678901234567890", Body = "1234-5678-9012-3456-7890", TraceJson = "{}" } };
            try { await store.ReserveAsync(invalid, "TEST"); Check(false, "Malformed audit must fail"); } catch (ArgumentException) { count++; }
            Check(await store.GetAsync("badtrace") == null, "Malformed audit creates no reservation");
            var archive = new MessageHistory(localPath);
            var local = (await archive.LoadAsync()).Single();
            Check(local.TraceJson == phoneTrace && local.Reference == prepared.Reference, "Local archive retains reference for later server recovery");
            var recoveredStore = new SharedMessageHistoryStore(Path.Combine(folder, "recovered.db"));
            await new SharedMessageHistory(localPath, new Api(recoveredStore, "RECOVERY")).SynchronizeAsync();
            Check((await recoveredStore.GetAsync(prepared.Key))?.TraceJson == phoneTrace, "Re-import to restored server retains frozen audit");
            var savedTrace = (await store.GetAsync(prepared.Key))!.TraceJson;
            var import = new HistoryTransfer { Entry = details }; import.Entry.TraceJson = example.TraceJson;
            await store.ReserveAsync(import, "OTHER", import: true);
            Check((await store.GetAsync(prepared.Key))!.TraceJson == savedTrace, "Archive re-import never overwrites existing audit");
            var unverified = TestMessagePreparation.Create(settings, "oldserver@example.com", "", day).Single(); apiA.DropTraceOnRead = true;
            try { await a.ReserveAsync(unverified); Check(false, "Unverified trace must block provider call"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("Aucun mail envoyé"), "Client blocks when old server fails to retain audit"); }
            apiA.DropTraceOnRead = false;
            Check((await store.FindReservedAsync(new() { unverified.Key })).Count == 1, "Uncertain trace verification does not release reservation");
            Check((await new SharedMessageHistoryStore(db).GetAsync(row.Key))!.Reference == prepared.Reference, "Reference survives server restart");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
        return count;
        RdvInfo SetPhone(RdvInfo row) { row.Tel1 = "0699999999"; return row; }
    }

    private sealed class Api(SharedMessageHistoryStore store, string workstation) : ISharedMessageHistoryApi
    {
        public bool DropTraceOnRead;
        public Task<string> HistoryServerIdAsync() => store.ServerIdAsync();
        public async Task ImportHistoryAsync(HistoryTransfer entry) => await store.ReserveAsync(entry, workstation, true);
        public Task<bool> ReserveHistoryAsync(HistoryTransfer entry) => store.ReserveAsync(entry, workstation);
        public Task<List<string>> FindReservedHistoryAsync(List<string> keys) => store.FindReservedAsync(keys);
        public Task<HistoryPage> LoadHistoryPageAsync(long cursor) => store.LoadPageAsync(cursor);
        public async Task<HistoryRecord?> GetHistoryMessageAsync(string key) { var entry = await store.GetAsync(key); if (entry != null && DropTraceOnRead) { entry.TraceJson = ""; entry.Reference = ""; } return entry; }
        public Task<bool> UpdateHistoryAsync(HistoryUpdate update) => store.UpdateAsync(update);
    }
}
