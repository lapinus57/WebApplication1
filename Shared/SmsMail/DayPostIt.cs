using System;
using System.Collections.Generic;

namespace EyeChat.SmsMail
{
    /// <summary>Membre de la famille sur le post-it du jour, avec son rendez-vous du jour s'il en a un.</summary>
    public sealed class DayPostItMember
    {
        public int PatientId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Heure du rendez-vous ce jour-là, ou null s'il n'en a pas.</summary>
        public DateTime? AppointmentTime { get; set; }
    }

    /// <summary>
    /// Post-it EyeChat d'une famille pour un jour (cotations…) : un seul par famille et par jour, gardé par le serveur,
    /// affiché sur tous les postes à l'ouverture du dossier d'un membre. Il ne touche jamais aux post-it de StudioV.
    /// </summary>
    public sealed class DayPostIt
    {
        public int PatientId { get; set; }
        public DateTime Day { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Color { get; set; } = "#FFF8B0";
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
        /// <summary>Patients qui partagent ce post-it (famille au moment de la première saisie, ou le patient seul).</summary>
        public List<DayPostItMember> Members { get; set; } = new();
    }
}
