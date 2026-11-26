// Vérifie le hub SignalR avec un vrai serveur en mémoire (boucle locale) et plusieurs postes simulés.
// Aucune donnée réelle : base SQLite temporaire, supprimée à la fin.
using ChatServeur;
using ChatServeur.Startup;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using System.Net;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("ÉCHEC : " + message);
    checks++;
}

var folder = Path.Combine(Path.GetTempPath(), "EyeChat-Hub-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var timeout = TimeSpan.FromSeconds(10);
try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = folder, WebRootPath = folder });
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
    builder.Services.AddDbContext<ChatDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(folder, "chat.db")};Default Timeout=30"));
    builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 2 * 1024 * 1024; o.AddFilter<ChatHubStateFilter>(); }); // Même limite que Program.cs.
    var access = new StationAccess("cle-de-test", "mot-de-passe-test");
    builder.Services.AddSingleton(access);
    builder.Services.AddSingleton(new EyeChat.SmsMail.SmsMailHistoryStore(Path.Combine(folder, "sms-mail-history.db")));
    var familyStore = new ChatServeur.Families.FamilyStore(Path.Combine(folder, "families.db"));
    builder.Services.AddSingleton(familyStore);
    // Réponses des patients : service externe et envoi de mail simulés (aucun appel réseau réel).
    var relay = new FakeRelay();
    var mailer = new FakeMailer();
    Microsoft.AspNetCore.DataProtection.DataProtectionBuilderExtensions.SetApplicationName(builder.Services.AddDataProtection(), "EyeChat.Tests");
    builder.Services.AddHttpClient("replies").ConfigurePrimaryHttpMessageHandler(() => relay);
    builder.Services.AddSingleton<ChatServeur.Replies.IReplyMailer>(mailer);
    builder.Services.AddScoped<ChatServeur.Replies.ReplySettingsStore>();
    builder.Services.AddScoped<ChatServeur.Replies.ReplyLinkServiceFactory>();
    await using var app = builder.Build();
    using (var scope = app.Services.CreateScope())
        scope.ServiceProvider.GetRequiredService<ChatDbContext>().Database.EnsureCreated();
    // Même contrôle que Program.cs.
    app.Use(async (context, next) =>
    {
        if (StationAccess.IsProtectedPath(context.Request.Path) && !access.IsStationAuthorized(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next();
    });
    app.MapHub<ChatHub>("/chatHub");
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    var connections = new List<HubConnection>();
    async Task<HubConnection> Connect()
    {
        var c = new HubConnectionBuilder()
            .WithUrl(address + "/chatHub", o => o.AccessTokenProvider = () => Task.FromResult<string?>("cle-de-test"))
            .Build();
        await c.StartAsync();
        connections.Add(c);
        return c;
    }
    TaskCompletionSource<T> Expect<T>(HubConnection c, string method)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        c.On<T>(method, value => tcs.TrySetResult(value));
        return tcs;
    }
    async Task<T> Within<T>(Task<T> task, string what)
    {
        var done = await Task.WhenAny(task, Task.Delay(timeout));
        Check(done == task, what);
        return await task;
    }
    async Task<UserInfo?> FindUser(HubConnection c, string username)
        => (await c.InvokeAsync<List<UserInfo>>("GetAllUsers")).FirstOrDefault(u => u.Username == username);

    // 0. Clé de poste : sans elle, ou avec une mauvaise clé, la connexion est refusée.
    foreach (var wrongKey in new string?[] { null, "mauvaise-cle" })
    {
        var intruder = new HubConnectionBuilder()
            .WithUrl(address + "/chatHub", o => { if (wrongKey != null) o.AccessTokenProvider = () => Task.FromResult<string?>(wrongKey); })
            .Build();
        try
        {
            await intruder.StartAsync();
            Check(false, "Un poste sans clé valide ne doit pas se connecter");
        }
        catch (HttpRequestException ex) { Check(ex.StatusCode == HttpStatusCode.Unauthorized, "Refus 401 sans clé valide"); }
        await intruder.DisposeAsync();
    }
    // WebSocket direct (sans négociation) : la clé passe dans access_token.
    var direct = new HubConnectionBuilder()
        .WithUrl(address + "/chatHub", Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets, o =>
        {
            o.SkipNegotiation = true;
            o.AccessTokenProvider = () => Task.FromResult<string?>("cle-de-test");
        })
        .Build();
    await direct.StartAsync();
    Check(direct.State == HubConnectionState.Connected, "La clé est acceptée en WebSocket direct");
    await direct.DisposeAsync();

    // 1. Changement de poste : l'ancien poste accepte, seul le nouveau reste, avec sa salle.
    var pcA = await Connect();
    await pcA.InvokeAsync("RegisterUser", "Marie", "", "Salle 1", "Blue", "PC-A");
    Check((await FindUser(pcA, "Marie"))?.Rooms.SequenceEqual(new[] { "Salle 1" }) == true, "Marie apparaît en Salle 1");

    var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    pcA.On<string, string, string, int>("LoginConflictRequested", (id, _, machine, _) => { if (machine == "PC-B") requested.TrySetResult(id); });
    var forceLogout = Expect<string>(pcA, "ForceLogout");
    var pcB = await Connect();
    var approved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    pcB.On<string, string>("LoginConflictApproved", (id, _) => approved.TrySetResult(id));
    await pcB.InvokeAsync("RegisterUser", "Marie", "", "Salle 3", "Blue", "PC-B");
    var requestId = await Within(requested.Task, "PC-A est prévenu de la demande de PC-B");
    await pcA.InvokeAsync("RespondToLoginConflict", requestId, true);
    await Within(approved.Task, "PC-B reçoit l'accord");
    await pcB.InvokeAsync("CompleteLoginAfterApproval", requestId);
    var reason = await Within(forceLogout.Task, "PC-A est déconnecté");
    Check(reason.Contains("PC-B"), "Le message de déconnexion nomme le nouveau poste");
    var marie = await FindUser(pcB, "Marie");
    Check(marie?.Rooms.SequenceEqual(new[] { "Salle 3" }) == true, "Seule la salle du nouveau poste est affichée");
    Check(marie?.MachineName == "PC-B", "Le compte est rattaché au dernier poste");

    // Après la prise de relais, l'ancien poste ne reçoit plus les messages privés.
    var privateOnA = Expect<int>(pcA, "ReceiveMessage");
    var privateOnB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    pcB.On<int, string, string, string, string, string, DateTime>("ReceiveMessage", (_, _, _, _, content, _, _) => privateOnB.TrySetResult(content));
    var sender = await Connect();
    await sender.InvokeAsync("RegisterUser", "Paul", "", "Accueil", "Red", "PC-ACCUEIL");
    await sender.InvokeAsync("SendMessage", "Paul", "Accueil", "Marie", "Patient prêt", "", DateTime.Now);
    Check(await Within(privateOnB.Task, "Le nouveau poste reçoit le message privé") == "Patient prêt", "Contenu du message privé");
    await Task.Delay(300);
    Check(!privateOnA.Task.IsCompleted, "L'ancien poste ne reçoit plus le message privé");

    // L'ancien poste se déconnecte : Marie reste en ligne sur PC-B.
    await pcA.StopAsync();
    await Task.Delay(300);
    marie = await FindUser(pcB, "Marie");
    Check(marie?.IsOnline == true && marie.Rooms.SequenceEqual(new[] { "Salle 3" }), "La déconnexion de l'ancien poste ne met pas Marie hors ligne");

    // 2. Reconnexion du même poste (coupure réseau) : pas de demande, l'ancienne connexion est remplacée.
    var conflictOnSamePc = false;
    var pcB2 = await Connect();
    pcB2.On<string, string, string, int>("LoginConflictPending", (_, _, _, _) => conflictOnSamePc = true);
    var replaced = Expect<string>(pcB, "ForceLogout");
    await pcB2.InvokeAsync("RegisterUser", "Marie", "", "Salle 3", "Blue", "PC-B");
    await Within(replaced.Task, "L'ancienne connexion du même poste est libérée");
    Check(!conflictOnSamePc, "Aucune demande de conflit pour une reconnexion du même poste");

    // 3. Même nom avec une autre casse : pas de doublon.
    var lower = await Connect();
    await lower.InvokeAsync("RegisterUser", "paul", "", "Accueil", "Red", "PC-ACCUEIL");
    var users = await lower.InvokeAsync<List<UserInfo>>("GetAllUsers");
    Check(users.Count(u => string.Equals(u.Username, "Paul", StringComparison.OrdinalIgnoreCase)) == 1, "« paul » et « Paul » restent un seul compte");

    // 4. Avatars : une page HTML déguisée est refusée avec un message clair.
    try
    {
        await pcB2.InvokeAsync<string>("UploadAvatar", "piege.html", Convert.ToBase64String("<script>alert(1)</script>"u8.ToArray()));
        Check(false, "Un avatar HTML doit être refusé");
    }
    catch (HubException ex) { Check(ex.Message.Contains("PNG"), "Le refus d'avatar explique les formats acceptés"); }
    var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
    var avatarPath = await pcB2.InvokeAsync<string>("UploadAvatar", "photo.jpg", Convert.ToBase64String(png));
    Check(avatarPath.StartsWith("/avatars/") && avatarPath.EndsWith(".png"), "L'extension de l'avatar vient du contenu");

    // 5. Requêtes par date : messages et patients du jour.
    var today = await pcB2.InvokeAsync<List<ChatMessage>>("GetTodayMessages", "Marie");
    Check(today.Any(m => m.Content == "Patient prêt"), "Le message du jour est retrouvé");
    Check((await pcB2.InvokeAsync<List<ChatMessage>>("GetMessagesForDate", "Marie", DateTime.Today.AddDays(-1))).Count == 0, "Rien la veille");
    await pcB2.InvokeAsync("DeclarePatient", new Patient { Id = "p1", LastName = "TEST", FirstName = "Synthétique", HoldTime = DateTime.Now, TimeOrder = DateTime.Now.TimeOfDay });
    await pcB2.InvokeAsync("DeclarePatient", new Patient { Id = "p0", LastName = "HIER", HoldTime = DateTime.Today.AddMinutes(-1), TimeOrder = TimeSpan.Zero });
    var patients = await pcB2.InvokeAsync<List<Patient>>("GetPatients");
    Check(patients.Count == 1 && patients[0].Id == "p1", "Seuls les patients du jour sont renvoyés");
    var logs = await pcB2.InvokeAsync<List<PatientLog>>("GetPatientLogs", "p1");
    Check(logs.Count == 1 && logs[0].Username == "Marie", "Le journal du patient garde l'auteur");

    // 6. Sécurité des actions : expéditeur, mot de passe applicatif, groupes protégés.
    // (« lower » est la connexion active de Paul : celle de l'étape 1 a été remplacée à l'étape 3.)
    var spoofed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    pcB2.On<int, string, string, string, string, string, DateTime>("ReceiveMessage", (_, from, _, _, content, _, _) => { if (content == "usurpation") spoofed.TrySetResult(from); });
    await lower.InvokeAsync("SendMessage", "Marie", "Accueil", "Marie", "usurpation", "", DateTime.Now);
    Check(await Within(spoofed.Task, "Le message usurpé arrive") == "Paul", "L'expéditeur est imposé par le serveur");
    var anonymous = await Connect();
    await anonymous.InvokeAsync("SendMessage", "Marie", "Accueil", "A Tous", "anonyme", "", DateTime.Now);
    Check(!(await pcB2.InvokeAsync<List<ChatMessage>>("GetTodayMessages", "Marie")).Any(m => m.Content == "anonyme"), "Un poste non enregistré ne peut pas écrire");
    Check(await pcB2.InvokeAsync<bool>("VerifyApplicationPassword", "mot-de-passe-test"), "Le mot de passe applicatif configuré est accepté");
    Check(!await pcB2.InvokeAsync<bool>("VerifyApplicationPassword", "901027"), "L'ancien mot de passe ne marche plus une fois configuré");
    Check(!(await pcB2.InvokeAsync<string>("CreateGroup", "Bloc", false, "secret-bloc", "mot-de-passe-test")).Contains("incorrect"), "Création d’un groupe protégé avec le mot de passe configuré");
    try
    {
        await lower.InvokeAsync("ChangeGroupPassword", "Bloc", "pirate");
        Check(false, "Un non-membre ne doit pas changer le mot de passe du groupe");
    }
    catch (HubException ex) { Check(ex.Message.Contains("membres"), "Refus expliqué pour le mot de passe de groupe"); }
    Check(await lower.InvokeAsync<string>("JoinProtectedGroup", "Bloc", "pirate") == "wrong_password", "Le mot de passe du groupe n'a pas changé");
    await pcB2.InvokeAsync("ChangeGroupPassword", "Bloc", "nouveau-secret");
    Check(await lower.InvokeAsync<string>("JoinProtectedGroup", "Bloc", "nouveau-secret") == "joined", "Un membre peut changer le mot de passe");

    // 7. Connexions simultanées : 30 postes en même temps, aucun utilisateur perdu.
    var crowd = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Connect()));
    await Task.WhenAll(crowd.Select((c, i) => c.InvokeAsync("RegisterUser", $"Poste{i:00}", "", $"Salle {i % 5}", "Green", $"PC-{i:00}")));
    users = await crowd[0].InvokeAsync<List<UserInfo>>("GetAllUsers");
    Check(Enumerable.Range(0, 30).All(i => users.Any(u => u.Username == $"Poste{i:00}" && u.IsOnline)), "Les 30 postes simultanés sont tous en ligne");
    await Task.WhenAll(crowd.Select(c => c.StopAsync()));
    await Task.Delay(500);
    users = await pcB2.InvokeAsync<List<UserInfo>>("GetAllUsers");
    Check(Enumerable.Range(0, 30).All(i => users.Any(u => u.Username == $"Poste{i:00}" && !u.IsOnline)), "Les 30 postes déconnectés passent hors ligne");

    // 8. Historique SMS/mail commun : un envoi réservé sur un poste est visible et bloqué sur un autre.
    var posteA = await Connect();
    var posteB = await Connect();
    var reservation = new EyeChat.SmsMail.HistoryReservation
    {
        Key = "sms-test-1", ReservationKeys = new() { "famille-80|2026-10-23", "tel-+33612345678|2026-10-23" },
        Channel = "SMS", Contact = "+33612345678", Body = "Rappel de rendez-vous", ProviderService = "sms-test"
    };
    Check(await posteA.InvokeAsync<bool>("SmsMailReserve", reservation), "Le premier poste réserve l'envoi");
    Check(!await posteB.InvokeAsync<bool>("SmsMailReserve", reservation), "Un autre poste ne peut pas renvoyer le même rappel");
    var sameFamily = new EyeChat.SmsMail.HistoryReservation { Key = "sms-test-2", ReservationKeys = new() { "famille-80|2026-10-23" }, Channel = "SMS", Contact = "+33698765432", Body = "x", ProviderService = "sms-test" };
    Check(!await posteB.InvokeAsync<bool>("SmsMailReserve", sameFamily), "Même famille, autre numéro : bloqué depuis un autre poste");
    await posteA.InvokeAsync("SmsMailUpdate", "sms-test-1", "Accepté par OVH", (long?)123);
    var shared = await posteB.InvokeAsync<List<EyeChat.SmsMail.HistoryRecord>>("SmsMailLoad");
    Check(shared.Single() is { Key: "sms-test-1", Status: "Accepté par OVH", ProviderId: 123 }, "Le second poste voit l'envoi et son statut");
    Check((await posteB.InvokeAsync<List<string>>("SmsMailReservedKeys")).Contains("famille-80|2026-10-23"), "Les réservations sont partagées");
    Check((await posteA.InvokeAsync<List<EyeChat.SmsMail.FamilyMembershipDto>>("SmsMailFamilies", new List<int> { 1, 2 })).Count == 0, "Aucun calcul de familles : liste vide");
    var familyScan = new EyeChat.Families.FamilyScanner { Today = new DateTime(2026, 11, 24) };
    foreach (var day in new[] { new DateTime(2025, 3, 2), new DateTime(2026, 3, 2) })
        foreach (var (id, first) in new[] { (601, "Ana"), (602, "Eli") })
            familyScan.Add(new EyeChat.Families.ScanRow(id, day, day.AddHours(9), null, null, "", "BRUN", "", first, "4 rue B", "Metz", new[] { "0601000601" }, Array.Empty<string>(), false));
    await familyStore.SaveResultAsync(familyScan.Finish(), "");
    var known = await posteA.InvokeAsync<List<EyeChat.SmsMail.FamilyMembershipDto>>("SmsMailFamilies", new List<int> { 601, 602, 603 });
    Check(known.Count == 2 && known.All(k => k.FamilyNumber == 1 && !k.ToReview && k.Why.Contains("Ana BRUN et Eli BRUN")), "Familles du serveur reçues par le poste, avec le pourquoi");
    Check(await posteA.InvokeAsync<int>("SmsMailFamilyAnswer", new List<int> { 602, 601 }, false, "ACCUEIL") == 1
        && (await posteB.InvokeAsync<List<EyeChat.SmsMail.FamilyMembershipDto>>("SmsMailFamilies", new List<int> { 601, 602 })).Select(k => k.SubGroup).Distinct().Count() == 2,
        "Réponse d'un poste gardée par le serveur et vue par les autres postes");
    var mail = new EyeChat.SmsMail.HistoryRecord { Key = "mail-ancien", Channel = "Mail", Contact = "Lea@Example.fr", Body = "<p>" + new string('x', 400_000) + "</p>", Status = "Accepté", CreatedAtUtc = "2026-10-20T08:00:00.0000000+00:00", ProviderService = "smtp.test" };
    Check(await posteA.InvokeAsync<int>("SmsMailImport", new List<EyeChat.SmsMail.HistoryRecord> { mail, mail }, new List<EyeChat.SmsMail.HistoryReservationKey>()) == 1, "Import : une seule ligne ajoutée pour une clé en double");
    Check(await posteB.InvokeAsync<DateTimeOffset?>("SmsMailEarliestMail", "smtp.test") == new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), "Date du premier mail");
    Check(await posteB.InvokeAsync<int>("SmsMailApplyReport", "mail-ancien", "lea@example.fr", "Remis", "smtp.test") == 1, "Retour de remise appliqué depuis un autre poste");

    // 9. Message individuel avec lien « Confirmer / Annuler ».
    var poste = await Connect();
    var tomorrow = DateTime.Today.AddDays(1).AddHours(9).AddMinutes(30);
    var linkRequest = new EyeChat.SmsMail.ReplyLinkRequest
    {
        MessageKey = "individuel-1", Channel = "SMS", Contact = "+33612345678", People = "Léa MARTIN", AppointmentStart = tomorrow,
        Cabinet = "Cabinet Test", CabinetAddress = "1 rue des Lilas", ReplyEmail = "secretariat@example.fr"
    };
    Check(await poste.InvokeAsync<string>("SmsMailReplyLinkBase") == "", "Service de réponse non configuré : aucune adresse");
    try { await poste.InvokeAsync<string>("SmsMailCreateReplyLink", linkRequest); Check(false, "Lien refusé sans configuration"); }
    catch (HubException ex) { Check(ex.Message.Contains("Réponses des patients"), "Refus expliqué sans configuration"); }

    using (var scope = app.Services.CreateScope())
        await scope.ServiceProvider.GetRequiredService<ChatServeur.Replies.ReplySettingsStore>().SaveAsync(new ChatServeur.Replies.ReplySettings
        {
            RelayUrl = "https://rdv.test.workers.dev/", SmtpHost = "smtp.test", FromAddress = "eyechat@example.fr"
        }, FakeRelay.Key, "mot-de-passe-smtp", CancellationToken.None);
    Check(await poste.InvokeAsync<string>("SmsMailReplyLinkBase") == "https://rdv.test.workers.dev", "Adresse du service configurée");

    var url = await poste.InvokeAsync<string>("SmsMailCreateReplyLink", linkRequest);
    var token = url["https://rdv.test.workers.dev/".Length..];
    Check(url.StartsWith("https://rdv.test.workers.dev/") && token.Length == 8 && token.All(char.IsLetterOrDigit), "Lien court créé : adresse + jeton de 8 caractères");
    Check(relay.LinkBodies.Count == 1 && relay.LinkBodies[0].Contains(token) && relay.LinkBodies[0].Contains("Cabinet Test") && relay.LinkBodies[0].Contains("9h30"),
        "Le service externe reçoit le jeton, le cabinet et l'heure");
    Check(!relay.LinkBodies[0].Contains("MARTIN") && !relay.LinkBodies[0].Contains("612345678") && !relay.LinkBodies[0].Contains("secretariat"),
        "Le service externe ne reçoit ni nom, ni téléphone, ni adresse mail");
    relay.FailWith = "Base D1 non liée : Worker → Paramètres → Liaisons";
    try { await poste.InvokeAsync<string>("SmsMailCreateReplyLink", linkRequest); Check(false, "Erreur du service attendue"); }
    catch (HubException ex) { Check(ex.Message.Contains("(500)") && ex.Message.Contains("Base D1 non liée"), "La cause donnée par le service s'affiche sur le poste"); }
    relay.FailWith = null;
    var past = new EyeChat.SmsMail.ReplyLinkRequest { MessageKey = "x", Channel = "SMS", Contact = "+33600000000", AppointmentStart = DateTime.Now.AddDays(-1), ReplyEmail = "a@example.fr" };
    try { await poste.InvokeAsync<string>("SmsMailCreateReplyLink", past); Check(false, "RDV passé refusé"); }
    catch (HubException ex) { Check(ex.Message.Contains("passé"), "RDV passé : pas de lien"); }

    Check(await poste.InvokeAsync<bool>("SmsMailReserve", new EyeChat.SmsMail.HistoryReservation { Key = "sans-lien", Channel = "SMS", Contact = "+33611111111", Body = "x", ProviderService = "sms-test" }), "Message sans lien");
    Check(await poste.InvokeAsync<bool>("SmsMailReserve", new EyeChat.SmsMail.HistoryReservation { Key = "individuel-1", Channel = "SMS", Contact = "+33612345678", Body = "RDV " + url, ProviderService = "sms-test" }),
        "Le message individuel est inscrit dans l'historique");

    var beforeReply = await poste.InvokeAsync<List<EyeChat.SmsMail.HistoryRecord>>("SmsMailLoad");
    Check(beforeReply.Single(h => h.Key == "individuel-1") is { ReplyChoice: "Attente", ReplyAppointment.Length: > 0 } && beforeReply.Single(h => h.Key == "sans-lien").ReplyChoice == "",
        "Avant réponse : « en attente » pour le message avec lien, rien pour les autres");

    async Task<int> Poll()
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ChatServeur.Replies.ReplyLinkServiceFactory>().Create().PollAsync();
    }
    relay.Answer(token, "Annule");
    Check(await Poll() == 1, "Réponse relevée");
    var history = await poste.InvokeAsync<List<EyeChat.SmsMail.HistoryRecord>>("SmsMailLoad");
    Check(history.Single(h => h.Key == "individuel-1") is { ReplyChoice: "Annule", Status: "Envoi commencé — résultat à vérifier" } && history.Single(h => h.Key == "individuel-1").ReplyAtUtc.Length > 0,
        "L'annulation s'affiche à part dans l'historique commun, sans effacer le statut d'envoi");
    Check(mailer.Sent.Count == 1 && mailer.Sent[0].To == "secretariat@example.fr" && mailer.Sent[0].Subject.Contains("ANNULATION") && mailer.Sent[0].Subject.Contains("Léa MARTIN"),
        "Mail d'annulation envoyé à l'adresse choisie, avec le nom du patient");
    Check(await Poll() == 0 && mailer.Sent.Count == 1, "Pas de second mail pour la même réponse");

    mailer.Fail = true;
    relay.Answer(token, "Confirme");
    await Poll();
    Check(mailer.Sent.Count == 1, "Mail en échec : rien d'envoyé");
    mailer.Fail = false;
    await Poll();
    Check(mailer.Sent.Count == 2 && mailer.Sent[1].Subject.Contains("Confirmation"), "Mail retenté au relevé suivant (patient qui change d'avis)");
    await poste.InvokeAsync("SmsMailUpdate", "individuel-1", "Reçu — 22/11 18:05", (long?)null); // « Statuts SMS » d'OVH
    Check((await poste.InvokeAsync<List<EyeChat.SmsMail.HistoryRecord>>("SmsMailLoad")).Single(h => h.Key == "individuel-1") is { ReplyChoice: "Confirme", Status: "Reçu — 22/11 18:05" },
        "La dernière réponse reste visible après l'actualisation des statuts OVH");
    relay.Answer("Inconnu1", "Annule");
    Check(await Poll() == 0 && mailer.Sent.Count == 2, "Réponse pour un lien inconnu ignorée");

    foreach (var c in connections)
        await c.DisposeAsync();
    await app.StopAsync();
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(folder, recursive: true); } catch { }
}
Console.WriteLine($"{checks} hub checks passed; local loopback server, synthetic data only.");

// Service de réponse simulé : mêmes adresses et même clé que le Worker Cloudflare (relay/worker.js).
sealed class FakeRelay : HttpMessageHandler
{
    public const string Key = "cle-du-service-de-reponse-0123456789";
    public List<string> LinkBodies { get; } = new();
    public string? FailWith { get; set; }
    private readonly List<(long Id, string Token, string Choice, long At)> _responses = new();
    public void Answer(string token, string choice) => _responses.Add((_responses.Count + 1, token, choice, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.Parameter != Key) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var path = request.RequestUri!.AbsolutePath;
        if (FailWith != null)
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { error = FailWith })) };
        if (path == "/api/links" && request.Method == HttpMethod.Post)
        {
            LinkBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Json("{\"created\":1}");
        }
        if (path == "/api/responses")
        {
            var after = long.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["after"] ?? "0");
            var list = _responses.Where(r => r.Id > after).Select(r => new { id = r.Id, token = r.Token, choice = r.Choice, answeredAt = r.At });
            return Json(System.Text.Json.JsonSerializer.Serialize(new { responses = list }));
        }
        if (path == "/api/ping") return Json("{\"ok\":true}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

sealed class FakeMailer : ChatServeur.Replies.IReplyMailer
{
    public List<(string To, string Subject, string Html)> Sent { get; } = new();
    public bool Fail { get; set; }
    public Task SendAsync(ChatServeur.Replies.ReplyConfiguration config, string to, string subject, string htmlBody, CancellationToken ct)
    {
        if (Fail) throw new System.Net.Mail.SmtpException("serveur mail indisponible");
        Sent.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
