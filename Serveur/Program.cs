using Microsoft.EntityFrameworkCore;
using ChatServeur;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

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
    EnsureArchivedColumn(db);
    EnsureIsDeletedColumn(db);
    EnsurePatientLogsTable(db);
    EnsureUserSettingsTable(db);
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
