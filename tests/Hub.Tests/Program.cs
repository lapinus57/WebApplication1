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
    builder.Services.AddSignalR(o => o.AddFilter<ChatHubStateFilter>());
    var access = new StationAccess("cle-de-test", "mot-de-passe-test");
    builder.Services.AddSingleton(access);
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
