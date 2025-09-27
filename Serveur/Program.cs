using Microsoft.EntityFrameworkCore;
using ChatServeur;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(args);

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
    db.Database.EnsureCreated();
    EnsurePatientsTable(db);
    EnsureArchivedColumn(db);
    EnsureIsDeletedColumn(db);
    EnsurePatientLogsTable(db);
    EnsureUserSettingsTable(db);
    EnsureReminderColumn(db);
    EnsureKnownUsersTable(db);
    CleanupKnownUsers(db);
    if (!db.ServerConfigs.Any())
    {
        db.ServerConfigs.Add(new ServerConfig());
        db.SaveChanges();
    }
}

void EnsureArchivedColumn(ChatDbContext db)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (!TableExists(connection, "Patients"))
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
    finally
    {
        connection.Close();
    }
}

void EnsurePatientsTable(ChatDbContext db)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (TableExists(connection, "Patients"))
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
    finally
    {
        connection.Close();
    }
}

bool TableExists(System.Data.Common.DbConnection connection, string tableName)
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

void EnsureIsDeletedColumn(ChatDbContext db)
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
    finally
    {
        connection.Close();
    }
}

void EnsureUserSettingsTable(ChatDbContext db)
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
    finally
    {
        connection.Close();
    }
}

void EnsureReminderColumn(ChatDbContext db)
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
    finally
    {
        connection.Close();
    }
}

void EnsurePatientLogsTable(ChatDbContext db)
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
    finally
    {
        connection.Close();
    }
}

void EnsureKnownUsersTable(ChatDbContext db)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        if (TableExists(connection, "KnownUsers"))
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
    finally
    {
        connection.Close();
    }
}

void CleanupKnownUsers(ChatDbContext db)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM KnownUsers WHERE (Username IS NULL OR Username = '') AND (DisplayName IS NULL OR DisplayName = '')";
        cmd.ExecuteNonQuery();
    }
    finally
    {
        connection.Close();
    }
}

app.MapRazorPages();
app.MapHub<ChatHub>("/chatHub");

app.Run();
