using Microsoft.EntityFrameworkCore;

namespace ChatServeur.EyeMail;

/// <summary>
/// Base EyeChat des patients (data/eyemail.db), séparée de chat.db.
/// Première table : tous les patients du cabinet, recopiés depuis Access (qui reste en lecture seule).
/// Un patient absent d'Access n'est jamais supprimé : il est marqué inactif.
/// </summary>
public sealed class EyeMailPatient
{
    public int Id { get; set; }
    /// <summary>Identifiant du patient dans Access (Code Patient). Unique.</summary>
    public string AccessPatientId { get; set; } = "";
    public string Civility { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string MaidenName { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public string Address { get; set; } = "";
    public string Address2 { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Phone1 { get; set; } = "";
    public string Phone1Info { get; set; } = "";
    public string Phone2 { get; set; } = "";
    public string Phone2Info { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmailInfo { get; set; } = "";
    /// <summary>Nom, prénom, nom de jeune fille et identifiant sans accents ni ponctuation, pour la recherche.</summary>
    public string SearchName { get; set; } = "";
    /// <summary>Faux si le patient n'était plus dans Access au dernier import.</summary>
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
    /// <summary>Dernière fois où une donnée du patient a changé dans Access.</summary>
    public DateTime UpdatedUtc { get; set; }
    /// <summary>Dernier import où le patient était présent dans Access.</summary>
    public DateTime LastSeenUtc { get; set; }
    /// <summary>Empreinte des champs recopiés : un import ne réécrit que les patients modifiés.</summary>
    public string ContentHash { get; set; } = "";
}

/// <summary>Historique des imports : quand, depuis quelle table, et ce qui a changé.</summary>
public sealed class EyeMailImportRun
{
    public int Id { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime FinishedUtc { get; set; }
    public string SourceTable { get; set; } = "";
    public int Read { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Reactivated { get; set; }
    public int Deactivated { get; set; }
}

public sealed class EyeMailDbContext(DbContextOptions<EyeMailDbContext> options) : DbContext(options)
{
    public DbSet<EyeMailPatient> Patients => Set<EyeMailPatient>();
    public DbSet<EyeMailImportRun> ImportRuns => Set<EyeMailImportRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EyeMailPatient>().HasIndex(p => p.AccessPatientId).IsUnique();
        modelBuilder.Entity<EyeMailPatient>().HasIndex(p => new { p.IsActive, p.LastName, p.FirstName });
    }
}
