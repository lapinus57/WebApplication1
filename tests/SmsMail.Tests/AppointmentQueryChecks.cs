using Client.SmsMail.Services;

// The SMS/mail query must only name fields that exist in the Access file.
static class AppointmentQueryChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); count++; }
        var tables = new[] { "Ag_rdv", "Patients" };
        IReadOnlyCollection<string> Columns(string table, string email) => table == "Ag_rdv"
            ? new[] { "Code Patient", "Date", "Heure", "Nom", "Annulé" }
            : new[] { "Code Patient", "Nom", email };

        var sql = AppointmentQuery.BuildDay("Ag_rdv", "Date", "Heure", tables, t => Columns(t, "E-mail"));
        Check(sql.Contains("p.[E-mail] AS PatientEmail") && !sql.Contains("p.Email"), "Patient email uses the real field name E-mail");
        Check(sql.Contains("INNER JOIN [Patients] p ON r.[Code Patient] = p.[Code Patient]"), "Join keeps the real table and key names");
        Check(AppointmentQuery.BuildDay("ag_rdv", "date", "heure", tables, t => Columns(t, "Email")).Contains("p.[Email] AS PatientEmail"), "Names are matched without case");

        var noEmail = AppointmentQuery.BuildDay("Ag_rdv", "Date", "Heure", tables, t => Columns(t, "Téléphone"));
        Check(!noEmail.Contains("PatientEmail") && noEmail.Contains("INNER JOIN"), "Missing email field is left out instead of failing");
        Check(!AppointmentQuery.BuildDay("Ag_rdv", "Date", "Heure", new[] { "Ag_rdv" }, t => Columns(t, "")).Contains("JOIN"), "Without Patients, the appointment row alone is read");

        foreach (var (table, date, time, expected) in new[] { ("Agenda", "Date", "Heure", "Agenda"), ("Ag_rdv", "Jour", "Heure", "Jour"), ("Ag_rdv", "Date", "Horaire", "Horaire") })
        {
            try { AppointmentQuery.BuildDay(table, date, time, tables, t => Columns(t, "E-mail")); Check(false, "Unknown field must be named"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains(expected) && ex.Message.Contains("introuvable"), "Clear message names the missing field: " + expected); }
        }
        Check(AppointmentQuery.BuildHistory("Ag_rdv", "Date", tables, t => Columns(t, "E-mail")) == "SELECT r.* FROM [Ag_rdv] r WHERE r.[Date] >= ? AND r.[Date] < ?", "History query uses checked names");
        return count;
    }
}
