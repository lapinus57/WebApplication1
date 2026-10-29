using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.SmsMail.Services
{
    /// <summary>
    /// Builds the SMS/mail appointment query from the columns that really exist in the Access file.
    /// Access reports an unknown field as "Aucune valeur donnée pour un ou plusieurs des paramètres requis";
    /// checking the schema first gives a message that names the missing field instead.
    /// </summary>
    public static class AppointmentQuery
    {
        public static readonly string[] PatientKeyNames = { "Code Patient", "CodePatient" };
        public static readonly string[] EmailNames = { "E-mail", "Email", "E mail", "Mail", "Adresse mail", "AdresseMail", "Courriel" };

        /// <param name="tables">Table names of the Access file.</param>
        /// <param name="columns">Returns the column names of one table.</param>
        public static string BuildDay(string appointmentsTable, string dateColumn, string timeColumn,
            IReadOnlyCollection<string> tables, Func<string, IReadOnlyCollection<string>> columns)
        {
            var table = Find(tables, appointmentsTable)
                ?? throw new InvalidOperationException($"Table des rendez-vous « {appointmentsTable} » introuvable dans la base Access. Vérifiez Paramètres → Recherche RDV.");
            var rdv = columns(table);
            var date = Require(rdv, dateColumn, table, "date");
            var time = Require(rdv, timeColumn, table, "heure");
            var rdvKey = Pick(rdv, PatientKeyNames)
                ?? throw new InvalidOperationException($"Champ « Code Patient » introuvable dans la table « {table} ».");

            var patients = Find(tables, "Patients");
            var patientColumns = patients == null ? Array.Empty<string>() : columns(patients);
            var patientKey = Pick(patientColumns, PatientKeyNames);
            var email = Pick(patientColumns, EmailNames);
            // Without a usable Patients table, contacts still come from the appointment row itself.
            var join = patients != null && patientKey != null;
            var select = $"SELECT r.*, r.{Q(rdvKey)} AS Code, r.{Q(date)} AS EyeChatDate, r.{Q(time)} AS EyeChatTime"
                + (join && email != null ? $", p.{Q(email)} AS PatientEmail" : "");
            var from = join ? $" FROM {Q(table)} r INNER JOIN {Q(patients!)} p ON r.{Q(rdvKey)} = p.{Q(patientKey!)}" : $" FROM {Q(table)} r";
            return select + from + $" WHERE r.{Q(date)} >= ? AND r.{Q(date)} < ? ORDER BY r.{Q(time)}";
        }

        public static string BuildHistory(string appointmentsTable, string dateColumn,
            IReadOnlyCollection<string> tables, Func<string, IReadOnlyCollection<string>> columns)
        {
            var table = Find(tables, appointmentsTable)
                ?? throw new InvalidOperationException($"Table des rendez-vous « {appointmentsTable} » introuvable dans la base Access. Vérifiez Paramètres → Recherche RDV.");
            var date = Require(columns(table), dateColumn, table, "date");
            return $"SELECT r.* FROM {Q(table)} r WHERE r.{Q(date)} >= ? AND r.{Q(date)} < ?";
        }

        private static string Require(IReadOnlyCollection<string> columns, string name, string table, string role) =>
            Pick(columns, name) ?? throw new InvalidOperationException(
                $"Champ {role} « {name} » introuvable dans la table « {table} ». Champs disponibles : {string.Join(", ", columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))}. Vérifiez Paramètres → Recherche RDV.");

        private static string? Find(IReadOnlyCollection<string> names, string wanted) =>
            names.FirstOrDefault(n => string.Equals(n, wanted?.Trim(), StringComparison.OrdinalIgnoreCase));

        private static string? Pick(IReadOnlyCollection<string> columns, params string[] wanted) =>
            wanted.Select(w => Find(columns, w)).FirstOrDefault(c => c != null);

        private static string Q(string value) => "[" + value.Replace("]", "]]") + "]";
    }
}
