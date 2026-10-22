using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using EyeChat.Fundus;

namespace Client.Fundus;

public sealed record FoReview(string EvidenceHash, FoStatus Status, string Author, DateTime Utc);

// No complete consultation is retained in the table or written to disk.
public sealed record FoPatientRow(string PatientId, string PatientName, DateTime Appointment, FoAnalysis Analysis, FoReview? Review = null)
{
    public FoStatus Status => Review?.Status ?? Analysis.Status;
    public string StatusText => FundusAnalysis.Label(Status);
    public string AppointmentText => Appointment.ToString("ddd dd/MM/yyyy\nHH:mm", CultureInfo.GetCultureInfo("fr-FR"));
    public string SourceText => Analysis.SourceDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    public string Excerpt => Analysis.Excerpt;
    public string ReviewText => Review == null ? "Détection automatique" : $"Relu par {Review.Author}";

    // Source, patient and complete evidence must match; a new consultation invalidates a review.
    public string ReviewKey(string sourceKey) => FundusAnalysis.Hash(sourceKey + "\n" + PatientId);
    public FoPatientRow WithReview(FoReview? review) => this with
    {
        Review = review?.EvidenceHash == Analysis.EvidenceHash && Enum.IsDefined(review.Status) ? review : null
    };

    public static IReadOnlyList<FoPatientRow> Analyze(FoDataSet data, DateTime now, CancellationToken token)
    {
        var rows = new List<FoPatientRow>();
        foreach (var patient in data.Patients)
        {
            token.ThrowIfCancellationRequested();
            var analysis = FundusAnalysis.Analyze(patient.Consultations, now);
            if (analysis != null) rows.Add(new(patient.Id, patient.Name, patient.Appointment, analysis));
        }
        return rows.OrderBy(r => r.Appointment).ThenBy(r => r.PatientName).ToList();
    }

    public static IReadOnlyList<FoPatientRow> Filter(IEnumerable<FoPatientRow> rows, int state, string query)
    {
        var normalized = FundusAnalysis.Normalize(query.Trim());
        return rows.Where(r => state switch
        {
            1 => r.Status == FoStatus.Planned,
            2 => r.Status == FoStatus.Verify,
            3 => r.Status == FoStatus.ProbablyPerformed,
            4 => true,
            _ => r.Status != FoStatus.ProbablyPerformed
        }).Where(r => normalized.Length == 0 || FundusAnalysis.Normalize(r.PatientName + " " + r.PatientId).Contains(normalized))
            .OrderBy(r => r.Appointment).ThenBy(r => r.PatientName).ToList();
    }
}
