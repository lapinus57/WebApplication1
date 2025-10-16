using Microsoft.EntityFrameworkCore;
using ChatServeur;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddRazorPages();
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 2 * 1024 * 1024;
});
builder.Services.AddHostedService<ReminderService>();
builder.WebHost.UseUrls("http://0.0.0.0:5000");

var app = builder.Build();
var logger = app.Logger;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
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
    EnsureKnownUsersTable(db, logger);
    CleanupKnownUsers(db, logger);
    if (!db.ServerConfigs.Any())
    {
        db.ServerConfigs.Add(new ServerConfig());
        db.SaveChanges();
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
app.MapHub<ChatHub>("/chatHub");

app.Run();
