using System.Collections.Generic;
using Client.SmsMail.Configuration;
using Client.SmsMail.Services;

namespace Client.SmsMail.Models
{
    // Échanges entre l'onglet SMS et mail des postes et le serveur, qui garde les réglages, lit l'agenda et envoie.

    /// <summary>Quels secrets sont enregistrés sur le serveur (leur valeur n'est jamais renvoyée aux postes).</summary>
    public sealed class SmsMailSecretsState
    {
        public bool SmsApplicationSecret { get; set; }
        public bool SmsConsumerKey { get; set; }
        public bool MailPassword { get; set; }
    }

    /// <summary>Réglages SMS et mail tels qu'un poste les reçoit : sans mots de passe ni clés secrètes.</summary>
    public sealed class SmsMailStationSettings
    {
        /// <summary>Faux tant qu'aucun poste n'a enregistré de réglages sur le serveur.</summary>
        public bool Exists { get; set; }
        public SmsMailSettings Settings { get; set; } = new();
        public SmsMailSecretsState Secrets { get; set; } = new();
    }

    public sealed class SmsMailPostIts
    {
        public List<PostItAnalysis.PostIt> PostIts { get; set; } = new();
        public List<SmsMailName> Names { get; set; } = new();
    }

    public sealed class SmsMailName
    {
        public string Nom { get; set; } = "";
        public string Prenom { get; set; } = "";
    }

    /// <summary>Résultat d'un envoi, transmis au poste.</summary>
    public sealed class SmsMailSendResult
    {
        public string Status { get; set; } = "";
        public long? ProviderId { get; set; }
        public bool Accepted { get; set; }
    }

}
