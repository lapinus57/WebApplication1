using System;
using System.Data.OleDb;
using System.Linq;

namespace EyeChat.Data;

public sealed record AccessReadOptions(string DatabasePath, string Provider, string WorkgroupPath, string UserName, string Password);

public static class ReadOnlyAccess
{
    public static OleDbConnection Open(AccessReadOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
            throw new InvalidOperationException("Renseignez le chemin de la base Access dans les paramètres RDV.");
        foreach (var provider in new[] { options.Provider, "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var builder = new OleDbConnectionStringBuilder { Provider = provider, DataSource = options.DatabasePath };
            builder["Mode"] = "Read";
            builder["Persist Security Info"] = false;
            if (!string.IsNullOrWhiteSpace(options.WorkgroupPath)) builder["Jet OLEDB:System Database"] = options.WorkgroupPath;
            if (!string.IsNullOrWhiteSpace(options.UserName)) builder["User ID"] = options.UserName;
            if (!string.IsNullOrWhiteSpace(options.Password)) builder["Password"] = options.Password;
            var connection = new OleDbConnection(builder.ConnectionString);
            try { connection.Open(); return connection; }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase)) { connection.Dispose(); }
            catch { connection.Dispose(); throw; }
        }
        throw new InvalidOperationException("Aucun fournisseur Access OLE DB compatible. Installez ACE de même architecture que l’application (x64/x86).");
    }
}
