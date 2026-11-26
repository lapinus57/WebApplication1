using Client.SmsMail.Models;
using Client.SmsMail.Services;
using EyeChat.SmsMail;

static class ServerFamilyChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var day = new DateTime(2026, 12, 3);
        RdvInfo P(int id, string first, string last, int minutes, string phone) => new()
        {
            Id = id, Prenom = first, Nom = last, DateRDV = day, HeureRDV = day.AddHours(9).AddMinutes(minutes), Tel1 = phone,
            Adresse = id + " rue Fictive", Ville = "Ville fictive"
        };
        var mother = P(10072, "Monique", "Klein", 0, "0611111111");
        var son = P(18390, "Marc", "Klein", 90, "0622222222");
        var stranger = P(500, "Paul", "Autre", 30, "0633333333");
        var known = new List<FamilyMembershipDto>
        {
            new() { PatientId = 10072, FamilyNumber = 12, Why = "Monique Klein et Marc Klein : carte Vitale : parent et enfant" },
            new() { PatientId = 18390, FamilyNumber = 12, Why = "Monique Klein et Marc Klein : carte Vitale : parent et enfant" },
        };
        var rows = new List<RdvInfo> { mother, son, stranger };
        Check(ServerFamilies.Apply(rows, day, known, null) == 1 && mother.ReferenceFamilyNumber == ServerFamilies.NumberOffset + 1200
            && son.ReferenceFamilyNumber == mother.ReferenceFamilyNumber && stranger.ReferenceFamilyNumber == 0, "Sure server family applied without asking");
        var families = EyeEmailFamilyGrouping.Build(rows);
        var together = families.Single(f => f.RDVs.Contains(mother));
        Check(together.RDVs.Contains(son) && !together.RDVs.Contains(stranger) && together.GroupingDisplay == "Famille connue du serveur",
            "Different phones, but the server family puts them in one message");
        Check(GroupingExplanation.Explain(together.RDVs).Contains("Famille connue du serveur : Monique Klein et Marc Klein : carte Vitale"),
            "The message explains why (server reasons)");

        // Famille à vérifier : on demande, avec le pourquoi
        foreach (var r in rows) { r.ReferenceFamilyNumber = 0; r.ReferenceFamilyWhy = ""; }
        foreach (var m in known) { m.ToReview = true; m.Why = "À vérifier (certains membres ne sont reliés que par l'intermédiaire d'un autre)\n" + m.Why; }
        string? asked = null;
        var answers = new List<ServerFamilies.Answer>();
        Check(ServerFamilies.Apply(rows, day, known, (message, title) => { asked = message; return false; }, answers) == 0 && mother.ReferenceFamilyNumber == 0,
            "Family to review, answer no: not grouped");
        Check(answers.Single() is { Together: false } a0 && a0.PatientIds.SequenceEqual(new[] { 10072, 18390 }), "The answer is kept to be sent to the server");
        Check(asked != null && asked.Contains("Monique Klein (09:00)") && asked.Contains("Marc Klein (10:30)") && asked.Contains("Pourquoi") && asked.Contains("n° 12"),
            "The question shows who, when and why");
        Check(ServerFamilies.Apply(rows, day, known, (_, _) => true) == 1 && son.ReferenceFamilyNumber == ServerFamilies.NumberOffset + 1200, "Answer yes: grouped");
        foreach (var r in rows) r.ReferenceFamilyNumber = 0;
        Check(ServerFamilies.Apply(rows, day, known, null) == 0, "Family to review without a way to ask: never grouped");
        // Le serveur a gardé « séparés » : deux groupes, plus de famille à proposer
        foreach (var r in rows) r.ReferenceFamilyNumber = 0;
        known[1].SubGroup = 1;
        Check(ServerFamilies.Apply(rows, day, known, (_, _) => throw new Exception("asked again")) == 0 && mother.ReferenceFamilyNumber == 0, "Kept apart on the server: not asked again");
        known[1].SubGroup = 0;

        // Une seule personne de la famille ce jour-là, ou famille EyeEmail déjà connue : rien ne change
        Check(ServerFamilies.Apply(new List<RdvInfo> { P(10072, "Monique", "Klein", 0, "0611111111") }, day, known, (_, _) => true) == 0, "Alone today: nothing to group");
        var a = P(10072, "Monique", "Klein", 0, "0611111111"); var b = P(18390, "Marc", "Klein", 90, "0622222222");
        a.ReferenceFamilyNumber = b.ReferenceFamilyNumber = 7;
        Check(ServerFamilies.Apply(new List<RdvInfo> { a, b }, day, known, (_, _) => true) == 0 && a.ReferenceFamilyNumber == 7, "EyeEmail family kept");
        return count;
    }
}
