using System;
using System.Collections.Generic;
using System.Linq;
using Client.SmsMail.Models;
using EyeChat.SmsMail;

namespace Client.SmsMail.Services;

/// <summary>
/// Familles calculées par le serveur (tout l'historique de l'agenda, carte Vitale, paiements, corrections faites sur la
/// page « Familles ») appliquées aux rendez-vous du jour. Une famille « à vérifier » n'est regroupée que si la personne
/// qui envoie l'accepte, après avoir vu pourquoi.
/// </summary>
public static class ServerFamilies
{
    /// <summary>Numéros à part de ceux de la base EyeEmail, pour ne jamais mélanger les deux.</summary>
    public const int NumberOffset = 1_000_000;

    /// <summary>Réponse donnée à une famille à vérifier, à transmettre au serveur pour ne plus poser la question.</summary>
    public sealed record Answer(IReadOnlyList<int> PatientIds, bool Together);

    public static int ReferenceNumber(FamilyMembershipDto m) => NumberOffset + m.FamilyNumber * 100 + Math.Clamp(m.SubGroup, 0, 99);

    public static int Apply(IReadOnlyList<RdvInfo> rows, DateTime day, IReadOnlyList<FamilyMembershipDto> memberships, Func<string, string, bool>? confirm,
        List<Answer>? answers = null)
    {
        var byPatient = memberships.GroupBy(m => m.PatientId).ToDictionary(g => g.Key, g => g.First());
        var today = rows.Where(r => r.DateRDV.Date == day.Date && !r.IsCancelled && r.Id > 0 && byPatient.ContainsKey(r.Id)).ToList();
        int applied = 0;
        foreach (var family in today.GroupBy(r => ReferenceNumber(byPatient[r.Id])))
        {
            var people = family.GroupBy(r => r.Id).Select(g => g.First()).ToList();
            if (people.Count < 2) continue;
            // Déjà réunis par la base EyeEmail : on ne touche à rien.
            if (people.All(r => r.ReferenceFamilyNumber > 0)) continue;
            var info = byPatient[people[0].Id];
            if (info.ToReview)
            {
                var names = string.Join(", ", people.Select(r => $"{r.Prenom} {r.Nom} ({r.HeureRDV:HH:mm})".Trim()));
                var message = $"Le serveur range ces personnes dans la même famille : {names}." + Environment.NewLine + Environment.NewLine
                    + "Pourquoi :" + Environment.NewLine + info.Why + Environment.NewLine + Environment.NewLine
                    + "Regrouper leurs rappels dans un seul message ? Votre réponse est enregistrée sur le serveur : "
                    + "la question ne sera plus posée pour ces personnes, sur aucun poste. "
                    + $"(Famille n° {info.FamilyNumber} sur la page « Familles » de l'administration du serveur.)";
                if (confirm == null) continue;
                var together = confirm(message, "Famille à vérifier");
                answers?.Add(new Answer(people.Select(p => p.Id).OrderBy(id => id).ToList(), together));
                if (!together) continue;
            }
            foreach (var row in rows.Where(r => people.Any(p => p.Id == r.Id)))
            {
                if (row.ReferenceFamilyNumber == 0) row.ReferenceFamilyNumber = ReferenceNumber(info);
                row.ReferenceFamilyWhy = info.Why;
            }
            applied++;
        }
        return applied;
    }
}
