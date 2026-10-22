using System;
using System.Collections.Generic;

namespace Client.SmsMail.Models
{
    public class RdvInfo
    {
        public int Id { get; set; }
        public string Titre { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string NomJeuneFille { get; set; } = string.Empty;
        public string Prenom { get; set; } = string.Empty;
        public DateTime DatePriseRDV { get; set; }
        public DateTime DateRDV { get; set; }
        public DateTime HeureRDV { get; set; }
        public DateTime? HeureArrivee { get; set; }
        public string Remarque { get; set; } = string.Empty;
        public string Adresse { get; set; } = string.Empty;
        public string Ville { get; set; } = string.Empty;
        public string Tel1 { get; set; } = string.Empty;
        public string Tel1Info { get; set; } = string.Empty;
        public bool SMSSendTel1 { get; set; }
        public DateTime? SMSSendTel1Date { get; set; }
        public string Tel2 { get; set; } = string.Empty;
        public string Tel2Info { get; set; } = string.Empty;
        public bool SMSSendTel2 { get; set; }
        public DateTime? SMSSendTel2Date { get; set; }
        public string Email { get; set; } = string.Empty;
        public string EmailInfo { get; set; } = string.Empty;
        public List<string> AdditionalEmails { get; set; } = new();
        public bool EmailSend { get; set; }
        public DateTime? EmailSendDate { get; set; }
        public bool Istried { get; set; }
        public string TriedMethodRound1 { get; set; } = string.Empty;
        public int? FamilyMethodRound1 { get; set; }
        public string TriedMethodRound2 { get; set; } = string.Empty;
        public int? FamilyMethodRound2 { get; set; }
        public string TriedMethodRound3 { get; set; } = string.Empty;
        public int? FamilyMethodRound3 { get; set; }
        public string TriedMethodRound4 { get; set; } = string.Empty;
        public int? FamilyMethodRound4 { get; set; }
        public string TriedMethodRound5 { get; set; } = string.Empty;
        public int? FamilyMethodRound5 { get; set; }
        public string TriedMethodRound6 { get; set; } = string.Empty;
        public int? FamilyMethodRound6 { get; set; }
        public int NumeroFamille { get; set; }
        public int ReferenceFamilyNumber { get; set; }
        public string Media { get; set; } = string.Empty;
        public bool IsCancelled { get; set; }

        public override string ToString()
        {
            return $"{Titre} {Prenom} {Nom} {NomJeuneFille} {DateRDV:g} {Remarque} {Adresse} {Ville} {Tel1} {Tel2} {Email}";
        }
    }
}


