using System.Collections.Generic;

namespace EyeChat.SmsMail
{
    /// <summary>
    /// Famille d'un patient, calculée par le serveur (page « Familles ») et reçue par l'onglet SMS et mail.
    /// </summary>
    public sealed class FamilyMembershipDto
    {
        public int PatientId { get; set; }
        public int FamilyNumber { get; set; }
        /// <summary>Groupe dans la famille : des personnes « gardées séparées » à un poste sont dans des groupes différents.</summary>
        public int SubGroup { get; set; }
        /// <summary>Famille « à vérifier » (chaîne, plus de 6 personnes, lien faible) : l'envoi demande avant de regrouper.</summary>
        public bool ToReview { get; set; }
        /// <summary>Pourquoi ces personnes sont regroupées (liens internes) et, le cas échéant, pourquoi la famille est à vérifier.</summary>
        public string Why { get; set; } = string.Empty;
        /// <summary>Pourquoi la famille est à vérifier (chaîne, plus de 6 personnes, lien faible), sinon vide.</summary>
        public string ReviewReason { get; set; } = string.Empty;
        /// <summary>Tous les membres de la famille (codes patient), présents ou non ce jour-là.</summary>
        public List<int> Members { get; set; } = new();
        /// <summary>Tous les liens internes de la famille : le poste n'explique que ceux qui concernent les personnes de la question.</summary>
        public List<FamilyLinkDto> Links { get; set; } = new();
    }

    public sealed class FamilyLinkDto
    {
        public int A { get; set; }
        public int B { get; set; }
        public string Strength { get; set; } = string.Empty;
        /// <summary>Raisons du lien, sans aucun nom (« carte Vitale : parent et enfant, même adresse… »).</summary>
        public string Reasons { get; set; } = string.Empty;
    }
}
