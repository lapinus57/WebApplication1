using EyeChat.Fundus;

namespace ChatServeur.Fundus;

public sealed class FoSettings
{
    public int Id { get; set; } = 1;
    public string MappingJson { get; set; } = "";
    public string SourceKey { get; set; } = "";
}

public sealed class FoAlert
{
    public int Id { get; set; }
    public string SourceKey { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string PatientName { get; set; } = "";
    public DateTime? NextAppointment { get; set; }
    public string SourceConsultationId { get; set; } = "";
    public DateTime SourceDate { get; set; }
    public string Excerpt { get; set; } = "";
    public FoStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public string LaterExcerpt { get; set; } = "";
    public DateTime? LaterDate { get; set; }
    public string EvidenceHash { get; set; } = "";
    public DateTime LastAnalysisUtc { get; set; }
    public FoStatus? ManualStatus { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public string ReviewedBy { get; set; } = "";

    public FoStatus EffectiveStatus => ManualStatus ?? Status;
}

public sealed record FoPatientData(string Id, string Name, DateTime Appointment, IReadOnlyList<FoConsultation> Consultations);
public sealed record FoDataSet(string SourceKey, IReadOnlyList<FoPatientData> Patients);
