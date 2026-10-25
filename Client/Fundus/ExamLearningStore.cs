using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using EyeChat.Fundus;

namespace Client.Fundus;

// Training notes and annotations stay encrypted in this Windows profile. Only the model DTO is exportable.
public sealed class ExamLearningStore
{
    public string Salt { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceKey { get; set; } = "";
    public List<ExamExample> Examples { get; set; } = new();
    public ExamTreeModel? Candidate { get; set; }
    public ExamTreeModel? Active { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeChat", "Fundus", "learning.dat");
    private const int MaxBytes = 64 * 1024 * 1024;

    public static ExamLearningStore Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            if (new FileInfo(FilePath).Length > MaxBytes) throw new InvalidDataException();
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            try
            {
                var result = JsonSerializer.Deserialize<ExamLearningStore>(bytes) ?? throw new JsonException();
                result.Validate(); return result;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch { throw new FoDataException("Le jeu d’apprentissage local est illisible. Il n’a pas été remplacé. Vérifiez le profil Windows utilisé."); }
    }

    public void Validate()
    {
        if (!Guid.TryParseExact(Salt, "N", out _) || Examples == null || Examples.Count > 1000) throw new FoDataException("Jeu d’apprentissage invalide.");
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var example in Examples) { ExamLearningData.Validate(example); if (!groups.Add(example.Group)) throw new FoDataException("Patient présent plusieurs fois dans le jeu."); }
        if (Candidate != null) ExamDecisionTree.Validate(Candidate);
        if (Active != null) ExamDecisionTree.Validate(Active);
    }

    public void Save()
    {
        Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        try
        {
            if (bytes.Length > MaxBytes - 1024) throw new FoDataException("Le jeu local dépasse 64 Mo. Aucun fichier n’a été remplacé.");
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, encrypted); File.Move(temp, FilePath, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public string Group(string source, string patientId) => FundusAnalysis.Hash(Salt + "\n" + source + "\n" + patientId);
}
