using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Client.Helpers;
using Client.SmsMail.Models;
using static Client.SmsMail.Services.EyeEmailRecordMapper;

namespace Client.SmsMail.Services
{
    // Reads both EyeEmail's Person/ContactChannel/FamilyMembership schema and
    // its legacy Patients(CodePatient, NumeroFamille) schema. Never writes.
    public static class EyeEmailFamilyDatabase
    {
        public static Task<int> ApplyAsync(string path, MachineConfig machine, IReadOnlyList<RdvInfo> appointments, DateTime day) => Task.Run(() =>
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Base familles EyeEmail introuvable.", path);
            foreach (var provider in new[] { machine.AccessOleDbProvider, "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" }.Distinct())
            {
                var builder = new OleDbConnectionStringBuilder { Provider = provider, DataSource = path };
                builder["Mode"] = "Read";
                if (!string.IsNullOrWhiteSpace(machine.AccessWorkgroupPath)) builder["Jet OLEDB:System Database"] = machine.AccessWorkgroupPath;
                if (!string.IsNullOrWhiteSpace(machine.AccessUserName)) builder["User ID"] = machine.AccessUserName;
                if (!string.IsNullOrWhiteSpace(machine.AccessPassword)) builder["Password"] = machine.AccessPassword;
                using var connection = new OleDbConnection(builder.ConnectionString);
                try { connection.Open(); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase)) { continue; }

                var tableNames = connection.GetSchema("Tables").Rows.Cast<DataRow>().Select(row => Convert.ToString(row["TABLE_NAME"]) ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var membership = new Dictionary<int, int>();
                bool hasMembership = Read("FamilyMembership", record => AddMembership(record));
                if (!hasMembership) hasMembership = Read("Patients", record => AddMembership(record));
                if (!hasMembership) throw new InvalidDataException("Aucune table FamilyMembership ou Patients dans la base EyeEmail.");
                var details = appointments.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => new RdvInfo { Id = g.Key });
                Read("Person", record =>
                {
                    var fields = BuildOrdinalLookup(record);
                    int id = GetInt(record, fields, "PersonId", "CodePatient", "Code Patient", "Code");
                    if (!details.TryGetValue(id, out var person)) return;
                    person.Nom = GetString(record, fields, "Nom");
                    person.Prenom = GetString(record, fields, "Prenoms", "Prénoms", "Prenom", "Prénom");
                });
                Read("Patients", record =>
                {
                    var fields = BuildOrdinalLookup(record);
                    int id = GetInt(record, fields, "CodePatient", "Code Patient", "PersonId", "Code", "Id");
                    if (!details.TryGetValue(id, out var person)) return;
                    person.Nom = GetString(record, fields, "Nom"); person.Prenom = GetString(record, fields, "Prenom", "Prénom");
                    AddPhone(person, GetString(record, fields, "Tel 1", "Téléphone 1", "Telephone", "Tel", "Tel1"));
                    AddPhone(person, GetString(record, fields, "Tel 2", "Téléphone 2", "Portable", "Mobile", "Tel2"));
                    AssignEmails(person, GetString(record, fields, "Email", "E-mail", "Mail"));
                    AssignEmails(person, GetString(record, fields, "Email 2", "E-mail 2", "EmailCopie"));
                });
                var contactTypes = new Dictionary<int, string>();
                Read("RefContactType", record =>
                {
                    var fields = BuildOrdinalLookup(record);
                    contactTypes[GetInt(record, fields, "ContactTypeId", "TypeId", "Id")] = GetString(record, fields, "Code", "Type", "Libelle", "Libellé");
                });
                Read("ContactChannel", record =>
                {
                    var fields = BuildOrdinalLookup(record);
                    if (!IsCurrent(record, fields, day)) return;
                    int id = GetInt(record, fields, "PersonId", "CodePatient", "Code Patient", "PatientId", "PersonneId");
                    if (!details.TryGetValue(id, out var person)) return;
                    var kind = GetString(record, fields, "Code", "ContactType", "Type", "Channel", "Canal");
                    if (string.IsNullOrWhiteSpace(kind) || int.TryParse(kind, out _))
                        contactTypes.TryGetValue(GetInt(record, fields, "ContactTypeId", "TypeId", "ContactType"), out kind);
                    var value = GetString(record, fields, "ValeurAffichee", "Valeur Affichee", "Valeur", "Contact", "Coordonnee", "ValeurNormalisee", "Valeur Normalisée");
                    if (string.Equals(kind, "Email", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "Mail", StringComparison.OrdinalIgnoreCase)) AssignEmails(person, value);
                    else if (new[] { "Telephone", "Téléphone", "Tel", "Mobile" }.Contains(kind, StringComparer.OrdinalIgnoreCase)) AddPhone(person, value);
                });
                int matched = 0;
                foreach (var row in appointments)
                {
                    row.ReferenceFamilyNumber = membership.TryGetValue(row.Id, out int number) ? number : 0;
                    if (row.ReferenceFamilyNumber > 0) matched++;
                    if (details.TryGetValue(row.Id, out var person)) MergeAppointmentDetails(row, person);
                }
                return matched;

                void AddMembership(IDataRecord record)
                {
                    var fields = BuildOrdinalLookup(record);
                    if (!IsCurrent(record, fields, day)) return;
                    int id = GetInt(record, fields, "PersonId", "CodePatient", "Code Patient", "Code");
                    int family = GetInt(record, fields, "FamilyId", "NumeroFamille");
                    if (id <= 0 || family <= 0) return;
                    if (membership.TryGetValue(id, out int existing) && existing != family)
                        throw new InvalidDataException($"Le patient {id} appartient à plusieurs familles actives. Vérifiez la base EyeEmail.");
                    membership[id] = family;
                }
                bool Read(string table, Action<IDataRecord> consume)
                {
                    if (!tableNames.Contains(table)) return false;
                    using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM [" + table + "]";
                    var reader = command.ExecuteReader();
                    if (reader == null) return false;
                    using (reader) while (reader.Read()) consume(reader);
                    return true;
                }
            }
            throw new InvalidOperationException("Aucun fournisseur OLE DB Access compatible.");
        });

        private static bool IsCurrent(IDataRecord record, IReadOnlyDictionary<string, int> fields, DateTime day)
        {
            var start = GetDateTime(record, fields, "StartDate", "DateDebut", "ValidFrom");
            var end = GetDateTime(record, fields, "EndDate", "DateFin", "ValidTo");
            return (!start.HasValue || start.Value.Date <= day.Date) && (!end.HasValue || end.Value.Date >= day.Date);
        }

        private static void AddPhone(RdvInfo row, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (string.IsNullOrWhiteSpace(row.Tel1)) row.Tel1 = value;
            else if (string.IsNullOrWhiteSpace(row.Tel2) && row.Tel1 != value) row.Tel2 = value;
        }
    }
}
