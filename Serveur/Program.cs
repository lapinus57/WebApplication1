using ChatServeur.Startup;
using Microsoft.EntityFrameworkCore;
using ChatServeur;
using ChatServeur.Fundus;
using ChatServeur.Registry;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Extensions;

try
{
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var administrationUsername = builder.Configuration["Administration:Username"];
var administrationPassword = builder.Configuration["Administration:Password"];
var certificatePath = builder.Configuration["Administration:HttpsCertificatePath"];
var certificatePassword = builder.Configuration["Administration:HttpsCertificatePassword"];
var useDevelopmentHttp = builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(certificatePath);
if (string.IsNullOrWhiteSpace(administrationUsername) ||
    string.IsNullOrWhiteSpace(administrationPassword) ||
    administrationPassword.Length < 12 ||
    string.Equals(administrationPassword, "change-me", StringComparison.OrdinalIgnoreCase))
{
    throw new StartupConfigurationException(
        "Configurez Administration:Username et un mot de passe Administration:Password d’au moins 12 caractères avant de démarrer le serveur.");
}
if (string.IsNullOrWhiteSpace(certificatePath) && !useDevelopmentHttp)
    throw new StartupConfigurationException("Configurez Administration:HttpsCertificatePath avec un certificat HTTPS PFX valide.");

if (!useDevelopmentHttp)
{
    certificatePath = Path.IsPathRooted(certificatePath!)
        ? certificatePath
        : Path.Combine(AppContext.BaseDirectory, certificatePath!);
    if (!File.Exists(certificatePath))
        throw new StartupConfigurationException($"Le certificat HTTPS d’administration est introuvable : {certificatePath}");
}

using var administrationCertificate = useDevelopmentHttp ? null
    : AdministrationCertificate.Load(certificatePath!, certificatePassword);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
    if (!useDevelopmentHttp)
        options.ListenAnyIP(5443, listenOptions => listenOptions.UseHttps(administrationCertificate!));
});

if (OperatingSystem.IsWindows() && Environment.UserInteractive && !WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddHostedService<TrayIconHostedService>();
}

if (OperatingSystem.IsWindows())
{
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "ChatServeur";
    });
}

var dbFolder = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dbFolder);
var dbPath = Path.Combine(dbFolder, "chat.db");

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.Cookie.Name = "EyeChat.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = useDevelopmentHttp
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
});
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 2 * 1024 * 1024;
});
builder.Services.AddHostedService<ReminderService>();
builder.Services.AddScoped<FoAccessReader>();
builder.Services.AddScoped<IFoAccessReader>(p => p.GetRequiredService<FoAccessReader>());
builder.Services.AddScoped<IRegistryAccessReader>(p => p.GetRequiredService<FoAccessReader>());
builder.Services.AddScoped<PatientRegistryStore>();
builder.Services.AddSingleton<PatientRegistryImport>();
builder.Services.AddScoped<FoStore>();

var app = builder.Build();
var logger = app.Logger;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    var isAdministrationPage = context.Request.Path == "/" ||
        context.Request.Path.StartsWithSegments("/Index") ||
        context.Request.Path.StartsWithSegments("/Mobile") ||
        context.Request.Path.StartsWithSegments("/Fundus") ||
        context.Request.Path.StartsWithSegments("/PatientRegistry") ||
        context.Request.Path.StartsWithSegments("/Login") ||
        context.Request.Path.StartsWithSegments("/Logout");
    if (!useDevelopmentHttp && !context.Request.IsHttps && isAdministrationPage)
    {
        var httpsHost = new HostString(context.Request.Host.Host, 5443);
        var destination = UriHelper.BuildAbsolute("https", httpsHost, context.Request.PathBase, context.Request.Path, context.Request.QueryString);
        context.Response.Redirect(destination, permanent: false);
        return;
    }
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER00: Failed to ensure SQLite database is created.");
        throw;
    }

    FoSchema.Ensure(db);
    PatientRegistrySchema.Ensure(db);
    EnsurePatientsTable(db, logger);
    EnsureColumn(db, logger, "Patients", "IsArchived", "INTEGER NOT NULL DEFAULT 0", "SER02");
    EnsureColumn(db, logger, "Messages", "IsDeleted", "INTEGER NOT NULL DEFAULT 0", "SER04");
    EnsurePatientLogsTable(db, logger);
    EnsureUserSettingsTable(db, logger);
    EnsureColumn(db, logger, "ServerConfigs", "ReminderJson", "TEXT NOT NULL DEFAULT ''", "SER07");
    EnsureColumn(db, logger, "ServerConfigs", "AppointmentSearchJson", "TEXT NOT NULL DEFAULT ''", "SER15");
    EnsureColumn(db, logger, "ServerConfigs", "DeploymentConfigurationJson", "TEXT NOT NULL DEFAULT ''", "SER17");
    EnsureColumn(db, logger, "ServerConfigs", "IdleRoomTimeoutSeconds", "INTEGER NOT NULL DEFAULT 180", "SER18");
    EnsureKnownUsersTable(db, logger);
    EnsureColumn(db, logger, "SecureGroups", "IsPublic", "INTEGER NOT NULL DEFAULT 0", "SER15");
    CleanupKnownUsers(db, logger);
    if (!db.ServerConfigs.Any())
    {
        db.ServerConfigs.Add(new ServerConfig());
        db.SaveChanges();
    }
}

// Ajoute une colonne manquante à une table existante (mise à niveau des anciennes bases).
// Les noms viennent uniquement des appels ci-dessus, jamais d’une saisie utilisateur.
void EnsureColumn(ChatDbContext db, ILogger logger, string table, string column, string definition, string errorCode)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (!TableExists(connection, table, logger))
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{table}')";
        var exists = false;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                exists |= string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase);
        }
        if (!exists)
        {
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "{ErrorCode}: Failed to ensure {Table} contains {Column} column.", errorCode, table, column);
        throw;
    }
    finally
    {
        connection.Close();
    }
}

void EnsurePatientsTable(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (TableExists(connection, "Patients", logger))
        {
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"CREATE TABLE Patients (
            Id TEXT NOT NULL PRIMARY KEY,
            Colors TEXT NOT NULL DEFAULT '',
            Title TEXT NOT NULL DEFAULT '',
            LastName TEXT NOT NULL DEFAULT '',
            FirstName TEXT NOT NULL DEFAULT '',
            Exams TEXT NOT NULL DEFAULT '',
            Eye TEXT NOT NULL DEFAULT '',
            Annotation TEXT NOT NULL DEFAULT '',
            Position TEXT NOT NULL DEFAULT '',
            HoldTime TEXT NOT NULL,
            PickUpTime TEXT NULL,
            TimeOrder TEXT NOT NULL,
            Examinator TEXT NOT NULL DEFAULT '',
            OperatorName TEXT NOT NULL DEFAULT '',
            IsTaken INTEGER NOT NULL DEFAULT 0,
            IsArchived INTEGER NOT NULL DEFAULT 0
        )";
        cmd.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER01: Failed to create Patients table.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}

bool TableExists(System.Data.Common.DbConnection connection, string tableName, ILogger logger)
{
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        cmd.Parameters.Add(parameter);
        var result = cmd.ExecuteScalar();
        return result != null;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER03: Failed to check existence of table {TableName}.", tableName);
        throw;
    }
}


void EnsureUserSettingsTable(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='UserSettings'";
        var result = cmd.ExecuteScalar();
        if (result == null)
        {
            cmd.CommandText = "CREATE TABLE UserSettings (Id INTEGER PRIMARY KEY AUTOINCREMENT, Username TEXT NOT NULL UNIQUE, SettingsJson TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER06: Failed to ensure UserSettings table exists.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}



void EnsurePatientLogsTable(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='PatientLogs'";
        var result = cmd.ExecuteScalar();
        if (result == null)
        {
            cmd.CommandText = "CREATE TABLE PatientLogs (Id INTEGER PRIMARY KEY AUTOINCREMENT, PatientId TEXT NOT NULL, Username TEXT NOT NULL, Action TEXT NOT NULL, Details TEXT NOT NULL, Timestamp TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER05: Failed to ensure PatientLogs table exists.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}

void EnsureKnownUsersTable(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (TableExists(connection, "KnownUsers", logger))
        {
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"CREATE TABLE KnownUsers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ConnectionId TEXT NOT NULL DEFAULT '',
            Username TEXT NOT NULL DEFAULT '',
            Avatar TEXT NOT NULL DEFAULT '',
            Room TEXT NOT NULL DEFAULT '',
            DisplayName TEXT NOT NULL DEFAULT '',
            ColorUserName TEXT NOT NULL DEFAULT '',
            IsOnline INTEGER NOT NULL DEFAULT 0,
            Note TEXT NOT NULL DEFAULT ''
        )";
        cmd.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER08: Failed to ensure KnownUsers table exists.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}

void CleanupKnownUsers(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM KnownUsers WHERE (Username IS NULL OR Username = '') AND (DisplayName IS NULL OR DisplayName = '')";
        cmd.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER09: Failed to cleanup KnownUsers table.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}

app.MapRazorPages();
app.MapGet("/api/discovery", (HttpResponse response) =>
{
    response.Headers.Append("X-EyeChat-Server", "1");
    return Results.Ok(new { service = "EyeChat", version = 1 });
});
app.MapGet("/api/configuration", async (ChatDbContext db) =>
{
    var config = await db.ServerConfigs.AsNoTracking().SingleOrDefaultAsync();
    if (string.IsNullOrWhiteSpace(config?.DeploymentConfigurationJson))
        return Results.NoContent();

    return Results.Content(config.DeploymentConfigurationJson, "application/json");
});
app.MapHub<ChatHub>("/chatHub");

app.Run();


}
catch (Exception exception)
{
    StartupFailure.Report(exception);
    Environment.ExitCode = 1;
}
