using System.Globalization;
using EyeChat.Families;
using EyeChat.SmsMail;
using Microsoft.Data.Sqlite;

namespace ChatServeur.Families;

public sealed record StoredFamilyMember(int PatientId, string Name, DateTime LastSeen);
public sealed record StoredFamilyLink(int A, int B, string Strength, string Reasons);
public sealed record StoredKinship(int PatientId, int OtherId, int? OtherFamily, string Reasons);

public sealed class StoredFamily
{
    public int Number { get; init; }
    public bool ToReview { get; init; }
    public bool Validated { get; init; }
    public bool HasChain { get; init; }
    public string Signature { get; init; } = "";
    public List<StoredFamilyMember> Members { get; } = new();
    public List<StoredFamilyLink> Links { get; } = new();
    public string NameOf(int id) => Members.FirstOrDefault(m => m.PatientId == id)?.Name is { Length: > 0 } n ? n : id.ToString(CultureInfo.InvariantCulture);
    /// <summary>Pourquoi la famille est à vérifier, en clair.</summary>
    public string ReviewReason => !ToReview ? "" : string.Join(", ", new[]
    {
        HasChain ? "certains membres ne sont reliés que par l'intermédiaire d'un autre" : null,
        Members.Count > 6 ? $"{Members.Count} personnes" : null,
        Links.Any(l => l.Strength == nameof(LinkStrength.Faible)) ? "un lien faible" : null,
    }.Where(x => x != null));
}

internal delegate string? LinkStrengthOf(int a, int b);

public sealed record PaymentBatch(int Id, DateTime ImportedUtc, string FileName, int Lines, DateTime? First, DateTime? Last);

/// <summary>
/// Base des familles (data/families.db) : photo de la carte Vitale, exports des paiements (le NIR n'y est jamais : chaque
/// carte devient un simple numéro de groupe propre à l'export), décisions prises à la main et dernier calcul.
/// </summary>
public sealed class FamilyStore
{
    private readonly string _connectionString;
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public FamilyStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var c = Open();
        Exec(c, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS vitale_row (patient_id INTEGER, surname TEXT, birth_name TEXT, usual_name TEXT, first_name TEXT,
                all_first_names TEXT, sex TEXT, birth TEXT, card TEXT, quality INTEGER, active INTEGER, created TEXT, valid_until TEXT, last_consultation TEXT);
            CREATE TABLE IF NOT EXISTS payment_batch (id INTEGER PRIMARY KEY AUTOINCREMENT, imported_utc TEXT NOT NULL, file_name TEXT NOT NULL,
                lines INTEGER NOT NULL, first_date TEXT, last_date TEXT);
            CREATE TABLE IF NOT EXISTS payment_row (batch INTEGER NOT NULL, payer_id TEXT, payer_surname TEXT, payer_first TEXT,
                benef_surname TEXT, benef_first TEXT, invoice_date TEXT, card_group INTEGER);
            CREATE INDEX IF NOT EXISTS payment_row_batch ON payment_row(batch);
            CREATE TABLE IF NOT EXISTS decision (id INTEGER PRIMARY KEY AUTOINCREMENT, kind INTEGER NOT NULL, a INTEGER NOT NULL, b INTEGER NOT NULL,
                members TEXT NOT NULL DEFAULT '', created_utc TEXT NOT NULL, author TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS family (number INTEGER PRIMARY KEY, to_review INTEGER NOT NULL, validated INTEGER NOT NULL,
                has_chain INTEGER NOT NULL, signature TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS family_member (patient_id INTEGER PRIMARY KEY, number INTEGER NOT NULL, name TEXT NOT NULL, last_seen TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS family_member_number ON family_member(number);
            CREATE TABLE IF NOT EXISTS family_link (number INTEGER NOT NULL, a INTEGER NOT NULL, b INTEGER NOT NULL, strength TEXT NOT NULL, reasons TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS family_link_number ON family_link(number);
            CREATE TABLE IF NOT EXISTS family_kin (patient_id INTEGER NOT NULL, other_id INTEGER NOT NULL, reasons TEXT NOT NULL);
            """);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, SqliteTransaction? t = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    private static DateTime? P(string? s) => DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "O" }, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    // ---------- Meta ----------

    public string? Meta(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var c = Open();
        SetMeta(c, null, key, value);
    }

    private static void SetMeta(SqliteConnection c, SqliteTransaction? t, string key, string value)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = "INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // ---------- Photo de la carte Vitale (base AXI_DATABASE, plus utilisée : importée une fois) ----------

    public async Task<int> ReplaceVitaleAsync(IReadOnlyList<VitaleRow> rows, string fileName)
    {
        await WriteGate.WaitAsync();
        try
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            Exec(c, "DELETE FROM vitale_row", t);
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;
            cmd.CommandText = """
                INSERT INTO vitale_row VALUES ($id, $s, $bn, $un, $f, $af, $sex, $birth, $card, $q, $active, $created, $until, $last)
                """;
            var names = new[] { "$id", "$s", "$bn", "$un", "$f", "$af", "$sex", "$birth", "$card", "$q", "$active", "$created", "$until", "$last" };
            var p = names.Select(n => cmd.Parameters.Add(n, SqliteType.Text)).ToArray();
            foreach (var r in rows)
            {
                object?[] v = { r.PatientId, r.Surname, r.BirthName, r.UsualName, r.FirstName, r.AllFirstNames, r.Sex, D(r.BirthDate), r.Card,
                    r.Quality, r.Active switch { true => 1, false => 0, null => null }, D(r.Created), D(r.ValidUntil), D(r.LastConsultation) };
                for (int i = 0; i < p.Length; i++) p[i].Value = v[i] ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
            SetMeta(c, t, "vitale_file", fileName);
            SetMeta(c, t, "vitale_imported_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            t.Commit();
            return rows.Count;
        }
        finally { WriteGate.Release(); }
    }

    public List<VitaleRow> LoadVitale()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM vitale_row";
        using var r = cmd.ExecuteReader();
        var rows = new List<VitaleRow>();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        while (r.Read())
            rows.Add(new VitaleRow(r.GetInt32(0), S(1), S(2), S(4), P(S(7)), S(8), r.IsDBNull(9) ? null : r.GetInt32(9),
                r.IsDBNull(10) ? null : r.GetInt32(10) == 1, P(S(11)), P(S(12)))
            { UsualName = S(3), AllFirstNames = S(5), Sex = S(6), LastConsultation = P(S(13)) });
        return rows;
    }

    public int VitaleCount()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vitale_row";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------- Exports des paiements (CGM e-Vitale), ajoutés de temps en temps ----------

    /// <summary>Ajoute un export. Chaque NIR (déjà réduit à une empreinte en mémoire) devient un numéro de groupe : il n'est pas gardé.</summary>
    public async Task<PaymentBatch> AddPaymentsAsync(IReadOnlyList<PaymentRow> rows, string fileName)
    {
        await WriteGate.WaitAsync();
        try
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            var dates = rows.Where(r => r.InvoiceDate.HasValue).Select(r => r.InvoiceDate!.Value).ToList();
            DateTime? first = dates.Count > 0 ? dates.Min() : null, last = dates.Count > 0 ? dates.Max() : null;
            var now = DateTime.UtcNow;
            long id;
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = "INSERT INTO payment_batch(imported_utc, file_name, lines, first_date, last_date) VALUES($u, $f, $l, $a, $b); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$u", now.ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$f", fileName);
                cmd.Parameters.AddWithValue("$l", rows.Count);
                cmd.Parameters.AddWithValue("$a", D(first));
                cmd.Parameters.AddWithValue("$b", D(last));
                id = (long)cmd.ExecuteScalar()!;
            }
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = "INSERT INTO payment_row VALUES($batch, $pid, $ps, $pf, $bs, $bf, $d, $g)";
                var p = new[] { "$batch", "$pid", "$ps", "$pf", "$bs", "$bf", "$d", "$g" }.Select(n => cmd.Parameters.Add(n, SqliteType.Text)).ToArray();
                foreach (var r in rows)
                {
                    object? group = r.Card.Length == 0 ? null : groups.TryGetValue(r.Card, out var g) ? g : groups[r.Card] = groups.Count + 1;
                    object?[] v = { id, r.PayerId, r.PayerSurname, r.PayerFirstName, r.BeneficiarySurname, r.BeneficiaryFirstName, D(r.InvoiceDate), group };
                    for (int i = 0; i < p.Length; i++) p[i].Value = v[i] ?? DBNull.Value;
                    cmd.ExecuteNonQuery();
                }
            }
            t.Commit();
            return new PaymentBatch((int)id, now, fileName, rows.Count, first, last);
        }
        finally { WriteGate.Release(); }
    }

    public List<PaymentBatch> PaymentBatches()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, imported_utc, file_name, lines, first_date, last_date FROM payment_batch ORDER BY id DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<PaymentBatch>();
        while (r.Read())
            list.Add(new PaymentBatch(r.GetInt32(0), P(r.GetString(1)) ?? DateTime.MinValue, r.GetString(2), r.GetInt32(3),
                P(r.IsDBNull(4) ? null : r.GetString(4)), P(r.IsDBNull(5) ? null : r.GetString(5))));
        return list;
    }

    public async Task DeletePaymentBatchAsync(int id)
    {
        await WriteGate.WaitAsync();
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM payment_row WHERE batch = $id; DELETE FROM payment_batch WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        finally { WriteGate.Release(); }
    }

    public List<PaymentRow> LoadPayments()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT batch, payer_id, payer_surname, payer_first, benef_surname, benef_first, invoice_date, card_group FROM payment_row";
        using var r = cmd.ExecuteReader();
        var rows = new List<PaymentRow>();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        while (r.Read())
        {
            var batch = r.GetInt32(0).ToString(CultureInfo.InvariantCulture);
            rows.Add(new PaymentRow(S(1), S(2), S(3), S(4), S(5), P(S(6)))
            {
                Batch = batch,
                Card = r.IsDBNull(7) ? "" : batch + ":" + r.GetInt32(7).ToString(CultureInfo.InvariantCulture)
            });
        }
        return rows;
    }

    // ---------- Décisions prises à la main ----------

    public async Task AddDecisionAsync(FamilyDecision decision, string author)
    {
        await WriteGate.WaitAsync();
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO decision(kind, a, b, members, created_utc, author) VALUES($k, $a, $b, $m, $u, $w)";
            cmd.Parameters.AddWithValue("$k", (int)decision.Kind);
            cmd.Parameters.AddWithValue("$a", Math.Min(decision.A, decision.B));
            cmd.Parameters.AddWithValue("$b", Math.Max(decision.A, decision.B));
            cmd.Parameters.AddWithValue("$m", decision.Members);
            cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$w", author);
            cmd.ExecuteNonQuery();
        }
        finally { WriteGate.Release(); }
    }

    /// <summary>Décisions dans l'ordre : pour une même paire, la plus récente l'emporte (séparer puis regrouper = regroupés).</summary>
    public List<FamilyDecision> LoadDecisions()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT kind, a, b, members FROM decision ORDER BY id";
        using var r = cmd.ExecuteReader();
        var all = new List<FamilyDecision>();
        while (r.Read()) all.Add(new FamilyDecision((FamilyDecisionKind)r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3)));
        var latestPair = new Dictionary<(int, int), FamilyDecision>();
        foreach (var d in all.Where(d => d.Kind != FamilyDecisionKind.Validate)) latestPair[(d.A, d.B)] = d;
        return latestPair.Values.Concat(all.Where(d => d.Kind == FamilyDecisionKind.Validate)).ToList();
    }

    /// <summary>Réponse d'un poste à « Famille à vérifier » : chaque paire de personnes présentes est notée ensemble ou séparée.</summary>
    public async Task<int> RecordAnswerAsync(IReadOnlyList<int> patientIds, bool together, string author)
    {
        var ids = patientIds.Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
        if (ids.Count < 2 || ids.Count > 50) return 0;
        int count = 0;
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                await AddDecisionAsync(new FamilyDecision(together ? FamilyDecisionKind.Join : FamilyDecisionKind.Separate, ids[i], ids[j]), author);
                count++;
            }
        return count;
    }

    public int DecisionCount()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM decision";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------- Résultat du dernier calcul ----------

    public async Task SaveResultAsync(ScanResult result, string anonymousReport)
    {
        await WriteGate.WaitAsync();
        try
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            Exec(c, "DELETE FROM family; DELETE FROM family_member; DELETE FROM family_link; DELETE FROM family_kin;", t);
            using var fam = c.CreateCommand(); fam.Transaction = t;
            fam.CommandText = "INSERT INTO family VALUES($n, $r, $v, $c, $s)";
            var fp = new[] { "$n", "$r", "$v", "$c", "$s" }.Select(n => fam.Parameters.Add(n, SqliteType.Text)).ToArray();
            using var mem = c.CreateCommand(); mem.Transaction = t;
            mem.CommandText = "INSERT INTO family_member VALUES($p, $n, $name, $seen)";
            var mp = new[] { "$p", "$n", "$name", "$seen" }.Select(n => mem.Parameters.Add(n, SqliteType.Text)).ToArray();
            using var lnk = c.CreateCommand(); lnk.Transaction = t;
            lnk.CommandText = "INSERT INTO family_link VALUES($n, $a, $b, $s, $r)";
            var lp = new[] { "$n", "$a", "$b", "$s", "$r" }.Select(n => lnk.Parameters.Add(n, SqliteType.Text)).ToArray();

            var numberOf = new Dictionary<int, int>();
            foreach (var f in result.Families)
            {
                object[] fv = { f.Number, f.ToReview ? 1 : 0, f.Validated ? 1 : 0, f.HasChain ? 1 : 0, FamilyDecision.Signature(f.Members) };
                for (int i = 0; i < fp.Length; i++) fp[i].Value = fv[i];
                fam.ExecuteNonQuery();
                foreach (var id in f.Members)
                {
                    numberOf[id] = f.Number;
                    var p = result.PatientsById[id];
                    object[] mv = { id, f.Number, p.Name, D(p.LastSeen) };
                    for (int i = 0; i < mp.Length; i++) mp[i].Value = mv[i];
                    mem.ExecuteNonQuery();
                }
            }
            foreach (var l in result.Links.Where(l => l.SameHouseholdNow && numberOf.TryGetValue(l.A, out var n) && numberOf.TryGetValue(l.B, out var m) && n == m))
            {
                object[] lv = { numberOf[l.A], l.A, l.B, l.Strength.ToString(), l.Reasons };
                for (int i = 0; i < lp.Length; i++) lp[i].Value = lv[i];
                lnk.ExecuteNonQuery();
            }
            using var kin = c.CreateCommand(); kin.Transaction = t;
            kin.CommandText = "INSERT INTO family_kin VALUES($p, $o, $r)";
            var kp = new[] { "$p", "$o", "$r" }.Select(n => kin.Parameters.Add(n, SqliteType.Text)).ToArray();
            foreach (var l in result.Links.Where(l => l.OtherHousehold))
                foreach (var (p, o) in new[] { (l.A, l.B), (l.B, l.A) })
                {
                    kp[0].Value = p; kp[1].Value = o; kp[2].Value = l.Reasons;
                    kin.ExecuteNonQuery();
                }
            SetMeta(c, t, "last_run_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            SetMeta(c, t, "last_report", anonymousReport);
            t.Commit();
        }
        finally { WriteGate.Release(); }
    }

    public int FamilyCount(bool toReviewOnly = false)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = toReviewOnly ? "SELECT COUNT(*) FROM family WHERE to_review = 1" : "SELECT COUNT(*) FROM family";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Familles complètes (membres et liens) : à vérifier, d'un numéro, ou contenant un patient.</summary>
    public List<StoredFamily> Families(bool toReviewOnly = false, int? number = null, int? patientId = null, int limit = 200)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT number, to_review, validated, has_chain, signature FROM family WHERE 1=1"
            + (toReviewOnly ? " AND to_review = 1" : "")
            + (number.HasValue ? " AND number = $n" : "")
            + (patientId.HasValue ? " AND number IN (SELECT number FROM family_member WHERE patient_id = $p)" : "")
            + " ORDER BY number LIMIT $limit";
        if (number.HasValue) cmd.Parameters.AddWithValue("$n", number.Value);
        if (patientId.HasValue) cmd.Parameters.AddWithValue("$p", patientId.Value);
        cmd.Parameters.AddWithValue("$limit", limit);
        var families = new Dictionary<int, StoredFamily>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                families[r.GetInt32(0)] = new StoredFamily { Number = r.GetInt32(0), ToReview = r.GetInt32(1) == 1, Validated = r.GetInt32(2) == 1, HasChain = r.GetInt32(3) == 1, Signature = r.GetString(4) };
        if (families.Count == 0) return new();
        var list = string.Join(",", families.Keys);
        using (var m = c.CreateCommand())
        {
            m.CommandText = $"SELECT number, patient_id, name, last_seen FROM family_member WHERE number IN ({list}) ORDER BY patient_id";
            using var r = m.ExecuteReader();
            while (r.Read()) families[r.GetInt32(0)].Members.Add(new StoredFamilyMember(r.GetInt32(1), r.GetString(2), P(r.GetString(3)) ?? DateTime.MinValue));
        }
        using (var l = c.CreateCommand())
        {
            l.CommandText = $"SELECT number, a, b, strength, reasons FROM family_link WHERE number IN ({list})";
            using var r = l.ExecuteReader();
            while (r.Read()) families[r.GetInt32(0)].Links.Add(new StoredFamilyLink(r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
        }
        return families.Values.ToList();
    }

    public List<StoredKinship> Kinship(int patientId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT k.patient_id, k.other_id, m.number, k.reasons FROM family_kin k LEFT JOIN family_member m ON m.patient_id = k.other_id WHERE k.patient_id = $p";
        cmd.Parameters.AddWithValue("$p", patientId);
        using var r = cmd.ExecuteReader();
        var list = new List<StoredKinship>();
        while (r.Read()) list.Add(new StoredKinship(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetString(3)));
        return list;
    }

    /// <summary>Pour l'envoi des rappels : la famille de chacun des patients demandés, avec le pourquoi.</summary>
    public List<FamilyMembershipDto> Memberships(IEnumerable<int> patientIds)
    {
        var ids = patientIds.Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0) return new();
        using var c = Open();
        var numbers = new Dictionary<int, int>();
        foreach (var chunk in ids.Chunk(500))
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT patient_id, number FROM family_member WHERE patient_id IN ({string.Join(",", chunk)})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) numbers[r.GetInt32(0)] = r.GetInt32(1);
        }
        // Réponses données aux postes (« Regrouper » / « Garder séparés ») : la plus récente pour chaque paire.
        var answers = LoadDecisions().Where(d => d.Kind != FamilyDecisionKind.Validate)
            .ToDictionary(d => (d.A, d.B), d => d.Kind == FamilyDecisionKind.Join);
        bool? Answer(int a, int b) => answers.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var together) ? together : null;
        var result = new List<FamilyMembershipDto>();
        foreach (var family in numbers.Values.Distinct().Chunk(200).SelectMany(chunk => FamiliesByNumbers(c, chunk)))
        {
            var why = string.Join(Environment.NewLine, family.Links.Select(l =>
                $"{Name(family, l.A)} et {Name(family, l.B)} : {l.Reasons}"));
            // Personnes « gardées séparées » : jamais dans le même groupe, même avant le prochain calcul.
            var groups = new List<List<int>>();
            foreach (var id in family.Members.Select(m => m.PatientId).Where(numbers.ContainsKey))
            {
                var group = groups.FirstOrDefault(g => g.All(other => Answer(id, other) != false));
                if (group == null) groups.Add(group = new List<int>());
                group.Add(id);
            }
            for (int g = 0; g < groups.Count; g++)
            {
                // Déjà confirmés ensemble au poste (toutes les paires présentes) : plus de question.
                var pairs = groups[g].SelectMany(a => groups[g].Where(b => b > a).Select(b => (a, b))).ToList();
                var confirmed = pairs.Count > 0 && pairs.All(p => Answer(p.a, p.b) == true);
                // La famille est à vérifier, mais ces personnes-là sont toutes reliées directement par un lien fort :
                // la chaîne douteuse passe par d'autres membres, absents ce jour (vérifié au cabinet : 4 questions sur 4 inutiles).
                LinkStrengthOf strengthOf = (a, b) => family.Links.FirstOrDefault(l => (l.A == a && l.B == b) || (l.A == b && l.B == a))?.Strength;
                var solid = pairs.Count > 0 && pairs.All(p => strengthOf(p.a, p.b) == nameof(LinkStrength.Forte));
                var toReview = family.ToReview && !confirmed && !solid;
                var reviewReason = !toReview ? ""
                    : pairs.Any(p => strengthOf(p.a, p.b) == null) ? "pas de lien direct entre ces personnes"
                    : "lien moyen ou faible entre ces personnes";
                var groupWhy = toReview ? "À vérifier (" + reviewReason + ")" + Environment.NewLine + why : why;
                foreach (var id in groups[g])
                    result.Add(new FamilyMembershipDto
                    {
                        PatientId = id, FamilyNumber = family.Number, SubGroup = g, ToReview = toReview, Why = groupWhy,
                        ReviewReason = reviewReason,
                        Members = family.Members.Select(m => m.PatientId).ToList(),
                        Links = family.Links.Select(l => new FamilyLinkDto { A = l.A, B = l.B, Strength = l.Strength, Reasons = l.Reasons }).ToList(),
                    });
            }
        }
        return result;

        static string Name(StoredFamily f, int id) => f.Members.FirstOrDefault(m => m.PatientId == id)?.Name is { Length: > 0 } n ? n : id.ToString(CultureInfo.InvariantCulture);
    }

    private IEnumerable<StoredFamily> FamiliesByNumbers(SqliteConnection _, int[] numbers)
        => numbers.SelectMany(n => Families(number: n, limit: 1));
}
