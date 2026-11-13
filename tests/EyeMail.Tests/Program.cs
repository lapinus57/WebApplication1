// Vérifie la base EyeMail (data/eyemail.db) : reconnaissance des colonnes Access, synchronisation
// et import complet avec une source Access simulée. Données synthétiques uniquement.
using ChatServeur.EyeMail;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("ÉCHEC : " + message);
    checks++;
}

var today = new DateTime(2026, 11, 13);

// 1. Colonnes : noms d'Access tels quels, avec accents, espaces ou casse différente.
var columns = EyeMailColumns.Detect(new[] { "Code Patient", "TITRE", "Nom", "Prénom", "Nom jeune fille", "Date de naissance",
    "Adresse", "CP Ville", "Tel 1", "Tel 1 Info", "Portable", "E-mail", "Observation" });
Check(columns.Found["Id"] == "Code Patient" && columns.Found["FirstName"] == "Prénom" && columns.Found["Phone2"] == "Portable", "Colonnes Access reconnues");
Check(!columns.Found.ContainsKey("PostalCode") && columns.Found["City"] == "CP Ville", "CP Ville reconnu comme ville");
Check(!columns.Found.Values.Contains("Observation"), "Les colonnes médicales ne sont pas recopiées");
var row = columns.Map(new object?[] { 42, "Mme", "MARTIN", "Léa", "DUPONT", new DateTime(1980, 5, 2, 0, 0, 0), "12 rue des Lilas", "57000 Metz",
    "03 87 00 00 00", "fixe", "06 12 34 56 78", "lea@example.fr", "texte médical" }, today)!;
Check(row.AccessPatientId == "42" && row.LastName == "MARTIN" && row.MaidenName == "DUPONT", "Identité recopiée");
Check(row.PostalCode == "57000" && row.City == "Metz", "« 57000 Metz » séparé en code postal et ville");
Check(row.BirthDate == new DateTime(1980, 5, 2) && row.Phone2 == "06 12 34 56 78" && row.Email == "lea@example.fr", "Naissance, portable et e-mail");
Check(columns.Map(new object?[] { DBNull.Value, "", "SANS", "Id", "", null, "", "", "", "", "", "", "" }, today) == null, "Ligne sans identifiant ignorée");
var future = columns.Map(new object?[] { 7, "", "X", "Y", "", "01/01/2030", "", "", "", "", "", "", "" }, today)!;
Check(future.BirthDate == null, "Date de naissance future ignorée");
try { EyeMailColumns.Detect(new[] { "Prénom", "Ville" }); Check(false, "Sans Code Patient ni Nom, l'import doit être refusé"); }
catch (InvalidDataException ex) { Check(ex.Message.Contains("Code Patient") && ex.Message.Contains("Ville"), "Refus expliqué avec les colonnes trouvées"); }
Check(EyeMailColumns.SearchText("Léa", "D'Almeida-Brück") == "LEA DALMEIDABRUCK", "Texte de recherche sans accents ni ponctuation");

// 2. Synchronisation dans une vraie base SQLite temporaire.
var folder = Path.Combine(Path.GetTempPath(), "EyeMail-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var options = new DbContextOptionsBuilder<EyeMailDbContext>().UseSqlite($"Data Source={Path.Combine(folder, "eyemail.db")}").Options;
    EyeMailDbContext NewDb() => new(options);
    using (var db = NewDb()) EyeMailSchema.Ensure(db);

    EyeMailSourcePatient P(string id, string last, string first, string phone = "", string email = "")
        => new(id, "", last, first, "", null, "", "", "", "", phone, "", "", "", email, "");
    EyeMailSourceData Source(params EyeMailSourcePatient[] patients) => new("Patients", new Dictionary<string, string>(), patients);

    using (var db = NewDb())
    {
        var run = await new EyeMailStore(db).SyncAsync(Source(P("1", "MARTIN", "Léa"), P("2", "DURAND", "Paul"), P("3", "PETIT", "Zoé")), default);
        Check(run.Read == 3 && run.Added == 3 && run.Updated == 0 && run.Deactivated == 0, "Premier import : 3 patients ajoutés");
    }
    DateTime firstUpdate;
    using (var db = NewDb())
    {
        var paul = await db.Patients.SingleAsync(p => p.AccessPatientId == "2");
        firstUpdate = paul.UpdatedUtc;
        Check(paul.IsActive && paul.SearchName == "DURAND PAUL 2", "Patient enregistré et cherchable");
    }
    await Task.Delay(20);
    using (var db = NewDb())
    {
        // Léa change de téléphone, Paul ne change pas, Zoé n'est plus dans Access, Hugo arrive.
        var run = await new EyeMailStore(db).SyncAsync(Source(P("1", "MARTIN", "Léa", "0612345678"), P("2", "DURAND", "Paul"), P("4", "MARTIN", "Hugo")), default);
        Check(run.Added == 1 && run.Updated == 1 && run.Deactivated == 1 && run.Reactivated == 0, "Deuxième import : 1 nouveau, 1 modifié, 1 absent");
    }
    using (var db = NewDb())
    {
        Check((await db.Patients.SingleAsync(p => p.AccessPatientId == "1")).Phone1 == "0612345678", "La modification est recopiée");
        Check((await db.Patients.SingleAsync(p => p.AccessPatientId == "2")).UpdatedUtc == firstUpdate, "Un patient inchangé n'est pas réécrit");
        var zoe = await db.Patients.SingleAsync(p => p.AccessPatientId == "3");
        Check(!zoe.IsActive && zoe.LastName == "PETIT", "Un patient absent d'Access est gardé et marqué absent");
        Check(await db.Patients.CountAsync() == 4 && await db.ImportRuns.CountAsync() == 2, "Rien n'est supprimé ; chaque import est historisé");
    }
    using (var db = NewDb())
    {
        var run = await new EyeMailStore(db).SyncAsync(Source(P("1", "MARTIN", "Léa", "0612345678"), P("2", "DURAND", "Paul"), P("3", "PETIT", "Zoé"), P("4", "MARTIN", "Hugo")), default);
        Check(run.Reactivated == 1 && run.Added == 0, "Un patient revenu dans Access est réactivé");
    }
    using (var db = NewDb())
    {
        try
        {
            await new EyeMailStore(db).SyncAsync(Source(P("1", "A", "B"), P("1", "C", "D")), default);
            Check(false, "Un identifiant en double doit être refusé");
        }
        catch (InvalidDataException ex) { Check(ex.Message.Contains("deux fois"), "Doublon d'identifiant refusé"); }
    }
    using (var db = NewDb())
        Check((await db.Patients.SingleAsync(p => p.AccessPatientId == "1")).LastName == "MARTIN" && await db.ImportRuns.CountAsync() == 3, "Un import refusé ne modifie rien");

    // 3. Import en arrière-plan, comme sur le serveur, avec une source Access simulée.
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDbContext<EyeMailDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(folder, "eyemail.db")}"));
    services.AddScoped<EyeMailStore>();
    services.AddSingleton<IHostApplicationLifetime, TestLifetime>();
    var fake = new FakeSource();
    services.AddSingleton<IEyeMailSource>(fake);
    services.AddSingleton<EyeMailImport>();
    await using var provider = services.BuildServiceProvider();
    var import = provider.GetRequiredService<EyeMailImport>();
    Check(import.Start() && !import.Start(), "Un seul import à la fois");
    var waited = 0;
    while (import.Status.Running && waited++ < 200) await Task.Delay(50);
    Check(!import.Status.Running && !import.Status.Failed && import.Status.Message.Contains("1000 patient(s)"), "Import en arrière-plan terminé : " + import.Status.Message);
    using (var db = NewDb())
        Check(await db.Patients.CountAsync(p => p.IsActive) == 1000, "Les 1000 patients de la source sont actifs");

    fake.Fail = true;
    import.Start();
    waited = 0;
    while (import.Status.Running && waited++ < 200) await Task.Delay(50);
    Check(import.Status.Failed && !import.Status.Message.Contains("DUPONT"), "Une erreur Access n'affiche pas de données patient");
    using (var db = NewDb())
        Check(await db.Patients.CountAsync(p => p.IsActive) == 1000, "Après un échec, la base précédente est conservée");
}
finally
{
    SqliteConnection.ClearAllPools();
    try { Directory.Delete(folder, recursive: true); } catch { }
}
// 4. Messages d'échec : la vraie cause est affichée, sans données patient.
var noProvider = EyeMailImport.Describe(new InvalidOperationException("Aucun fournisseur Access OLE DB compatible. Installez ACE de même architecture que l’application (x64/x86)."));
Check(noProvider.Contains("Aucun fournisseur Access") && noProvider.Contains("bits"), "Pilote Access absent : cause et architecture affichées");
Check(EyeMailImport.Describe(new SqliteException("Fichier verrouillé", 5)).Contains("Access a refusé la lecture : Fichier verrouillé"), "Erreur du pilote affichée");
Check(EyeMailImport.Describe(new UnauthorizedAccessException("C:\\x")).Contains("droit de lire"), "Droits insuffisants expliqués");
var unknown = EyeMailImport.Describe(new NullReferenceException("DUPONT Jean"));
Check(unknown.Contains("NullReferenceException") && !unknown.Contains("DUPONT"), "Erreur inconnue : type seulement, sans son texte");

Console.WriteLine($"{checks} EyeMail checks passed; temporary SQLite database, synthetic patients only.");

sealed class FakeSource : IEyeMailSource
{
    public bool Fail { get; set; }
    public EyeMailSourceData ReadPatients(Action<int>? progress, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("Erreur pilote contenant DUPONT Jean");
        var patients = Enumerable.Range(1, 1000).Select(i => new EyeMailSourcePatient(i.ToString(), "", $"NOM{i}", "Prénom", "", null,
            "", "", "", "", "", "", "", "", "", "")).ToList();
        progress?.Invoke(patients.Count);
        return new EyeMailSourceData("Patients", new Dictionary<string, string>(), patients);
    }
}

sealed class TestLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}
