using System;
using System.Collections.Generic;
using System.Linq;

namespace EyeChat.Fundus;

public sealed record FoField(string Table, string Name, int DataType);
public sealed class FoMapping
{
    public string PatientsTable { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string ConsultationsTable { get; set; } = "";
    public string ConsultationId { get; set; } = "";
    public string ConsultationPatientId { get; set; } = "";
    public string ConsultationDate { get; set; } = "";
    public string[] ObservationFields { get; set; } = Array.Empty<string>();
    public string AppointmentsTable { get; set; } = "";
    public string AppointmentPatientId { get; set; } = "";
    public string AppointmentDate { get; set; } = "";
    public string AppointmentTime { get; set; } = "";
    public string Cancelled { get; set; } = "";
    public bool NoCancellationColumn { get; set; }

    // Suggestions use names confirmed by the user/repository, and only when they exist in the inspected schema.
    public static FoMapping Suggest(IReadOnlyList<FoField> fields)
    {
        string Table(string name) => fields.Select(f => f.Table).FirstOrDefault(t => t.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";
        string Field(string table, params string[] names) => names.Select(n => fields.FirstOrDefault(f => f.Table == table && f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))?.Name).FirstOrDefault(n => n != null) ?? "";
        var p = Table("Patients"); var c = Table("Consultation"); var a = Table("Ag_Rdv");
        return new FoMapping
        {
            PatientsTable = p, PatientId = Field(p, "Code Patient"),
            LastName = Field(p, "Nom"), FirstName = Field(p, "Prénom", "Prenom"),
            ConsultationsTable = c, ConsultationId = Field(c, "N° consultation"),
            ConsultationPatientId = Field(c, "Code patient"), ConsultationDate = Field(c, "Date"),
            ObservationFields = new[] { Field(c, "Observation"), Field(c, "REFRACTION") }.Where(f => f != "").ToArray(),
            AppointmentsTable = a, AppointmentPatientId = Field(a, "Code Patient"),
            AppointmentDate = Field(a, "Date", "DateRdv"), AppointmentTime = Field(a, "Heure"),
            Cancelled = Field(a, "Annulé", "Annule")
        };
    }

    public void SelectClinicalFields(IReadOnlyList<FoField> fields)
    {
        ObservationFields = new[] { "Observation", "REFRACTION" }.Select(name =>
            fields.FirstOrDefault(f => f.Table == ConsultationsTable && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name
            ?? throw new FoDataException($"Champ médical requis absent de la table choisie : {name}."))
            .ToArray();
    }

    public void Validate(IReadOnlyList<FoField> fields)
    {
        void Require(string table, string column, string label)
        {
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column) ||
                !fields.Any(f => f.Table == table && f.Name == column))
                throw new FoDataException($"Champ manquant ou absent du schéma : {label}.");
        }
        Require(PatientsTable, PatientId, "identifiant patient");
        Require(PatientsTable, LastName, "nom");
        Require(PatientsTable, FirstName, "prénom");
        Require(ConsultationsTable, ConsultationId, "identifiant consultation");
        Require(ConsultationsTable, ConsultationPatientId, "patient de la consultation");
        Require(ConsultationsTable, ConsultationDate, "date de consultation");
        if (ObservationFields == null || ObservationFields.Length != 2 ||
            !ObservationFields.Contains("Observation", StringComparer.OrdinalIgnoreCase) ||
            !ObservationFields.Contains("REFRACTION", StringComparer.OrdinalIgnoreCase))
            throw new FoDataException("L’analyse FO doit utiliser les champs Observation et REFRACTION.");
        foreach (var field in ObservationFields) Require(ConsultationsTable, field, "texte médical");
        Require(AppointmentsTable, AppointmentPatientId, "patient du rendez-vous");
        Require(AppointmentsTable, AppointmentDate, "date du rendez-vous");
        Require(AppointmentsTable, AppointmentTime, "heure du rendez-vous");
        if (!NoCancellationColumn) Require(AppointmentsTable, Cancelled, "annulation du rendez-vous");
        if (NoCancellationColumn && !string.IsNullOrEmpty(Cancelled))
            throw new FoDataException("Choisissez un champ d’annulation ou confirmez son absence, pas les deux.");
    }
}

// Messages deliberately contain schema/configuration information only, never patient data or provider errors.
public sealed class FoDataException : Exception
{
    public FoDataException(string message) : base(message) { }
}
