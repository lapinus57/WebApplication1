using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Client.Helpers;
using Client.Models;
using Client.SmsMail.Models;

namespace Client.SmsMail.Services
{
    public static class AppointmentLoader
    {
        public static Task<List<RdvInfo>> LoadAsync(AppointmentSearchConfig config, MachineConfig machine, DateTime day) => Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(config.DatabasePath)) throw new InvalidOperationException("Renseignez la base Access dans les paramètres RDV.");
            var providers = new[] { machine.AccessOleDbProvider, "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in providers)
            {
                var builder = new OleDbConnectionStringBuilder { Provider = provider, DataSource = config.DatabasePath };
                if (!string.IsNullOrWhiteSpace(machine.AccessWorkgroupPath)) builder["Jet OLEDB:System Database"] = machine.AccessWorkgroupPath;
                if (!string.IsNullOrWhiteSpace(machine.AccessUserName)) builder["User ID"] = machine.AccessUserName;
                if (!string.IsNullOrWhiteSpace(machine.AccessPassword)) builder["Password"] = machine.AccessPassword;
                using var connection = new OleDbConnection(builder.ConnectionString);
                try { connection.Open(); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase)) { continue; }
                using var command = connection.CreateCommand();
                string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
                var table = Quote(config.TableName);
                command.CommandText = $@"SELECT r.DateCreation, r.[Code Patient] AS Code, r.titre, r.nom, r.NomJeuneFille,
                    r.[Prénom], r.{Quote(config.DateColumn)} AS DateRDV, r.{Quote(config.TimeColumn)} AS HeureRDV,
                    r.Remarque, r.Adr AS Adresse, r.[CP Ville] AS Ville, r.Tel AS Tel1, r.[Tel Bureau] AS Tel2, p.Email
                    FROM {table} r INNER JOIN patients p ON r.[Code Patient] = p.[Code Patient]
                    WHERE r.{Quote(config.DateColumn)} >= ? AND r.{Quote(config.DateColumn)} < ?";
                command.Parameters.Add("?", OleDbType.Date).Value = day.Date;
                command.Parameters.Add("?", OleDbType.Date).Value = day.Date.AddDays(1);
                using var reader = command.ExecuteReader();
                var result = new List<RdvInfo>();
                while (reader != null && reader.Read())
                {
                    string Text(string name) => Convert.ToString(reader[name], CultureInfo.GetCultureInfo("fr-FR")) ?? string.Empty;
                    DateTime Date(string name) => reader[name] is DateTime value ? value : DateTime.MinValue;
                    var name = Text("nom");
                    if (string.Equals(name.Trim(), "ABSENCE", StringComparison.OrdinalIgnoreCase) || name.Trim().All(c => c == '/')) continue;
                    var rdv = new RdvInfo
                    {
                        Id = Convert.ToInt32(reader["Code"]), Titre = Text("titre"), Nom = name, Prenom = Text("Prénom"),
                        NomJeuneFille = Text("NomJeuneFille"), DatePriseRDV = Date("DateCreation"), DateRDV = Date("DateRDV"),
                        HeureRDV = day.Date + Date("HeureRDV").TimeOfDay, Remarque = Text("Remarque"),
                        Adresse = Text("Adresse"), Ville = Text("Ville"), Tel1 = Text("Tel1"), Tel2 = Text("Tel2"), Email = Text("Email")
                    };
                    rdv.IsCancelled = Regex.IsMatch(rdv.Remarque, "annul", RegexOptions.IgnoreCase);
                    var printed = Regex.Match(rdv.Remarque, @"imprim[ée]{0,2}\s*[àa]\s*(\d{1,2})\s*h\s*(\d{0,2})", RegexOptions.IgnoreCase);
                    if (printed.Success && int.TryParse(printed.Groups[1].Value, out int hour) && hour < 24)
                    {
                        int.TryParse(printed.Groups[2].Value, out int minute);
                        if (minute < 60) rdv.HeureRDV = day.Date.AddHours(hour).AddMinutes(minute);
                    }
                    result.Add(rdv);
                }
                return result;
            }
            throw new InvalidOperationException("Aucun fournisseur Access OLE DB compatible. Installez ACE de même architecture que le client (x64/x86).");
        });
    }
}
