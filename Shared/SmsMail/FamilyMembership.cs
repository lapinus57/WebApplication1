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
    }
}
