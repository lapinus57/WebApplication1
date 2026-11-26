using System.Text;
using ChatServeur.Families;
using EyeChat.Families;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("ÉCHEC : " + message);
    checks++;
}

var folder = Path.Combine(Path.GetTempPath(), "families-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var dbPath = Path.Combine(folder, "families.db");
try
{
    var store = new FamilyStore(dbPath);

    // Photo Vitale
    var vitale = new List<VitaleRow>
    {
        new(1, "DUPONT", "MARTIN", "Marie", new DateTime(1980, 5, 1), "HASHCARTE1", 0, true, new DateTime(2010, 1, 1), null) { UsualName = "DUPONT", Sex = "F", LastConsultation = new DateTime(2019, 3, 2) },
        new(2, "DUPONT", "", "Lucas", new DateTime(2012, 5, 1), "HASHCARTE1", 6, null, null, new DateTime(2030, 1, 1)),
    };
    await store.ReplaceVitaleAsync(vitale, "export-vitale.csv");
    var loaded = store.LoadVitale();
    Check(loaded.Count == 2 && loaded[0] == vitale[0] && loaded[1] == vitale[1], "Photo Vitale relue à l'identique");
    await store.ReplaceVitaleAsync(vitale.Take(1).ToList(), "autre.csv");
    Check(store.VitaleCount() == 1 && store.Meta("vitale_file") == "autre.csv", "Un nouvel import Vitale remplace le précédent");

    // Paiements : le NIR n'est jamais stocké, ni son empreinte
    var csv = "IdPayeur;DateFacture;NomPayeur;PrenomPayeur;NomBeneficiaire;PrenomBeneficiaire;NIRBeneficiaire\n"
        + ";2025-02-03;CPAM;;ROUX;Léa;2850357463012\n"
        + ";2025-02-10;CPAM;;ROUX;Claire;2850357463012\n"
        + ";2025-03-10;CPAM;;GRIS;Marc;1700357000111\n";
    var rows = PaymentLinks.ReadCsv(new StringReader(csv));
    var batch = await store.AddPaymentsAsync(rows, "paiements-2025.csv");
    Check(batch.Lines == 3 && batch.First == new DateTime(2025, 2, 3) && batch.Last == new DateTime(2025, 3, 10), "Export des paiements enregistré");
    var payments = store.LoadPayments();
    Check(payments[0].Card == payments[1].Card && payments[0].Card != payments[2].Card && payments[0].Card == $"{batch.Id}:1" && payments[0].Batch == batch.Id.ToString(),
        "Cartes réduites à un numéro de groupe par export");
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    var bytes = Encoding.Latin1.GetString(File.ReadAllBytes(dbPath) .Concat(File.Exists(dbPath + "-wal") ? File.ReadAllBytes(dbPath + "-wal") : Array.Empty<byte>()).ToArray());
    Check(!bytes.Contains("2850357463012") && !bytes.Contains(rows[0].Card), "Ni NIR ni empreinte dans la base");
    var second = await store.AddPaymentsAsync(rows, "paiements-2026.csv");
    Check(store.PaymentBatches().Count == 2 && store.LoadPayments().Count == 6, "Deuxième export ajouté");
    await store.DeletePaymentBatchAsync(second.Id);
    Check(store.PaymentBatches().Single().Id == batch.Id && store.LoadPayments().Count == 3, "Export supprimé");

    // Décisions : la plus récente l'emporte pour une même paire
    await store.AddDecisionAsync(new FamilyDecision(FamilyDecisionKind.Separate, 12, 11), "secretariat");
    await store.AddDecisionAsync(new FamilyDecision(FamilyDecisionKind.Join, 11, 12), "secretariat");
    await store.AddDecisionAsync(new FamilyDecision(FamilyDecisionKind.Validate, 0, 0, "3,4"), "secretariat");
    var decisions = store.LoadDecisions();
    Check(decisions.Count == 2 && decisions.Any(d => d is { Kind: FamilyDecisionKind.Join, A: 11, B: 12 }) && decisions.Any(d => d.Kind == FamilyDecisionKind.Validate && d.Members == "3,4"),
        "Décisions : regrouper après séparer = regroupés");

    // Résultat d'un calcul
    var scanner = new FamilyScanner { Today = new DateTime(2026, 11, 24) };
    foreach (var day in new[] { new DateTime(2025, 3, 2), new DateTime(2026, 3, 2) })
    {
        scanner.Add(new ScanRow(501, day, day.AddHours(9), null, null, "", "BRUN", "", "Ana", "4 rue B", "Metz", new[] { "0601000501" }, Array.Empty<string>(), false));
        scanner.Add(new ScanRow(502, day, day.AddHours(9), null, null, "", "BRUN", "", "Eli", "4 rue B", "Metz", new[] { "0601000501" }, Array.Empty<string>(), false));
        scanner.Add(new ScanRow(503, day, day.AddHours(11), null, null, "", "ROSE", "", "Ines", "8 rue R", "Metz", new[] { "0601000503" }, Array.Empty<string>(), false));
    }
    var result = scanner.Finish();
    await store.SaveResultAsync(result, FamilyScanReport.Anonymous(result));
    var members = store.Memberships(new[] { 501, 502, 503, 999 });
    Check(members.Count == 2 && members.All(m => m.FamilyNumber == 1 && !m.ToReview) && members[0].Why.Contains("Ana BRUN et Eli BRUN") && members[0].Why.Contains("même adresse"),
        "Familles des patients du jour, avec le pourquoi");
    Check(members[0].Members.SequenceEqual(new[] { 501, 502 }) && members[0].Links.Single() is { A: 501, B: 502 } link0 && link0.Reasons.Contains("même adresse")
        && !link0.Reasons.Contains("BRUN"), "Membres et relations transmis au poste, sans nom dans les raisons");
    Check(store.FamilyCount() == 1 && store.FamilyCount(toReviewOnly: true) == 0 && store.Families(patientId: 502).Single().Members.Count == 2, "Recherche d'une famille par patient");
    Check(store.Meta("last_report")!.Contains("Familles") && store.Meta("last_run_utc") != null, "Rapport anonymisé conservé");
    await store.SaveResultAsync(result, "");
    Check(store.FamilyCount() == 1, "Un nouveau calcul remplace le précédent");

    // Réponses des postes à « Famille à vérifier »
    var chain = new ScanFamily { Number = 1, HasChain = true };
    chain.Members.AddRange(new[] { 701, 702, 703 });
    var chainResult = new ScanResult
    {
        Families = new[] { chain },
        PatientsById = new[] { 701, 702, 703 }.ToDictionary(id => id, id => new PatientInfo { Id = id, Name = "Patient " + id, LastSeen = new DateTime(2026, 1, 1) }),
    };
    var solidLink = new PatientLink { A = 703, B = 704, Strength = LinkStrength.Forte, SameHouseholdNow = true, Reasons = "même adresse" };
    var weakLink = new PatientLink { A = 704, B = 705, Strength = LinkStrength.Moyenne, SameHouseholdNow = true, Reasons = "même nom" };
    var big = new ScanFamily { Number = 2, HasChain = true };
    big.Members.AddRange(new[] { 703, 704, 705 });
    var bigResult = new ScanResult
    {
        Families = new[] { big }, Links = new[] { solidLink, weakLink },
        PatientsById = new[] { 703, 704, 705 }.ToDictionary(id => id, id => new PatientInfo { Id = id, Name = "Patient " + id, LastSeen = new DateTime(2026, 1, 1) }),
    };
    var solidStore = new FamilyStore(Path.Combine(folder, "solid.db"));
    await solidStore.SaveResultAsync(bigResult, "");
    Check(solidStore.Memberships(new[] { 703, 704 }).All(m => !m.ToReview), "Famille à vérifier, mais ces deux-là ont un lien fort direct : pas de question");
    Check(solidStore.Memberships(new[] { 704, 705 }).All(m => m.ToReview && m.ReviewReason == "lien moyen ou faible entre ces personnes"), "Lien direct moyen : question");
    Check(solidStore.Memberships(new[] { 703, 705 }).All(m => m.ToReview && m.ReviewReason == "pas de lien direct entre ces personnes"), "Pas de lien direct : question");
    var answerStore = new FamilyStore(Path.Combine(folder, "answers.db"));
    await answerStore.SaveResultAsync(chainResult, "");
    Check(answerStore.Memberships(new[] { 701, 702 }).All(m => m.ToReview && m.SubGroup == 0 && m.Why.StartsWith("À vérifier")), "Famille à vérifier : la question sera posée");
    Check(await answerStore.RecordAnswerAsync(new[] { 702, 701 }, together: true, "poste ACCUEIL") == 1, "Réponse « Regrouper » enregistrée");
    Check(answerStore.Memberships(new[] { 701, 702 }).All(m => !m.ToReview && m.SubGroup == 0), "Confirmés ensemble : plus de question, sur aucun poste");
    Check(answerStore.Memberships(new[] { 701, 702, 703 }).All(m => m.ToReview), "Une autre personne de la famille ce jour-là : on redemande");
    await answerStore.RecordAnswerAsync(new[] { 702, 703 }, together: false, "poste ACCUEIL");
    var apart = answerStore.Memberships(new[] { 702, 703 });
    Check(apart.Single(m => m.PatientId == 702).SubGroup != apart.Single(m => m.PatientId == 703).SubGroup, "Gardés séparés : groupes différents, immédiatement");
    Check(answerStore.LoadDecisions().Count == 2 && answerStore.FamilyCount(toReviewOnly: true) == 1, "Réponses gardées pour le prochain calcul ; la famille reste à vérifier sur la page");
    Check(await answerStore.RecordAnswerAsync(new[] { 702 }, together: true, "x") == 0, "Une seule personne : rien à enregistrer");


// ---------- Page d'administration, servie pour de vrai ----------
{
    var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions
    {
        ApplicationName = typeof(Serveur.Pages.FamiliesModel).Assembly.GetName().Name, EnvironmentName = "Development"
    });
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddRazorPages().AddApplicationPart(typeof(Serveur.Pages.FamiliesModel).Assembly);
    builder.Services.AddAuthentication("test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuth>("test", null);
    builder.Services.AddAuthorization();
    var pageStore = new FamilyStore(Path.Combine(folder, "page.db"));
    builder.Services.AddSingleton(pageStore);
    builder.Services.AddSingleton<FamilyRebuild>();
    var app = builder.Build();
    app.UseAuthentication(); app.UseAuthorization();
    app.MapRazorPages();
    await app.StartAsync();
    var address = app.Urls.First();
    using var http = new HttpClient { BaseAddress = new Uri(address) };

    var page = System.Net.WebUtility.HtmlDecode(await http.GetStringAsync("/Families"));
    Check(page.Contains("Familles reconstituées") && page.Contains("0 famille(s)"), "Page affichée (base vide)");
    string Token(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    async Task<string> Upload(string handler, string content, string name)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(Token(await http.GetStringAsync("/Families"))), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name);
        var response = await http.PostAsync("/Families?handler=" + handler, form);
        return System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
    var vitaleCsv = "PAT_ID;NOM;NOM_NAISSANCE;PRENOM;NAISSANCE;CARTE;QUALITE;ACTIF;CREATION;FIN_VALIDITE\n1;DUPONT;;Marie;1980-05-01;H1;0;1;2010-01-01;\n2;DUPONT;;Lucas;2012-05-01;H1;6;1;2012-06-01;\n";
    var afterVitale = await Upload("Vitale", vitaleCsv, "export-vitale.csv");
    Check(afterVitale.Contains("Photo de la carte Vitale importée : 2 fiches") && pageStore.VitaleCount() == 2, "Import de la photo Vitale depuis la page");
    var afterPayments = await Upload("Payments", csv, "paiements.csv");
    Check(afterPayments.Contains("Export des paiements ajouté : 3 factures") && afterPayments.Contains("paiements.csv") && !afterPayments.Contains("2850357463012"),
        "Ajout d'un export des paiements depuis la page");

    await pageStore.SaveResultAsync(result, FamilyScanReport.Anonymous(result));
    var withFamily = System.Net.WebUtility.HtmlDecode(await http.GetStringAsync("/Families?Patient=501"));
    Check(withFamily.Contains("Famille 1") && withFamily.Contains("Ana BRUN") && withFamily.Contains("même adresse"), "Famille d'un patient, avec le pourquoi");
    var join = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = Token(withFamily), ["first"] = "502", ["second"] = "503"
    });
    var joined = System.Net.WebUtility.HtmlDecode(await (await http.PostAsync("/Families?handler=Join", join)).Content.ReadAsStringAsync());
    Check(joined.Contains("seront regroupés") && pageStore.LoadDecisions().Single() is { Kind: FamilyDecisionKind.Join, A: 502, B: 503 }, "Regroupement enregistré");
    var remove = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = Token(withFamily), ["patient"] = "501", ["members"] = "501,502"
    });
    await http.PostAsync("/Families?handler=Remove", remove);
    Check(pageStore.LoadDecisions().Any(d => d is { Kind: FamilyDecisionKind.Separate, A: 501, B: 502 }), "Retrait du foyer enregistré");
    var progress = await http.GetStringAsync("/Families?handler=Progress");
    Check(progress.Contains("\"running\":false"), "Suivi du calcul en JSON");
    await app.StopAsync();
}
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(folder, true); } catch (IOException) { }
}
Console.WriteLine($"{checks} vérifications réussies.");

sealed class TestAuth(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
    ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin") }, "test");
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(
            new Microsoft.AspNetCore.Authentication.AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(identity), "test")));
    }
}
