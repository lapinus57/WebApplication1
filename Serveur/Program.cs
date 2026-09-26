using Microsoft.EntityFrameworkCore;
using ChatServeur;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Extensions;

var builder = WebApplication.CreateBuilder(args);

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
    throw new InvalidOperationException(
        "Configurez Administration:Username et un mot de passe Administration:Password d’au moins 12 caractères avant de démarrer le serveur.");
}
if (string.IsNullOrWhiteSpace(certificatePath) && !useDevelopmentHttp)
    throw new InvalidOperationException("Configurez Administration:HttpsCertificatePath avec un certificat HTTPS PFX valide.");

if (!useDevelopmentHttp)
{
    certificatePath = Path.IsPathRooted(certificatePath!)
        ? certificatePath
        : Path.Combine(AppContext.BaseDirectory, certificatePath!);
    if (!File.Exists(certificatePath))
        throw new InvalidOperationException($"Le certificat HTTPS d’administration est introuvable : {certificatePath}");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
    if (!useDevelopmentHttp)
        options.ListenAnyIP(5443, listenOptions => listenOptions.UseHttps(certificatePath!, certificatePassword));
});

if (OperatingSystem.IsWindows())
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

    EnsurePatientsTable(db, logger);
    EnsureArchivedColumn(db, logger);
    EnsureIsDeletedColumn(db, logger);
    EnsurePatientLogsTable(db, logger);
    EnsureUserSettingsTable(db, logger);
    EnsureReminderColumn(db, logger);
    EnsureAppointmentSearchColumn(db, logger);
    EnsureDeploymentConfigurationColumn(db, logger);
    EnsureKnownUsersTable(db, logger);
    EnsureSecureGroupTypeColumn(db, logger);
    CleanupKnownUsers(db, logger);
    if (!db.ServerConfigs.Any())
    {
        db.ServerConfigs.Add(new ServerConfig());
        db.SaveChanges();
    }
}

void EnsureDeploymentConfigurationColumn(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('ServerConfigs')";
        using var reader = cmd.ExecuteReader();
        var exists = false;
        while (reader.Read())
            exists |= string.Equals(reader.GetString(1), "DeploymentConfigurationJson", StringComparison.OrdinalIgnoreCase);
        reader.Close();
        if (!exists)
        {
            cmd.CommandText = "ALTER TABLE ServerConfigs ADD COLUMN DeploymentConfigurationJson TEXT NOT NULL DEFAULT ''";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER17: Failed to ensure deployment configuration column exists.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}

void EnsureSecureGroupTypeColumn(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (!TableExists(connection, "SecureGroups", logger))
            return;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('SecureGroups')";
        using var reader = cmd.ExecuteReader();
        var exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "IsPublic", StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();
        if (!exists)
        {
            cmd.CommandText = "ALTER TABLE SecureGroups ADD COLUMN IsPublic INTEGER NOT NULL DEFAULT 0";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER15: Failed to ensure SecureGroups contains IsPublic column.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}

void EnsureArchivedColumn(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (!TableExists(connection, "Patients", logger))
        {
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('Patients')";
        using var reader = cmd.ExecuteReader();
        bool exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "IsArchived", StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();
        if (!exists)
        {
            cmd.CommandText = "ALTER TABLE Patients ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER02: Failed to ensure Patients table contains IsArchived column.");
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

void EnsureIsDeletedColumn(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('Messages')";
        using var reader = cmd.ExecuteReader();
        bool exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "IsDeleted", StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();
        if (!exists)
        {
            cmd.CommandText = "ALTER TABLE Messages ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER04: Failed to ensure Messages table contains IsDeleted column.");
        throw;
    }
    finally
    {
        connection.Close();
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

void EnsureReminderColumn(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('ServerConfigs')";
        using var reader = cmd.ExecuteReader();
        bool exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "ReminderJson", StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();
        if (!exists)
        {
            cmd.CommandText = "ALTER TABLE ServerConfigs ADD COLUMN ReminderJson TEXT NOT NULL DEFAULT ''";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER07: Failed to ensure ServerConfigs table contains ReminderJson column.");
        throw;
    }
    finally
    {
        connection.Close();
    }
}

void EnsureAppointmentSearchColumn(ChatDbContext db, ILogger logger)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('ServerConfigs')";
        using var reader = cmd.ExecuteReader();
        bool exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "AppointmentSearchJson", StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();
        if (!exists)
        {
            cmd.CommandText = "ALTER TABLE ServerConfigs ADD COLUMN AppointmentSearchJson TEXT NOT NULL DEFAULT ''";
            cmd.ExecuteNonQuery();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SER15: Failed to ensure ServerConfigs table contains AppointmentSearchJson column.");
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
