using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace EyeChat.Families
{
    /// <summary>
    /// Page de vérification des familles (HTML autonome, sans Internet), à ouvrir au cabinet dans le navigateur.
    /// Nominative : ne pas diffuser. Les vérifications sont gardées dans le navigateur ; l'export « bilan anonyme »
    /// ne contient ni nom, ni code patient, ni coordonnée.
    /// </summary>
    public static class FamilyVerificationPage
    {
        public const int SampleSize = 40;

        /// <summary>Échantillon varié et reproductible : grandes familles et chaînes, Vitale, agenda seul, couples.</summary>
        public static HashSet<int> Sample(ScanResult r, int size = SampleSize)
        {
            var random = new Random(r.Rows);
            var linksByPatient = r.Links.Where(l => l.SameHouseholdNow).SelectMany(l => new[] { (l.A, l), (l.B, l) }).ToLookup(x => x.Item1, x => x.l);
            bool HasVitale(ScanFamily f) => f.Members.Any(m => linksByPatient[m].Any(l => l.VitaleRelation != null));
            List<ScanFamily> Pick(IEnumerable<ScanFamily> source, int count) => source.OrderBy(_ => random.Next()).Take(count).ToList();
            var chosen = new List<ScanFamily>();
            chosen.AddRange(r.Families.Where(f => f.ToReview).OrderByDescending(f => f.Members.Count).Take(size / 8));
            chosen.AddRange(Pick(r.Families.Where(f => f.ToReview).Except(chosen), size / 4));
            chosen.AddRange(Pick(r.Families.Where(f => !f.ToReview && HasVitale(f)).Except(chosen), size / 4));
            chosen.AddRange(Pick(r.Families.Where(f => !f.ToReview && !HasVitale(f)).Except(chosen), size / 4));
            chosen.AddRange(Pick(r.Families.Except(chosen), size - chosen.Count));
            return chosen.Select(f => f.Number).ToHashSet();
        }

        public static string Build(ScanResult r, int sampleSize = SampleSize)
        {
            var sample = Sample(r, sampleSize);
            var linksByPatient = r.Links.Where(l => l.SameHouseholdNow).SelectMany(l => new[] { (l.A, l), (l.B, l) }).ToLookup(x => x.Item1, x => x.l);
            var families = r.Families.Select(f =>
            {
                var members = f.Members.ToHashSet();
                var links = f.Members.SelectMany(m => linksByPatient[m]).Distinct()
                    .Where(l => members.Contains(l.A) && members.Contains(l.B))
                    .OrderByDescending(l => l.Strength)
                    .Select(l => new object[] { l.A, l.B, (int)l.Strength, l.Reasons, l.VitaleRelation != null ? 1 : 0 });
                return new
                {
                    k = string.Join("-", f.Members.OrderBy(x => x)),
                    n = f.Number,
                    r = f.ToReview ? 1 : 0,
                    c = f.HasChain ? 1 : 0,
                    s = sample.Contains(f.Number) ? 1 : 0,
                    m = f.Members.Select(id => r.PatientsById[id]).Select(p => new object[]
                    {
                        p.Id, p.Name, p.LastSeen == DateTime.MinValue ? "jamais venu" : p.LastSeen.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        string.Join(" ", p.Phones), string.Join(" ", p.Emails)
                    }),
                    l = links
                };
            });
            // Le JSON est échappé (<, >, &) : il ne peut pas fermer la balise <script>.
            var json = JsonSerializer.Serialize(families);
            return Template.Replace("/*DONNEES*/[]", json)
                .Replace("{{GENERE}}", DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
                .Replace("{{ECHANTILLON}}", sample.Count.ToString(CultureInfo.InvariantCulture));
        }

        private const string Template = """
<!doctype html>
<html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'">
<title>Vérification des familles</title>
<style>
:root { --bg:#f4f7fb; --card:#fff; --text:#152238; --muted:#69778d; --line:#e2e8f0; --ok:#16a36a; --bad:#d94c4c; --warn:#c77700; --accent:#246bfd; }
@media (prefers-color-scheme: dark) { :root { --bg:#10161f; --card:#18212d; --text:#e6ebf2; --muted:#9aa6b8; --line:#2a3646; } }
* { box-sizing:border-box } body { margin:0; font:15px/1.45 system-ui,"Segoe UI",sans-serif; background:var(--bg); color:var(--text) }
header { position:sticky; top:0; z-index:2; background:var(--bg); padding:12px 16px; border-bottom:1px solid var(--line) }
h1 { font-size:20px; margin:0 0 4px } .warn { color:var(--bad); font-weight:600; font-size:13px }
.bar { display:flex; flex-wrap:wrap; gap:8px; align-items:center; margin-top:8px }
select, input[type=search], button, textarea { font:inherit; color:inherit; background:var(--card); border:1px solid var(--line); border-radius:8px; padding:6px 10px }
button { cursor:pointer } button.on { outline:2px solid var(--accent) }
main { max-width:1100px; margin:auto; padding:12px 16px 60px }
.card { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:14px; margin:12px 0 }
.card.done { border-left:5px solid var(--ok) } .card.issue { border-left:5px solid var(--bad) }
.tags span { display:inline-block; font-size:12px; padding:1px 8px; border-radius:10px; background:var(--line); margin-right:4px }
table { border-collapse:collapse; width:100%; margin:8px 0; font-size:14px } td, th { text-align:left; padding:4px 6px; border-bottom:1px solid var(--line); vertical-align:top }
th { color:var(--muted); font-weight:600 } .muted { color:var(--muted) } .links li { margin:4px 0 } .links label { cursor:pointer }
.verdicts { display:flex; flex-wrap:wrap; gap:6px; margin-top:8px } .progress { font-weight:600 }
textarea { width:100%; min-height:38px; margin-top:6px }
.scroll { overflow-x:auto } input[type=search] { flex:1 1 220px; min-width:0 }
</style></head><body>
<header>
  <h1>Vérification des familles</h1>
  <div class="warn">Fichier nominatif (noms, téléphones) : à garder au cabinet, ne pas envoyer. Généré le {{GENERE}}.</div>
  <div class="bar">
    <select id="filtre" aria-label="Familles affichées">
      <option value="echantillon">Échantillon à vérifier ({{ECHANTILLON}} familles)</option>
      <option value="revoir">Familles « à vérifier »</option>
      <option value="grandes">7 personnes ou plus</option>
      <option value="faites">Déjà vérifiées</option>
      <option value="toutes">Toutes les familles</option>
    </select>
    <input type="search" id="cherche" placeholder="Nom ou code patient (2 caractères au moins)" aria-label="Chercher une famille">
    <span class="progress" id="progression"></span>
    <button id="bilan">Exporter le bilan anonyme</button>
    <button id="decisions">Exporter mes décisions (codes patients)</button>
  </div>
</header>
<main id="liste"></main>
<script>
const FAMILLES = /*DONNEES*/[];
const VERDICTS = [["juste","✔ Famille juste"],["intrus","Membre(s) en trop"],["manque","Il manque quelqu'un"],["separer","Plusieurs foyers à séparer"],["sais-pas","Je ne sais pas"]];
const CLE = "familyscan-verifications";
let notes = {};
try { notes = JSON.parse(localStorage.getItem(CLE) || "{}"); } catch (e) { notes = {}; }
function sauver() { try { localStorage.setItem(CLE, JSON.stringify(notes)); } catch (e) {} afficherProgression(); }
const force = ["", "faible", "moyenne", "forte"];
let limite = 50;

function correspond(f, q) {
  q = q.toLowerCase();
  return f.m.some(p => String(p[0]) === q || p[1].toLowerCase().includes(q));
}
function choisies() {
  const filtre = document.getElementById("filtre").value, q = document.getElementById("cherche").value.trim();
  if (q.length >= 2) return FAMILLES.filter(f => correspond(f, q));
  return FAMILLES.filter(f => filtre === "echantillon" ? f.s : filtre === "revoir" ? f.r : filtre === "grandes" ? f.m.length >= 7
    : filtre === "faites" ? notes[f.k] : true);
}
function el(tag, attrs, ...enfants) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) { if (k === "class") e.className = v; else if (k.startsWith("on")) e.addEventListener(k.slice(2), v); else e.setAttribute(k, v); }
  for (const c of enfants) e.append(c instanceof Node ? c : document.createTextNode(c ?? ""));
  return e;
}
function carte(f) {
  const n = notes[f.k] || {};
  const nom = id => (f.m.find(p => p[0] === id) || [id, String(id)])[1];
  const c = el("article", { class: "card" + (n.v === "juste" ? " done" : n.v && n.v !== "sais-pas" ? " issue" : "") });
  c.append(el("div", { class: "tags" }, el("strong", {}, `Famille n° ${f.n} — ${f.m.length} personnes `),
    ...(f.r ? [el("span", {}, "à vérifier")] : []), ...(f.c ? [el("span", {}, "chaîne")] : []), ...(f.s ? [el("span", {}, "échantillon")] : [])));
  const t = el("table", {}, el("tr", {}, el("th", {}, "Code"), el("th", {}, "Nom"), el("th", {}, "Dernière venue"), el("th", {}, "Téléphones"), el("th", {}, "Mails")));
  for (const p of f.m) t.append(el("tr", {}, el("td", {}, String(p[0])), el("td", {}, p[1]), el("td", {}, p[2]), el("td", {}, p[3]), el("td", {}, p[4])));
  c.append(el("div", { class: "scroll" }, t));
  const ul = el("ul", { class: "links" });
  for (const l of f.l) {
    const id = l[0] + "-" + l[1], coche = el("input", { type: "checkbox" });
    coche.checked = (n.faux || []).includes(id);
    coche.addEventListener("change", () => { const x = notes[f.k] || (notes[f.k] = {}); x.faux = (x.faux || []).filter(v => v !== id); if (coche.checked) x.faux.push(id); x.t = Date.now(); sauver(); });
    ul.append(el("li", {}, el("label", {}, coche, ` lien faux — `), el("strong", {}, `${nom(l[0])} ↔ ${nom(l[1])}`), el("span", { class: "muted" }, ` (${force[l[2]]}) : ${l[3]}`)));
  }
  c.append(el("div", { class: "muted" }, "Liens dans la famille (cochez ceux qui sont faux) :"), ul);
  const boutons = el("div", { class: "verdicts" });
  for (const [v, libelle] of VERDICTS) {
    boutons.append(el("button", { class: n.v === v ? "on" : "", onclick: () => { const x = notes[f.k] || (notes[f.k] = {}); x.v = v; x.taille = f.m.length; x.t = Date.now(); sauver(); c.replaceWith(carte(f)); } }, libelle));
  }
  const note = el("textarea", { placeholder: "Remarque (reste sur ce PC, n'est pas dans le bilan anonyme)" });
  note.value = n.note || "";
  note.addEventListener("change", () => { const x = notes[f.k] || (notes[f.k] = {}); x.note = note.value; sauver(); });
  c.append(boutons, note);
  return c;
}
function afficher() {
  const liste = document.getElementById("liste"), familles = choisies();
  liste.replaceChildren();
  if (!familles.length) liste.append(el("p", { class: "muted" }, "Aucune famille."));
  familles.slice(0, limite).forEach(f => liste.append(carte(f)));
  if (familles.length > limite) liste.append(el("button", { onclick: () => { limite += 50; afficher(); } }, `Afficher plus (${familles.length - limite} restantes)`));
  afficherProgression();
}
function afficherProgression() {
  const ech = FAMILLES.filter(f => f.s), faites = ech.filter(f => notes[f.k] && notes[f.k].v).length;
  document.getElementById("progression").textContent = `Échantillon : ${faites} / ${ech.length} vérifiées · total : ${Object.values(notes).filter(n => n.v).length}`;
}
function telecharger(nom, texte) {
  const a = el("a", { href: URL.createObjectURL(new Blob(["﻿" + texte], { type: "text/plain;charset=utf-8" })), download: nom });
  document.body.append(a); a.click(); a.remove();
}
document.getElementById("bilan").addEventListener("click", () => {
  // Bilan anonyme : ni nom, ni code patient, ni coordonnée, ni remarque. Seulement tailles, verdicts et raisons des liens.
  const lignes = ["Bilan anonyme de vérification des familles", ""];
  const vues = FAMILLES.filter(f => notes[f.k] && notes[f.k].v);
  for (const [v, libelle] of VERDICTS) lignes.push(`${libelle} : ${vues.filter(f => notes[f.k].v === v).length}`);
  const ech = vues.filter(f => f.s);
  lignes.push("", `Échantillon : ${ech.length} vérifiées, dont justes : ${ech.filter(f => notes[f.k].v === "juste").length}`, "");
  for (const f of vues.filter(f => notes[f.k].v !== "juste")) {
    const n = notes[f.k];
    lignes.push(`Famille de ${f.m.length}${f.r ? " (à vérifier)" : ""}${f.c ? " (chaîne)" : ""} : ${VERDICTS.find(x => x[0] === n.v)[1]}`);
    for (const l of f.l) lignes.push(`   ${(n.faux || []).includes(l[0] + "-" + l[1]) ? "FAUX " : "     "}${force[l[2]]} : ${l[3]}`);
  }
  telecharger("bilan-verification-anonyme.txt", lignes.join("\r\n"));
});
document.getElementById("decisions").addEventListener("click", () => {
  const lignes = ["Membres (codes patients);Verdict;Liens faux (codes);Remarque"];
  for (const [k, n] of Object.entries(notes)) if (n.v || (n.faux || []).length)
    lignes.push([k, n.v || "", (n.faux || []).join(" "), (n.note || "").replace(/[\r\n;]+/g, " ")].join(";"));
  telecharger("decisions-familles-NE-PAS-DIFFUSER.csv", lignes.join("\r\n"));
});
document.getElementById("filtre").addEventListener("change", () => { limite = 50; afficher(); });
document.getElementById("cherche").addEventListener("input", () => { limite = 50; afficher(); });
afficher();
</script></body></html>
""";
    }
}
