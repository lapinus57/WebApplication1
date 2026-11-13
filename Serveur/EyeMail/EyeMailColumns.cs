using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatServeur.EyeMail;

/// <summary>Un patient tel que lu dans Access, avant enregistrement dans eyemail.db.</summary>
public sealed record EyeMailSourcePatient(
    string AccessPatientId, string Civility, string LastName, string FirstName, string MaidenName, DateTime? BirthDate,
    string Address, string Address2, string PostalCode, string City,
    string Phone1, string Phone1Info, string Phone2, string Phone2Info, string Email, string EmailInfo);

/// <summary>
/// Retrouve les colonnes utiles de la table Patients d'Access d'après leur nom, sans réglage :
/// mêmes noms que ceux reconnus par l'onglet SMS et mail (« Code Patient », « Tel 1 », « E-mail »…).
/// La comparaison ignore la casse, les accents, les espaces et la ponctuation.
/// </summary>
public sealed class EyeMailColumns
{
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["Id"] = ["Code Patient", "CodePatient", "IDPatient", "Id Patient", "NumPatient", "Num Patient", "Code"],
        ["Civility"] = ["Titre", "Civilité", "Civilite"],
        ["LastName"] = ["Nom", "Nom Patient", "NomPatient"],
        ["FirstName"] = ["Prénom", "Prenom", "Prénoms", "Prenoms"],
        ["MaidenName"] = ["Nom jeune fille", "NomJeuneFille", "NomJF", "Nom de naissance"],
        ["BirthDate"] = ["Date de naissance", "DateNaissance", "Date naissance", "Naissance", "DDN"],
        ["Address"] = ["Adresse", "Adr", "Adresse 1", "Adresse1"],
        ["Address2"] = ["Adresse 2", "Adresse2", "Complément adresse", "Complement adresse", "ComplementAdresse"],
        ["PostalCode"] = ["Code postal", "CodePostal", "CP"],
        ["City"] = ["Ville", "Commune", "CP Ville", "CPVille"],
        ["Phone1"] = ["Tel 1", "Téléphone 1", "Telephone 1", "Tel1", "Téléphone", "Telephone", "Tel"],
        ["Phone1Info"] = ["Tel 1 Info", "Tel1 Info", "Tel1Info"],
        ["Phone2"] = ["Tel 2", "Téléphone 2", "Telephone 2", "Tel2", "Portable", "Mobile"],
        ["Phone2Info"] = ["Tel 2 Info", "Tel2 Info", "Tel2Info"],
        ["Email"] = ["E-mail", "Email", "E mail", "Mail", "Adresse mail", "AdresseMail", "Courriel"],
        ["EmailInfo"] = ["Email Info", "EmailInfo", "E-mail Info"],
    };

    private static readonly Regex PostalCodeAndCity = new(@"^\s*(\d{5})\s+(.+)$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, int> _ordinals;

    private EyeMailColumns(Dictionary<string, int> ordinals) => _ordinals = ordinals;

    /// <summary>Champs trouvés (champ EyeMail → nom de la colonne Access), pour l'affichage.</summary>
    public IReadOnlyDictionary<string, string> Found { get; private init; } = new Dictionary<string, string>();

    public static EyeMailColumns Detect(IReadOnlyList<string> columnNames)
    {
        var byKey = new Dictionary<string, int>();
        for (int i = 0; i < columnNames.Count; i++)
            byKey.TryAdd(Fold(columnNames[i]), i);

        var ordinals = new Dictionary<string, int>();
        var found = new Dictionary<string, string>();
        foreach (var (field, aliases) in Aliases)
        {
            foreach (var alias in aliases)
            {
                if (!byKey.TryGetValue(Fold(alias), out var index) || ordinals.ContainsValue(index))
                    continue;
                ordinals[field] = index;
                found[field] = columnNames[index];
                break;
            }
        }

        if (!ordinals.ContainsKey("Id") || !ordinals.ContainsKey("LastName"))
            throw new InvalidDataException(
                "La table Patients d'Access n'a pas de colonne « Code Patient » et « Nom » reconnaissable. Colonnes trouvées : "
                + string.Join(", ", columnNames));
        return new EyeMailColumns(ordinals) { Found = found };
    }

    /// <summary>Convertit une ligne Access (valeurs dans l'ordre des colonnes) ; null si l'identifiant est vide.</summary>
    public EyeMailSourcePatient? Map(IReadOnlyList<object?> values, DateTime today)
    {
        string Text(string field)
        {
            if (!_ordinals.TryGetValue(field, out var i)) return "";
            var value = values[i];
            if (value is null or DBNull) return "";
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
            return text.Length > 500 ? text[..500] : text;
        }

        var id = Text("Id");
        if (id.Length == 0)
            return null;

        DateTime? birth = null;
        if (_ordinals.TryGetValue("BirthDate", out var b))
        {
            var raw = values[b];
            if (raw is DateTime date) birth = date.Date;
            else if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var parsed)) birth = parsed.Date;
            if (birth > today || birth < new DateTime(1880, 1, 1)) birth = null;
        }

        var postalCode = Text("PostalCode");
        var city = Text("City");
        // Une seule colonne « CP Ville » : « 57000 Metz » est séparé en code postal et ville.
        if (postalCode.Length == 0 && PostalCodeAndCity.Match(city) is { Success: true } m)
        {
            postalCode = m.Groups[1].Value;
            city = m.Groups[2].Value.Trim();
        }

        return new EyeMailSourcePatient(id, Text("Civility"), Text("LastName"), Text("FirstName"), Text("MaidenName"), birth,
            Text("Address"), Text("Address2"), postalCode, city,
            Text("Phone1"), Text("Phone1Info"), Text("Phone2"), Text("Phone2Info"), Text("Email"), Text("EmailInfo"));
    }

    /// <summary>Majuscules, sans accents, sans espaces ni ponctuation : « Prénom » → « PRENOM ».</summary>
    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }

    /// <summary>Texte de recherche : mots en majuscules sans accents, séparés par une espace.</summary>
    public static string SearchText(params string[] parts)
        => string.Join(" ", parts.SelectMany(p => p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Select(Fold).Where(w => w.Length > 0));
}
