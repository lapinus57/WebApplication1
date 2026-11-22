// Service de réponse aux rendez-vous (Cloudflare Workers + base D1).
//
// Le patient reçoit un lien https://<worker>/<jeton> dans son SMS ou son mail de rappel et choisit
// « Je confirme » ou « J'annule ». Le serveur EyeChat du cabinet, seul à connaître la clé RELAY_KEY,
// dépose les liens et relève les réponses ; c'est lui qui rattache la réponse au patient et prévient le cabinet.
//
// Ce service ne connaît AUCUN nom, téléphone ni adresse mail : seulement le jeton, le nom et l'adresse du
// cabinet, la date et l'heure du rendez-vous, l'expiration du lien et le choix du patient.
//
// Liaisons attendues : base D1 « DB » (voir schema.sql), secret « RELAY_KEY ».

const TOKEN = /^[A-Za-z0-9]{6,16}$/;
const DAY = 24 * 60 * 60 * 1000;
const KEEP_EXPIRED_LINKS = 7 * DAY;     // liens supprimés 7 jours après le rendez-vous
const KEEP_RESPONSES = 30 * DAY;        // réponses supprimées après 30 jours (déjà relevées par EyeChat)
const CHOICES = { confirm: "Confirme", cancel: "Annule" };

export default {
  async fetch(request, env) {
    try {
      return await route(request, env);
    } catch (error) {
      console.error(error);
      return page(500, "Service momentanément indisponible", "<p>Merci de réessayer dans quelques minutes ou d'appeler le cabinet.</p>");
    }
  },
};

async function route(request, env) {
  const url = new URL(request.url);
  const path = url.pathname;

  if (path.startsWith("/api/")) {
    if (!(await authorized(request, env))) return json(401, { error: "Clé du serveur EyeChat absente ou incorrecte." });
    if (path === "/api/links" && request.method === "POST") return createLinks(request, env);
    if (path === "/api/responses" && request.method === "GET") return listResponses(url, env);
    if (path === "/api/ping" && request.method === "GET") return json(200, { ok: true });
    return json(404, { error: "Adresse inconnue." });
  }

  if (path === "/" && request.method === "GET")
    return page(200, "Service de réponse aux rendez-vous — en ligne", "<p>Ce service permet de confirmer ou d'annuler un rendez-vous à partir du lien reçu par SMS ou par mail.</p>");

  const token = path.slice(1);
  if (!TOKEN.test(token)) return page(404, "Lien inconnu ou expiré", expiredText());
  if (request.method === "GET") return showLink(token, url, env);
  if (request.method === "POST") return answer(token, request, env);
  return page(405, "Action non prise en charge", "");
}

// ---------- API du serveur EyeChat ----------

async function authorized(request, env) {
  const expected = env.RELAY_KEY || "";
  const header = request.headers.get("Authorization") || "";
  const given = header.startsWith("Bearer ") ? header.slice(7) : "";
  if (expected.length < 20 || given.length === 0) return false;
  // Comparaison à temps constant, sur les empreintes pour ne pas dépendre des longueurs.
  const [a, b] = await Promise.all([sha256(expected), sha256(given)]);
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}

async function sha256(text) {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text)));
}

async function createLinks(request, env) {
  let body;
  try { body = await request.json(); } catch { return json(400, { error: "JSON illisible." }); }
  const links = Array.isArray(body?.links) ? body.links : [];
  if (links.length === 0 || links.length > 500) return json(400, { error: "Entre 1 et 500 liens par envoi." });
  const now = Date.now();
  const statements = [];
  for (const link of links) {
    const token = String(link?.token ?? "");
    const expiresAt = Number(link?.expiresAt);
    const cabinet = text(link?.cabinet, 120);
    const address = text(link?.address, 200);
    const appointment = text(link?.appointment, 120);
    if (!TOKEN.test(token) || !Number.isFinite(expiresAt) || expiresAt <= now || expiresAt > now + 400 * DAY || !appointment)
      return json(400, { error: "Lien invalide : jeton, rendez-vous ou expiration incorrects." });
    statements.push(env.DB.prepare(
      "INSERT INTO links (token, cabinet, address, appointment, expires_at, created_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6) " +
      "ON CONFLICT(token) DO UPDATE SET cabinet = ?2, address = ?3, appointment = ?4, expires_at = ?5"
    ).bind(token, cabinet, address, appointment, expiresAt, now));
  }
  await env.DB.batch(statements);
  return json(200, { created: statements.length });
}

async function listResponses(url, env) {
  const after = Math.max(0, Number.parseInt(url.searchParams.get("after") || "0", 10) || 0);
  const now = Date.now();
  // Nettoyage à chaque relevé (le serveur EyeChat relève régulièrement) : pas besoin de tâche planifiée.
  await env.DB.batch([
    env.DB.prepare("DELETE FROM links WHERE expires_at < ?1").bind(now - KEEP_EXPIRED_LINKS),
    env.DB.prepare("DELETE FROM responses WHERE answered_at < ?1").bind(now - KEEP_RESPONSES),
  ]);
  const { results } = await env.DB.prepare(
    "SELECT id, token, choice, answered_at AS answeredAt FROM responses WHERE id > ?1 ORDER BY id LIMIT 500"
  ).bind(after).all();
  return json(200, { responses: results ?? [] });
}

// ---------- Pages du patient ----------

async function findLink(token, env) {
  const link = await env.DB.prepare("SELECT token, cabinet, address, appointment, expires_at AS expiresAt FROM links WHERE token = ?1").bind(token).first();
  return link && link.expiresAt > Date.now() ? link : null;
}

async function lastChoice(token, env) {
  return env.DB.prepare("SELECT choice, answered_at AS answeredAt FROM responses WHERE token = ?1 ORDER BY id DESC LIMIT 1").bind(token).first();
}

async function showLink(token, url, env) {
  const link = await findLink(token, env);
  if (!link) return page(404, "Lien inconnu ou expiré", expiredText());
  const last = await lastChoice(token, env);
  const thanks = url.searchParams.get("ok") === "1" && last
    ? `<p class="done">Merci, votre réponse a été transmise au cabinet.</p>` : "";
  const current = last
    ? `<p class="current">Votre réponse : <strong>${last.choice === CHOICES.cancel ? "rendez-vous annulé" : "rendez-vous confirmé"}</strong>. Vous pouvez la modifier ci-dessous.</p>`
    : "";
  const body = `
    ${thanks}
    <p class="when">${escape(link.appointment)}</p>
    ${link.address ? `<p class="where">${escape(link.address)}</p>` : ""}
    ${current}
    <form method="post">
      <button name="choice" value="confirm" class="confirm">Je confirme ma venue</button>
      <button name="choice" value="cancel" class="cancel">J'annule le rendez-vous</button>
    </form>
    <p class="small">Pour déplacer le rendez-vous, merci d'appeler le cabinet.</p>
    <p class="small">Votre choix est transmis au cabinet uniquement pour gérer ce rendez-vous. Ce lien expire après l'heure du rendez-vous.</p>`;
  return page(200, link.cabinet || "Votre rendez-vous", body);
}

async function answer(token, request, env) {
  const link = await findLink(token, env);
  if (!link) return page(404, "Lien inconnu ou expiré", expiredText());
  let choice = "";
  try { choice = String((await request.formData()).get("choice") || ""); } catch { /* formulaire illisible */ }
  if (!(choice in CHOICES)) return page(400, "Réponse non reconnue", `<p><a href="/${token}">Revenir au rendez-vous</a></p>`);
  const last = await lastChoice(token, env);
  // Même réponse envoyée deux fois (double clic, rechargement) : enregistrée une seule fois.
  if (!last || last.choice !== CHOICES[choice])
    await env.DB.prepare("INSERT INTO responses (token, choice, answered_at) VALUES (?1, ?2, ?3)").bind(token, CHOICES[choice], Date.now()).run();
  return new Response(null, { status: 303, headers: { ...securityHeaders(), Location: `/${token}?ok=1` } });
}

function expiredText() {
  return "<p>Ce lien n'est plus valable : le rendez-vous est passé ou le lien est incorrect.</p><p>Pour toute question, merci d'appeler le cabinet.</p>";
}

// ---------- Réponses HTTP ----------

function text(value, max) {
  return String(value ?? "").replace(/[\u0000-\u001f]/g, " ").trim().slice(0, max);
}

function escape(value) {
  return String(value).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

function securityHeaders() {
  return {
    "Cache-Control": "no-store",
    "X-Robots-Tag": "noindex, nofollow",
    "Referrer-Policy": "no-referrer",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'",
  };
}

function json(status, data) {
  return new Response(JSON.stringify(data), { status, headers: { ...securityHeaders(), "Content-Type": "application/json; charset=utf-8" } });
}

function page(status, title, body) {
  const html = `<!doctype html>
<html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow"><title>${escape(title)}</title>
<style>
  :root { color-scheme: light dark; --accent: #246bfd; }
  body { margin: 0; font: 17px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; background: #f4f7fb; color: #152238; }
  @media (prefers-color-scheme: dark) { body { background: #10161f; color: #e6ebf2; } .card { background: #18212d !important; } }
  .card { max-width: 30rem; margin: 1.5rem auto; padding: 1.5rem 1.25rem; background: #fff; border-radius: 14px; box-shadow: 0 2px 12px rgba(0,0,0,.08); }
  h1 { font-size: 1.3rem; margin: 0 0 .75rem; }
  .when { font-size: 1.2rem; font-weight: 600; margin: .5rem 0; }
  .where { margin: 0 0 1rem; opacity: .8; }
  form { display: grid; gap: .75rem; margin: 1.25rem 0; }
  button { font: inherit; font-weight: 600; padding: .9rem; border: 0; border-radius: 10px; color: #fff; cursor: pointer; }
  .confirm { background: #16a36a; } .cancel { background: #d94c4c; }
  .done { padding: .75rem; border-radius: 10px; background: rgba(22,163,106,.15); font-weight: 600; }
  .current { margin: 1rem 0 0; }
  .small { font-size: .85rem; opacity: .75; }
  a { color: var(--accent); }
</style></head>
<body><main class="card"><h1>${escape(title)}</h1>${body}</main></body></html>`;
  return new Response(html, { status, headers: { ...securityHeaders(), "Content-Type": "text/html; charset=utf-8" } });
}
