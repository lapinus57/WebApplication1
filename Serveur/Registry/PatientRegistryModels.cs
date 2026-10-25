using System.ComponentModel.DataAnnotations.Schema;
using EyeChat.Fundus;

namespace ChatServeur.Registry;

public sealed class PatientRegistrySettings
{
    public int Id { get; set; } = 1;
    public string MappingJson { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string Revision { get; set; } = "";
    public DateTime? LastImportUtc { get; set; }
    public int LastImportCount { get; set; }
}

public sealed class PatientRegistryEntry
{
    public int Id { get; set; }
    public string SourceKey { get; set; } = "";
    public string AccessPatientId { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string SearchName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime ImportedUtc { get; set; }
    public DateTime? LastConsultationDate { get; set; }
    public string Revision { get; set; } = "";
    public PatientConditionState DiabetesDetected { get; set; }
    public string DiabetesEvidence { get; set; } = "";
    public DateTime? DiabetesSourceDate { get; set; }
    public string DiabetesEvidenceHash { get; set; } = "";
    public PatientConditionState? DiabetesManual { get; set; }
    public string DiabetesReviewedHash { get; set; } = "";
    public string DiabetesReviewedBy { get; set; } = "";
    public DateTime? DiabetesReviewedUtc { get; set; }
    public PatientConditionState HtoDetected { get; set; }
    public string HtoEvidence { get; set; } = "";
    public DateTime? HtoSourceDate { get; set; }
    public string HtoEvidenceHash { get; set; } = "";
    public PatientConditionState? HtoManual { get; set; }
    public string HtoReviewedHash { get; set; } = "";
    public string HtoReviewedBy { get; set; } = "";
    public DateTime? HtoReviewedUtc { get; set; }
    [NotMapped] public PatientConditionState Diabetes => DiabetesManual ?? DiabetesDetected;
    [NotMapped] public PatientConditionState Hto => HtoManual ?? HtoDetected;
    [NotMapped] public bool DiabetesNeedsReview => DiabetesManual.HasValue && DiabetesReviewedHash != DiabetesEvidenceHash;
    [NotMapped] public bool HtoNeedsReview => HtoManual.HasValue && HtoReviewedHash != HtoEvidenceHash;
}
