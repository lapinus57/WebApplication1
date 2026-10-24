using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EyeChat.Messaging;

public sealed class MailTrace
{
    public int Version { get; set; } = 1;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public bool GroupingEnabled { get; set; }
    public bool IsTest { get; set; }
    public List<MailTracePerson> People { get; set; } = new();

    public static MailTrace Parse(string json)
    {
        if (json.Length > 200000) throw new ArgumentException("Trace du mail trop volumineuse.");
        MailTrace? trace;
        try { trace = JsonSerializer.Deserialize<MailTrace>(json); }
        catch (JsonException) { throw new ArgumentException("Trace du mail invalide."); }
        if (trace == null || trace.Version != 1 || trace.PreparedAtUtc == default || trace.People == null || trace.People.Count is < 1 or > 200)
            throw new ArgumentException("Trace du mail invalide.");
        foreach (var p in trace.People)
            if (p == null || p.Name == null || p.Name.Length > 512 || p.DestinationReason == null || p.DestinationReason.Length > 10000
                || p.GroupingReasons == null || p.GroupingReasons.Count > 200 || p.GroupingReasons.Any(s => s == null || s.Length > 20000))
                throw new ArgumentException("Trace du mail invalide.");
        return trace;
    }
}

public sealed class MailTracePerson
{
    public int PatientId { get; set; }
    public string Name { get; set; } = "";
    public DateTime AppointmentAt { get; set; }
    public DateTime AnnouncedAt { get; set; }
    public int FamilyNumber { get; set; }
    public int ReferenceFamilyNumber { get; set; }
    public string DestinationReason { get; set; } = "";
    public List<string> GroupingReasons { get; set; } = new();
}

public static class MailReference
{
    public static string Normalize(string value) => string.Concat(value.Where(c => c != '-' && !char.IsWhiteSpace(c)));
    public static bool IsValid(string value) => value.Length == 20 && value.All(c => c >= '0' && c <= '9');
    public static string Display(string value) => IsValid(value) ? string.Join("-", Enumerable.Range(0, 5).Select(i => value.Substring(i * 4, 4))) : value;
    public static bool Matches(string reference, string query)
    {
        var normalized = Normalize(query);
        return reference.Length > 0 && normalized.Length > 0 && reference.Contains(normalized, StringComparison.Ordinal);
    }
}
