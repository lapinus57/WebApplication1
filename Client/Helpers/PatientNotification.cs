using System;
using System.Linq;

namespace Client.Helpers
{
    /// <summary>
    /// Notification Windows affichée sur les autres postes quand un patient est déclaré.
    /// Classe sans WinUI pour pouvoir être vérifiée par les tests.
    /// </summary>
    public static class PatientNotification
    {
        public const string SettingKey = "NotifyNewPatients";

        /// <summary>Pas de notification sur le poste qui a déclaré le patient, ni si l'utilisateur l'a désactivée.</summary>
        public static bool ShouldNotify(string? examinator, string? currentUser, bool enabled)
            => enabled && !string.Equals((examinator ?? string.Empty).Trim(), (currentUser ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Trois lignes : « Nouveau patient · Salle », identité, puis examens, œil et déclarant.</summary>
        public static (string Header, string Name, string Details) Build(string? title, string? lastName, string? firstName,
            string? exams, string? eye, string? position, string? examinator)
        {
            static string Join(string separator, params string?[] parts)
                => string.Join(separator, parts.Select(p => (p ?? string.Empty).Trim()).Where(p => p.Length > 0));

            var header = Join(" · ", "Nouveau patient", position);
            var name = Join(" ", title, lastName, firstName);
            var details = Join(" · ", exams, eye, string.IsNullOrWhiteSpace(examinator) ? null : "déclaré par " + examinator.Trim());
            return (header, name.Length > 0 ? name : "Patient sans nom", details);
        }
    }
}
