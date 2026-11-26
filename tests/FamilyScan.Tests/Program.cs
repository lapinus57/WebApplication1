// Reconstitution des familles sur un historique synthétique couvrant les trois périodes d'AG_rdv :
// 2001-2006 (NoSS, pas de date de création ni d'arrivée), 2006-2007, puis fin 2007+ (création et arrivée).
using EyeChat.Families;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("ÉCHEC : " + message);
    checks++;
}

var rows = new List<ScanRow>();
ScanRow Row(int id, DateTime day, int hour, int minute, string surname, string first, string address = "", string phone = "", string email = "",
    string noss = "", DateTime? created = null, int? arrivalHour = null, int arrivalMinute = 0, string maiden = "", bool cancelled = false)
{
    var time = day.Date.AddHours(hour).AddMinutes(minute);
    var row = new ScanRow(id, day.Date, time, created, arrivalHour.HasValue ? day.Date.AddHours(arrivalHour.Value).AddMinutes(arrivalMinute) : null,
        noss, surname, maiden, first, address, "Metz", new[] { phone }, new[] { email }, cancelled);
    rows.Add(row);
    return row;
}

// Famille A (2003) : même NoSS, pas encore de date de création → parenté ; venus ensemble encore en 2025 → même foyer.
Row(1, new(2003, 3, 10), 9, 0, "MARTIN", "Paul", "1 rue A", "0387000001", noss: "1 55 03 57 463 012 45");
Row(2, new(2003, 3, 10), 9, 15, "MARTIN", "Léo", "1 rue A", "0387000001", noss: "155035746301299");
Row(1, new(2025, 5, 2), 10, 0, "MARTIN", "Paul", "1 rue A", "0612000001", created: new(2025, 1, 4, 11, 32, 10), arrivalHour: 9, arrivalMinute: 50);
Row(2, new(2025, 5, 2), 10, 15, "MARTIN", "Léo", "1 rue A", "0612000002", created: new(2025, 1, 4, 11, 32, 40), arrivalHour: 9, arrivalMinute: 52);

// Parenté ancienne seulement : même NoSS en 2004, jamais revenus ensemble, contacts différents → lien, mais pas même foyer.
Row(3, new(2004, 6, 1), 14, 0, "DURAND", "Anne", "2 rue B", "0387000003", noss: "2700457463012");
Row(4, new(2004, 9, 1), 15, 0, "DURAND", "Tom", "2 rue B", "0387000004", noss: "2700457463012");
Row(3, new(2024, 2, 1), 8, 0, "DURAND", "Anne", "9 rue X", "0611000003");
Row(4, new(2024, 6, 1), 16, 0, "DURAND", "Tom", "5 rue Y", "0611000004");

// Voisins d'immeuble : même adresse, noms et contacts différents, RDV pris à des mois d'écart, 3 h 30 d'écart → aucun lien.
Row(5, new(2026, 11, 20), 13, 0, "FRANZ", "Henri", "7 rue de l'Église", "0372000005", created: new(2026, 6, 2, 9, 40, 0));
Row(6, new(2026, 11, 20), 16, 30, "VINGT", "Béa", "7 rue de l'Église", "0631000006", created: new(2026, 3, 6, 15, 11, 0));

// Inconnus à des créneaux voisins, sans aucun indice commun → aucun lien.
Row(7, new(2026, 11, 20), 9, 0, "ALPHA", "A", "10 rue C", "0600000007", created: new(2026, 10, 1, 9, 0, 0));
Row(8, new(2026, 11, 20), 9, 15, "BETA", "B", "11 rue D", "0600000008", created: new(2026, 10, 2, 9, 0, 0));

// Couple récent : RDV pris à la même minute, venus ensemble → forte, même foyer.
Row(9, new(2026, 11, 20), 11, 0, "PETIT", "Jean", "3 rue E", "0601000009", created: new(2026, 9, 1, 10, 5, 0));
Row(10, new(2026, 11, 20), 11, 15, "GRAND", "Lise", "3 rue E", "0601000010", created: new(2026, 9, 1, 10, 5, 30));

// Numéro collectif (EHPAD) partagé par 8 résidents → ignoré.
for (int i = 0; i < 8; i++) Row(20 + i, new(2026, 1, 5 + i), 10, 0, "RESIDENT" + (char)('A' + i), "X", "Résidence Les Tilleuls", "0387999999");

// Chaîne : 11–12 même téléphone, 12–13 même mail, rien entre 11 et 13 → famille à vérifier.
Row(11, new(2026, 4, 1), 9, 0, "LOUP", "A", "", "0602000011", "chaine@example.fr", created: new(2026, 3, 1, 8, 0, 0));
Row(12, new(2026, 4, 1), 9, 15, "LOUP", "B", "", "0602000011", "chaine2@example.fr", created: new(2026, 3, 1, 8, 0, 20));
Row(12, new(2026, 5, 6), 14, 0, "LOUP", "B", "", "0602000011", "chaine2@example.fr", created: new(2026, 4, 2, 10, 0, 0));
Row(13, new(2026, 5, 6), 14, 15, "RENARD", "C", "", "0602000013", "conjointe : chaine2@example.fr", created: new(2026, 4, 9, 16, 0, 0));

// Habitués du même créneau : arrivés à la même heure 3 fois, sans aucun autre point commun → aucun lien.
foreach (var d in new[] { new DateTime(2015, 3, 2), new DateTime(2016, 3, 7), new DateTime(2017, 3, 6) })
{
    Row(30, d, 9, 0, "GAMMA", "G", "12 rue G", "0600000030", created: d.AddDays(-30).AddHours(9), arrivalHour: 8, arrivalMinute: 55);
    Row(31, d, 9, 15, "DELTA", "D", "13 rue H", "0600000031", created: d.AddDays(-20).AddHours(15), arrivalHour: 8, arrivalMinute: 58);
}

// Report de RDV en série : 6 patients sans rapport recréés à la même minute, à des créneaux voisins → pas de famille.
for (int i = 0; i < 6; i++) Row(40 + i, new DateTime(2027, 2, 3), 14, i * 15, "SERIE" + (char)('A' + i), "X", $"{i} rue S", "060000004" + i, created: new DateTime(2026, 11, 20, 16, 12, i * 5));

// Famille de 4 : un parent relié à chacun (distance 2 entre enfants) → normale, pas « à vérifier ».
Row(50, new(2026, 6, 1), 9, 0, "ROSE", "Mère", "4 rue R", "0603000050", created: new(2026, 5, 1, 9, 0, 0));
Row(51, new(2026, 6, 1), 9, 15, "ROSE", "E1", "4 rue R", "0603000050", created: new(2026, 5, 1, 9, 0, 10));
Row(52, new(2026, 6, 8), 9, 0, "ROSE", "Mère", "4 rue R", "0603000050", created: new(2026, 5, 2, 9, 0, 0));
Row(53, new(2026, 6, 8), 9, 15, "ROSE", "E2", "4 rue R", "0603000050", created: new(2026, 5, 2, 9, 0, 10));

// Vraie chaîne longue : A–B (téléphone), B–C (mail), C–D (téléphone), rien d'autre → à vérifier.
Row(60, new(2026, 7, 1), 9, 0, "UN", "A", "", "0604000060", "a60@example.fr"); Row(61, new(2026, 7, 1), 9, 15, "DEUX", "B", "", "0604000060", "b61@example.fr");
Row(61, new(2026, 7, 2), 9, 0, "DEUX", "B", "", "0604000060", "b61@example.fr"); Row(62, new(2026, 7, 2), 9, 15, "TROIS", "C", "", "0604000062", "b61@example.fr");
Row(62, new(2026, 7, 3), 9, 0, "TROIS", "C", "", "0604000062", "b61@example.fr"); Row(63, new(2026, 7, 3), 9, 15, "QUATRE", "D", "", "0604000062", "d63@example.fr");

// Portable de la grand-mère noté sur les fiches de deux foyers (même nom, adresses différentes, jamais venus ensemble)
// → lien, mais pas même foyer.
Row(80, new(2026, 1, 5), 9, 0, "MAMIE", "Rose", "1 rue M", "0607000080"); Row(81, new(2026, 2, 9), 14, 0, "MAMIE", "Léo", "8 rue N", "0607000080");
// Même téléphone, même nom ET même adresse, jamais venus ensemble → même foyer.
Row(82, new(2026, 1, 6), 9, 0, "MAISON", "Ana", "3 rue O", "0607000082"); Row(83, new(2026, 3, 9), 15, 0, "MAISON", "Eva", "3 rue O", "0607000082");

// Résidents d'un EHPAD réservés et venus ensemble 3 fois, sans nom, adresse ni contact commun → liés, pas même foyer.
foreach (var d in new[] { new DateTime(2025, 2, 3), new DateTime(2025, 9, 8), new DateTime(2026, 4, 6) })
{
    Row(85, d, 10, 0, "RESA", "A", "Chambre 12", "0608000085", created: d.AddDays(-40).AddHours(14), arrivalHour: 9, arrivalMinute: 40);
    Row(86, d, 10, 15, "RESB", "B", "Chambre 14", "0608000086", created: d.AddDays(-40).AddHours(14).AddSeconds(30), arrivalHour: 9, arrivalMinute: 41);
}

// Coordonnée périmée : même téléphone, même nom, même adresse, mais l'un n'est plus venu depuis 2012 → pas « actuel ».
Row(87, new(2012, 5, 2), 9, 0, "ANCIEN", "Parti", "6 rue Z", "0609000087"); Row(88, new(2026, 5, 2), 9, 0, "ANCIEN", "Reste", "6 rue Z", "0609000087");

// Rendez-vous annulé : ne compte pas comme venue commune.
Row(14, new(2026, 2, 2), 9, 0, "ANNUL", "A", "8 rue F", created: new(2026, 1, 1, 9, 0, 0));
Row(15, new(2026, 2, 2), 9, 15, "ANNUL", "B", "8 rue F", created: new(2026, 1, 1, 9, 0, 0), cancelled: true);

var scanner = new FamilyScanner { Today = new DateTime(2026, 11, 24) };
foreach (var row in rows.OrderBy(r => r.Day).ThenBy(r => r.Time)) scanner.Add(row);
var result = scanner.Finish();
PatientLink? L(int a, int b) => result.Links.FirstOrDefault(l => l.A == Math.Min(a, b) && l.B == Math.Max(a, b));

Check(L(1, 2) is { Strength: LinkStrength.Forte, SharedNoSS: true, SameHouseholdNow: true, BookedTogetherDays: 1, ArrivedTogetherDays: 1 }, "NoSS commun en 2003 + venue commune récente : forte, même foyer");
Check(L(1, 2)!.Reasons.Contains("même numéro d'assuré 2003") && L(1, 2)!.Reasons.Contains("venus ensemble 2 fois"), "Raisons lisibles");
Check(L(3, 4) is { Strength: LinkStrength.Forte, SameHouseholdNow: false }, "NoSS commun ancien sans venue récente : parenté, pas même foyer");
Check(L(5, 6) == null, "Voisins d'immeuble : aucun lien");
Check(L(7, 8) == null, "Inconnus à des créneaux voisins : aucun lien");
Check(L(9, 10) is { Strength: LinkStrength.Forte, SameHouseholdNow: true }, "RDV pris à la même minute et venus ensemble : forte");
Check(Enumerable.Range(20, 8).All(a => Enumerable.Range(20, 8).All(b => a >= b || L(a, b) == null)) && result.CollectivePhones == 1, "Numéro collectif ignoré");
Check(L(11, 13) == null && L(11, 12) != null && L(12, 13) != null, "Chaîne : pas de lien direct entre les extrémités");
var chain = result.Families.Single(f => f.Members.Contains(11));
Check(chain.Members.SequenceEqual(new[] { 11, 12, 13 }) && !chain.HasChain, "Trois personnes A–B–C : famille normale");
Check(Enumerable.Range(40, 6).All(a => Enumerable.Range(40, 6).All(b => a >= b || L(a, b) is null or { SameHouseholdNow: false })) && !result.Families.Any(f => f.Members.Contains(40)),
    "Report de RDV en série (même minute, rien d'autre en commun) : pas de famille");
Check(L(30, 31) == null, "Arrivés à la même heure sans autre indice (habitués du créneau) : aucun lien");
var longChain = result.Families.Single(f => f.Members.Contains(60));
Check(longChain.Members.Count == 4 && longChain.HasChain && longChain.ToReview, "Chaîne A–B–C–D : à vérifier");
Check(L(80, 81) is { Strength: LinkStrength.Forte, SharedContactNow: true, SameHouseholdNow: false }, "Téléphone commun seul (autre adresse, jamais venus ensemble) : lié, pas même foyer");
Check(L(85, 86) is { Strength: LinkStrength.Forte, SameHouseholdNow: false }, "Réservés et venus ensemble sans aucun indice d'identité : liés, jamais même foyer (confidentialité)");
Check(L(87, 88) is { SharedContactNow: false, SameAddressNow: false, SameHouseholdNow: false }, "Patient plus venu depuis 2012 : téléphone et adresse ne sont plus « actuels »");
Check(L(82, 83) is { SameHouseholdNow: true }, "Même téléphone, même nom, même adresse : même foyer");
var rose = result.Families.Single(f => f.Members.Contains(50));
Check(rose.Members.Count == 4 && !rose.HasChain && !rose.ToReview, "Parent relié à chaque enfant : famille de 4 non signalée");
Check(L(14, 15) == null || L(14, 15)!.CovisitDays == 0, "Annulation : pas de venue commune");
Check(result.Families.Single(f => f.Members.Contains(1)).Members.SequenceEqual(new[] { 1, 2 }), "Famille MARTIN");
Check(!result.Families.Any(f => f.Members.Contains(3)), "Parenté ancienne : pas de famille pour les rappels");
Check(result.PatientsById[1].Phones.SetEquals(new[] { "0612000001" }), "Coordonnées les plus récentes");
Check(result.Years.Single(y => y.Year == 2003).WithNoSS == 2 && result.Years.Single(y => y.Year == 2026).WithCreated > 0, "Statistiques par année");

// RDV futurs : « récent » se compte depuis aujourd'hui, pas depuis le dernier RDV de l'agenda (2028).
var future = new FamilyScanner { Today = new DateTime(2026, 11, 24) };
future.Add(new ScanRow(70, new DateTime(2024, 1, 10), new DateTime(2024, 1, 10, 9, 0, 0), null, null, "", "OMEGA", "", "A", "", "", new[] { "0600000070" }, Array.Empty<string>(), false));
future.Add(new ScanRow(71, new DateTime(2024, 1, 10), new DateTime(2024, 1, 10, 9, 15, 0), null, null, "", "OMEGA", "", "B", "", "", new[] { "0600000071" }, Array.Empty<string>(), false));
future.Add(new ScanRow(70, new DateTime(2024, 6, 10), new DateTime(2024, 6, 10, 9, 0, 0), null, null, "", "OMEGA", "", "A", "", "", new[] { "0600000070" }, Array.Empty<string>(), false));
future.Add(new ScanRow(71, new DateTime(2024, 6, 10), new DateTime(2024, 6, 10, 9, 15, 0), null, null, "", "OMEGA", "", "B", "", "", new[] { "0600000071" }, Array.Empty<string>(), false));
future.Add(new ScanRow(99, new DateTime(2028, 7, 8), new DateTime(2028, 7, 8, 9, 0, 0), null, null, "", "FUTUR", "", "Z", "", "", Array.Empty<string>(), Array.Empty<string>(), false));
var futureResult = future.Finish();
var stale = new FamilyScanner { Today = new DateTime(2026, 11, 24) };
foreach (var row in new[] { (70, 2020, 9, 0), (71, 2020, 9, 15), (70, 2021, 9, 0), (71, 2021, 9, 15) })
    stale.Add(new ScanRow(row.Item1, new DateTime(row.Item2, 3, 1), new DateTime(row.Item2, 3, 1, row.Item3, row.Item4, 0), null, null, "", "OMEGA", "", "A", "", "", new[] { "06000000" + row.Item1 }, Array.Empty<string>(), false));
stale.Add(new ScanRow(99, new DateTime(2028, 7, 8), new DateTime(2028, 7, 8, 9, 0, 0), null, null, "", "FUTUR", "", "Z", "", "", Array.Empty<string>(), Array.Empty<string>(), false));
Check(stale.Finish().Links.Single(l => l.A == 70) is { Strength: LinkStrength.Moyenne, SameHouseholdNow: false }, "Venus ensemble en 2021 seulement : plus le même foyer aujourd'hui (2028 n'est pas « aujourd'hui »)");
Check(futureResult.Links.Single(l => l.A == 70) is { Strength: LinkStrength.Moyenne, SameHouseholdNow: true }, "Venus ensemble en 2024 : récent par rapport à aujourd'hui malgré des RDV en 2028");

try { var s2 = new FamilyScanner(); s2.Add(rows[2]); s2.Add(rows[0]); Check(false, "Ordre chronologique exigé"); }
catch (InvalidOperationException) { checks++; }

Check(FamilyScanner.NormalizeNoSS("1 55 03 57 463 012 45") == "1550357463012" && FamilyScanner.NormalizeNoSS("0000000000000") == "" && FamilyScanner.NormalizeNoSS("123") == "", "NoSS normalisé");
Check(FamilyScanner.NormalizePhone("+33 6 12 00 00 01") == "0612000001" && FamilyScanner.NormalizePhone("00 00") == "", "Téléphones normalisés");
Check(FamilyScanner.NormalizeEmail("conjointe : Chaine2@Example.fr") == "chaine2@example.fr" && FamilyScanner.NormalizeEmail("pas de mail") == "", "Mails annotés");

var report = FamilyScanReport.Anonymous(result);
var names = rows.SelectMany(r => new[] { r.Surname, r.FirstName }).Where(n => n.Length > 2).Distinct();
Check(!names.Any(n => report.Contains(n, StringComparison.OrdinalIgnoreCase)), "Rapport anonymisé : aucun nom");
Check(!report.Contains("0612000001") && !report.Contains("1550357463012") && !report.Contains("@"), "Rapport anonymisé : ni téléphone, ni NoSS, ni mail");
Check(report.Contains("même numéro d'assuré") && report.Contains("Remplissage des colonnes") && report.Contains("Patients actifs") && report.Contains("Carte Vitale : NON LUE"),
    "Rapport : indices, remplissage, patients actifs, Vitale absente signalée");
var detail = FamilyScanReport.DetailCsv(result);
Check(detail.Contains("Paul MARTIN") && !detail.Contains("1550357463012"), "Détail nominatif sans NoSS");

// Lecture d'une table AG_rdv simulée : noms de colonnes réels, heures Access au 30/12/1899, annulation.
var agenda = new System.Data.DataTable();
foreach (var (name, type) in new[] { ("Code Patient", typeof(int)), ("Date", typeof(DateTime)), ("Heure", typeof(DateTime)), ("Pris le", typeof(DateTime)),
    ("Arrivé à", typeof(DateTime)), ("NoSS", typeof(string)), ("Nom", typeof(string)), ("Prénom", typeof(string)), ("Tel 1", typeof(string)), ("E-mail", typeof(string)), ("Annulé", typeof(bool)) })
    agenda.Columns.Add(name, type);
agenda.Rows.Add(42, new DateTime(2010, 5, 3), new DateTime(1899, 12, 30, 9, 15, 0), new DateTime(2010, 4, 1, 10, 2, 0), new DateTime(1899, 12, 30, 9, 5, 0), "1 55 03", "MARTIN", "Paul", "06 12 00 00 01", "p@example.fr", false);
agenda.Rows.Add(DBNull.Value, new DateTime(2010, 5, 3), DBNull.Value, DBNull.Value, DBNull.Value, "", "", "", "", "", true);
var columnsFound = agenda.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).ToList();
var map = Schema.Map(columnsFound, new Dictionary<string, string>());
Check(map["patient"] == "Code Patient" && map["creation"] == "Pris le" && map["arrivee"] == "Arrivé à" && map["noss"] == "NoSS" && map["prenom"] == "Prénom" && map["adresse"] == null,
    "Colonnes d'AG_rdv reconnues (accents compris), absentes signalées");
Check(Schema.Map(columnsFound, new Dictionary<string, string> { ["noss"] = "nOsS" })["noss"] == "NoSS", "Colonne imposée avec --colonne");
var cabinetColumns = new List<string> { "Code Patient", "Date", "Heure", "Tel 1", "Tel Bureau", "EmailPatient", "Email" };
var cabinetMap = Schema.Map(cabinetColumns, new Dictionary<string, string>());
Check(cabinetMap["tel2"] == "Tel Bureau" && cabinetMap["email"] == "EmailPatient", "Colonnes du cabinet : Tel Bureau et EmailPatient");
using (var agendaReader = agenda.CreateDataReader())
{
    var rowReader = new RowReader(agendaReader, map);
    agendaReader.Read();
    var first = rowReader.Row()!;
    Check(first.PatientId == 42 && first.Time == new DateTime(2010, 5, 3, 9, 15, 0) && first.Arrival == new DateTime(2010, 5, 3, 9, 5, 0) && first.Created == new DateTime(2010, 4, 1, 10, 2, 0),
        "Heures Access ramenées sur le jour du RDV");
    Check(first.Surname == "MARTIN" && first.Phones.Contains("06 12 00 00 01") && first.Emails.Contains("p@example.fr") && !first.Cancelled, "Champs de la ligne lus");
    agendaReader.Read();
    Check(rowReader.Row() == null, "Ligne sans patient ignorée");
}

// ---------- Carte Vitale ----------
var csvText = "\uFEFFPAT_ID;NOM;NOM_NAISSANCE;PRENOM;NAISSANCE;CARTE;QUALITE;ACTIF;CREATION;FIN_VALIDITE\n"
    + "501;DUPONT;;Marie;1970-02-03;-77123;0;1;2015-01-01 10:00:00.000;\n"
    + "502;DUPONT;;Lucas Jean;2010-05-06;-77123;6;1;2015-01-01 10:00:00.000;\n"
    + "503;\"DUPONT, épouse\";BERNARD;Anne;1972-08-09;-77123;2;1;2015-01-01;\n"
    + "504;MARTIN;;Paul;1950-01-01;9988;0;0;2001-01-01;2010-01-01\n"
    + "505;MARTIN;;Jeanne;1952-02-02;9988;5;1;2001-01-01;\n"
    + "506;INCONNU;;Zoé;1990-01-01;5555;0;1;2020-01-01;\n"
    + "507;JUMEAU;;Tom;2005-03-03;4444;6;1;2020-01-01;\n";
var vitaleRows = VitaleLinks.ReadCsv(new StringReader(csvText));
Check(vitaleRows.Count == 7 && vitaleRows[2].Surname == "DUPONT, épouse" && vitaleRows[3].Active == false && vitaleRows[3].ValidUntil == new DateTime(2010, 1, 1)
    && vitaleRows[0].Created == new DateTime(2015, 1, 1, 10, 0, 0) && vitaleRows[0].Quality == 0, "Export Vitale lu (séparateur, guillemets, dates, booléens)");
var identities = new List<AgendaIdentity>
{
    new(801, "DUPONT", "", "Marie", new DateTime(1970, 2, 3)), new(802, "Dupont", "", "Lucas", new DateTime(2010, 5, 6)),
    new(803, "DUPONT", "BERNARD", "Anne-Sophie", new DateTime(1972, 8, 9)), new(804, "MARTIN", "", "Paul", new DateTime(1950, 1, 1)),
    new(805, "MARTIN", "", "Jeanne", new DateTime(1952, 2, 2)), new(807, "JUMEAU", "", "Tom", new DateTime(2005, 3, 3)),
    new(808, "JUMEAU", "", "Tom", new DateTime(2005, 3, 3))
};
var vmatch = VitaleLinks.Match(vitaleRows, identities);
Check(vmatch.CodeByVitaleId[501] == 801 && vmatch.CodeByVitaleId[502] == 802 && vmatch.CodeByVitaleId[503] == 803, "Rapprochement par nom (ou nom de naissance), premier prénom et naissance");
Check(vmatch.Matched == 5 && vmatch.NotFound == 1 && vmatch.Ambiguous == 1 && !vmatch.CodeByVitaleId.ContainsKey(507), "Inconnu non trouvé, homonymes ambigus non rapprochés");
// Nom d'usage, liste des prénoms, sexe.
var extended = VitaleLinks.ReadCsv(new StringReader("PAT_ID;NOM;NOM_NAISSANCE;NOM_USUEL;PRENOM;PRENOMS;SEXE;NAISSANCE;CARTE;QUALITE;ACTIF;CREATION;FIN_VALIDITE\n"
    + "601;LEROY;LEROY;MARCHAL;Marie;Marie Thérèse;2;1960-04-05;77;0;1;2010-01-01;\n"
    + "602;PETIT;;;Jean;Jean Claude;1;1955-06-07;88;0;1;2010-01-01;\n"
    + "603;GEMEAU;;;Camille;Camille;2;2001-09-09;99;6;1;2010-01-01;\n"));
Check(extended[0].UsualName == "MARCHAL" && extended[0].AllFirstNames == "Marie Thérèse" && extended[0].Sex == "F" && extended[1].Sex == "M", "Colonnes facultatives lues");
var extMatch = VitaleLinks.Match(extended, new[]
{
    new AgendaIdentity(701, "MARCHAL", "", "Marie-Thérèse", new DateTime(1960, 4, 5)),        // nom d'épouse côté agenda
    new AgendaIdentity(702, "PETIT", "", "Claude", new DateTime(1955, 6, 7)),                  // second prénom d'usage
    new AgendaIdentity(703, "GEMEAU", "", "Camille", new DateTime(2001, 9, 9)) { Sex = "F" },  // jumeaux homonymes
    new AgendaIdentity(704, "GEMEAU", "", "Camille", new DateTime(2001, 9, 9)) { Sex = "M" }
});
Check(extMatch.CodeByVitaleId.GetValueOrDefault(601) == 701 && extMatch.CodeByVitaleId.GetValueOrDefault(602) == 702, "Nom d'usage et second prénom : rapprochés");
Check(extMatch.CodeByVitaleId.GetValueOrDefault(603) == 703, "Homonymes départagés par le sexe");
// Dernière consultation : homonymes départagés, prénom écrit autrement retrouvé, rien sans RDV ce jour-là.
var consultRows = VitaleLinks.ReadCsv(new StringReader("PAT_ID;NOM;NOM_NAISSANCE;PRENOM;NAISSANCE;CARTE;QUALITE;ACTIF;CREATION;FIN_VALIDITE;DERNIERE_CONSULT\n"
    + "611;HOMO;;Luc;1980-01-01;1;0;1;2010-01-01;;2024-03-12 00:00:00.000\n"
    + "612;DUBOIS;;Jeannine;1950-02-02;2;0;1;2010-01-01;;2023-06-01\n"
    + "613;AUTRE;;Paul;1950-02-02;3;0;1;2010-01-01;;2022-01-01\n"));
var visits = new HashSet<(int, DateTime)> { (812, new DateTime(2024, 3, 12)), (813, new DateTime(2023, 6, 1)) };
var consultMatch = VitaleLinks.Match(consultRows, new[]
{
    new AgendaIdentity(811, "HOMO", "", "Luc", new DateTime(1980, 1, 1)), new AgendaIdentity(812, "HOMO", "", "Luc", new DateTime(1980, 1, 1)),
    new AgendaIdentity(813, "DUBOIS", "", "Jeanine", new DateTime(1950, 2, 2)),   // prénom écrit autrement
    new AgendaIdentity(814, "DURAND", "", "Pierre", new DateTime(1950, 2, 2))      // même naissance, rien d'autre
}, (code, day) => visits.Contains((code, day)));
Check(consultRows[0].LastConsultation == new DateTime(2024, 3, 12) && consultMatch.CodeByVitaleId.GetValueOrDefault(611) == 812, "Homonymes départagés par la dernière consultation");
Check(consultMatch.CodeByVitaleId.GetValueOrDefault(612) == 813 && consultMatch.MatchedByConsultation == 2, "Prénom écrit autrement : retrouvé par le nom et la dernière consultation");
Check(!consultMatch.CodeByVitaleId.ContainsKey(613), "Ni nom ni prénom concordant : jamais rapproché, même avec une date");

Check(VitaleLinks.Sex("Monsieur") == "M" && VitaleLinks.Sex("Mme") == "F" && VitaleLinks.Sex("Enfant") == "", "Sexe d'après la civilité");

var declaredLinks = VitaleLinks.Links(vitaleRows, vmatch, new DateTime(2026, 11, 24));
DeclaredLink D(int a, int b) => declaredLinks.Single(l => l.A == Math.Min(a, b) && l.B == Math.Max(a, b));
Check(D(801, 802) is { Relation: "parent et enfant", Current: true } && D(801, 803).Relation == "conjoints" && D(802, 803).Relation == "parent et enfant", "Nature du lien d'après la qualité");
Check(D(804, 805) is { Current: false, Separated: false } && D(804, 805).Relation == "conjoints séparés, divorcés ou veufs", "Rattachement terminé : pas en cours");
var adult = new VitaleRow(1, "A", "", "Enfant", new DateTime(1990, 1, 1), "c", 6, true, null, null);
var minor = adult with { BirthDate = new DateTime(2012, 1, 1) };
var parentRow = new VitaleRow(2, "A", "", "Parent", new DateTime(1965, 1, 1), "c", 0, true, null, null);
var today24 = new DateTime(2026, 11, 24);
Check(VitaleLinks.PresumedHousehold("parent et enfant", parentRow, minor, today24) && !VitaleLinks.PresumedHousehold("parent et enfant", parentRow, adult, today24)
    && VitaleLinks.PresumedHousehold("conjoints", parentRow, adult, today24) && !VitaleLinks.PresumedHousehold("enfants de la même carte", minor, adult, today24),
    "Enfant de 20 ans ou plus encore actif sur la carte : pas présumé du même foyer");
Check(VitaleLinks.Age(new DateTime(2006, 11, 25), today24) == 19 && VitaleLinks.Age(new DateTime(2006, 11, 24), today24) == 20, "Âge exact");
Check(VitaleLinks.Relation(6, 6) == "enfants de la même carte" && VitaleLinks.Relation(0, 9) == "même carte Vitale", "Autres qualités");

// Effet sur les familles : lien Vitale en cours sans aucun indice d'agenda → même foyer ; conjoints séparés → jamais.
var vs = new FamilyScanner { Today = new DateTime(2026, 11, 24) };
vs.Add(new ScanRow(801, new DateTime(2025, 1, 10), new DateTime(2025, 1, 10, 9, 0, 0), null, null, "", "DUPONT", "", "Marie", "1 rue V", "Metz", new[] { "0605000801" }, Array.Empty<string>(), false));
vs.Add(new ScanRow(802, new DateTime(2025, 6, 10), new DateTime(2025, 6, 10, 15, 0, 0), null, null, "", "DUPONT", "", "Lucas", "", "", new[] { "0605000802" }, Array.Empty<string>(), false));
vs.Add(new ScanRow(804, new DateTime(2026, 3, 1), new DateTime(2026, 3, 1, 9, 0, 0), null, null, "", "MARTIN", "", "Paul", "2 rue W", "Metz", new[] { "0387000804" }, Array.Empty<string>(), false));
vs.Add(new ScanRow(805, new DateTime(2026, 3, 1), new DateTime(2026, 3, 1, 9, 15, 0), null, null, "", "MARTIN", "", "Jeanne", "2 rue W", "Metz", new[] { "0387000804" }, Array.Empty<string>(), false));
vs.AddDeclared(declaredLinks.Concat(new[] { new DeclaredLink(804, 805, "conjoints séparés, divorcés ou veufs", true, true, null) }).Where(l => l.A != 804 || l.Separated));
var vres = vs.Finish();
vres.Vitale = vmatch;
PatientLink VL(int a, int b) => vres.Links.Single(l => l.A == Math.Min(a, b) && l.B == Math.Max(a, b));
Check(VL(801, 802) is { Strength: LinkStrength.Forte, SameHouseholdNow: true, AgendaHouseholdNow: false } && VL(801, 802).Reasons.StartsWith("carte Vitale : parent et enfant"), "Vitale en cours : même foyer, trouvé grâce à Vitale");
Check(VL(804, 805) is { SameHouseholdNow: false, AgendaHouseholdNow: true, VitaleSeparated: true }, "Conjoints séparés : plus regroupés, même si l'agenda les rapprochait");
var olds = new FamilyScanner { Today = today24 };
olds.Add(new ScanRow(901, new DateTime(2025, 1, 10), new DateTime(2025, 1, 10, 9, 0, 0), null, null, "", "OLD", "", "Parent", "1 rue P", "Metz", new[] { "0606000901" }, Array.Empty<string>(), false));
olds.Add(new ScanRow(902, new DateTime(2025, 6, 10), new DateTime(2025, 6, 10, 15, 0, 0), null, null, "", "OLD", "", "Fils", "9 rue Q", "Nancy", new[] { "0606000902" }, Array.Empty<string>(), false));
olds.AddDeclared(new[] { new DeclaredLink(901, 902, "parent et enfant", true, false, null, Household: false) });
var oldLink = olds.Finish().Links.Single();
Check(oldLink is { Strength: LinkStrength.Forte, SameHouseholdNow: false } && oldLink.Reasons.Contains("enfant de 20 ans ou plus"), "Enfant adulte encore actif sur la carte, rien d'autre en commun : lié mais pas même foyer");
Check(vres.DeclaredLinks == 2 && vres.DeclaredLinksWithoutAgenda == 2, "Liens vers des patients sans RDV ignorés");
var vreport = FamilyScanReport.Anonymous(vres);
Check(vreport.Contains("5 rapprochés d'un Code Patient") && vreport.Contains("nouveaux grâce à Vitale : 1") && vreport.Contains("regroupés à tort par l'agenda seul : 1")
    && !vreport.Contains("DUPONT") && !vreport.Contains("-77123"), "Rapport Vitale anonymisé");

// Page de vérification : autonome (aucune ressource externe), données intégrées, échantillon.
var page = FamilyVerificationPage.Build(result, sampleSize: 4);
Check(page.Contains("const FAMILLES = [{") && !page.Contains("http://") && !page.Contains("https://") && !page.Contains("src=") && page.Contains("default-src 'none'"),
    "Page de vérification autonome, sans ressource externe");
Check(!page.Contains("1550357463012") && page.Split("</script>").Length == 2, "Pas de NoSS ; une seule balise script fermante (données échappées)");
Check(FamilyVerificationPage.Sample(result, 4).Count == Math.Min(4, result.Families.Count), "Échantillon de la taille demandée");
var pagePath = Environment.GetEnvironmentVariable("FAMILYSCAN_PAGE");
if (!string.IsNullOrEmpty(pagePath)) File.WriteAllText(pagePath, page);

Console.WriteLine($"{checks} vérifications réussies.");
