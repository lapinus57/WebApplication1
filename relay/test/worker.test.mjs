// Tests du Worker sans Cloudflare : la base D1 est simulée avec SQLite (node:sqlite, Node 22.5+).
// Lancer : node --test relay/test/
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import worker from "../worker.js";

const KEY = "cle-de-test-suffisamment-longue-123456";

function fakeD1() {
  const db = new DatabaseSync(":memory:");
  db.exec(readFileSync(new URL("../schema.sql", import.meta.url), "utf8"));
  const statement = (sql, params = []) => ({
    bind: (...values) => statement(sql, values),
    first: async () => db.prepare(sql).get(...params) ?? null,
    all: async () => ({ results: db.prepare(sql).all(...params) }),
    run: async () => db.prepare(sql).run(...params),
  });
  return { prepare: sql => statement(sql), batch: async list => { for (const s of list) await s.run(); }, raw: db };
}

const env = () => ({ DB: fakeD1(), RELAY_KEY: KEY });
const call = (e, path, init = {}) => worker.fetch(new Request("https://rdv.test" + path, init), e);
const api = (e, path, init = {}) => call(e, path, { ...init, headers: { Authorization: "Bearer " + KEY, "Content-Type": "application/json", ...(init.headers || {}) } });
const link = (token, overrides = {}) => ({ token, cabinet: "Cabinet Test", address: "1 rue des Lilas, Metz", appointment: "lundi 23 novembre à 9h30", expiresAt: Date.now() + 3600_000, ...overrides });
const choose = (e, token, choice) => call(e, "/" + token, { method: "POST", body: new URLSearchParams({ choice }), redirect: "manual" });

test("la page d'accueil répond", async () => {
  const res = await call(env(), "/");
  assert.equal(res.status, 200);
  assert.match(await res.text(), /en ligne/);
  assert.equal(res.headers.get("Cache-Control"), "no-store");
});

test("l'API refuse une clé absente, fausse ou trop courte", async () => {
  const e = env();
  assert.equal((await call(e, "/api/ping")).status, 401);
  assert.equal((await call(e, "/api/ping", { headers: { Authorization: "Bearer mauvaise" } })).status, 401);
  assert.equal((await api(e, "/api/ping")).status, 200);
  const weak = { DB: fakeD1(), RELAY_KEY: "court" };
  assert.equal((await call(weak, "/api/ping", { headers: { Authorization: "Bearer court" } })).status, 401);
});

test("parcours complet : lien, page, annulation, relevé", async () => {
  const e = env();
  const created = await api(e, "/api/links", { method: "POST", body: JSON.stringify({ links: [link("K7mQ2xAb")] }) });
  assert.equal(created.status, 200);

  const page = await (await call(e, "/K7mQ2xAb")).text();
  assert.match(page, /Cabinet Test/);
  assert.match(page, /lundi 23 novembre à 9h30/);
  assert.match(page, /Je confirme/);

  const posted = await choose(e, "K7mQ2xAb", "cancel");
  assert.equal(posted.status, 303);
  assert.equal(posted.headers.get("Location"), "/K7mQ2xAb?ok=1");
  const thanks = await (await call(e, "/K7mQ2xAb?ok=1")).text();
  assert.match(thanks, /Merci, votre réponse a été transmise/);
  assert.match(thanks, /rendez-vous annulé/);

  await choose(e, "K7mQ2xAb", "cancel"); // double clic : pas de seconde réponse
  await choose(e, "K7mQ2xAb", "confirm"); // le patient change d'avis
  const { responses } = await (await api(e, "/api/responses?after=0")).json();
  assert.deepEqual(responses.map(r => r.choice), ["Annule", "Confirme"]);
  assert.ok(responses.every(r => r.token === "K7mQ2xAb" && r.answeredAt > 0));

  const later = await (await api(e, "/api/responses?after=" + responses[0].id)).json();
  assert.deepEqual(later.responses.map(r => r.choice), ["Confirme"], "le relevé reprend après le dernier identifiant lu");
});

test("lien inconnu, expiré ou mal formé : page neutre, aucune réponse possible", async () => {
  const e = env();
  e.DB.raw.prepare("INSERT INTO links VALUES ('Expire01','C','','hier',?,?)").run(Date.now() - 1000, Date.now() - 5000);
  for (const path of ["/Inconnu1", "/Expire01", "/../admin", "/abc", "/api"]) {
    const res = await call(e, path);
    assert.ok(res.status === 404 || res.status === 401, path);
  }
  assert.equal((await choose(e, "Expire01", "cancel")).status, 404);
  assert.equal(e.DB.raw.prepare("SELECT COUNT(*) AS n FROM responses").get().n, 0);
});

test("choix inconnu refusé, contenu échappé", async () => {
  const e = env();
  await api(e, "/api/links", { method: "POST", body: JSON.stringify({ links: [link("Echap123", { cabinet: "<script>alert(1)</script>" })] }) });
  const page = await (await call(e, "/Echap123")).text();
  assert.ok(!page.includes("<script>alert"), "HTML échappé");
  assert.equal((await choose(e, "Echap123", "supprimer")).status, 400);
});

test("création refusée si jeton, date ou expiration invalides", async () => {
  const e = env();
  for (const bad of [link("a/b"), link("Valide12", { expiresAt: Date.now() - 1 }), link("Valide12", { appointment: "" })]) {
    const res = await api(e, "/api/links", { method: "POST", body: JSON.stringify({ links: [bad] }) });
    assert.equal(res.status, 400);
  }
  assert.equal((await api(e, "/api/links", { method: "POST", body: "{" })).status, 400);
});

test("nettoyage : liens et réponses anciens supprimés au relevé", async () => {
  const e = env();
  const old = Date.now() - 40 * 24 * 3600_000;
  e.DB.raw.prepare("INSERT INTO links VALUES ('Ancien12','C','','x',?,?)").run(old, old);
  e.DB.raw.prepare("INSERT INTO responses (token, choice, answered_at) VALUES ('Ancien12','Confirme',?)").run(old);
  await api(e, "/api/responses?after=0");
  assert.equal(e.DB.raw.prepare("SELECT COUNT(*) AS n FROM links").get().n, 0);
  assert.equal(e.DB.raw.prepare("SELECT COUNT(*) AS n FROM responses").get().n, 0);
});

test("diagnostic : base non liée ou tables absentes expliquées au serveur EyeChat", async () => {
  const noDb = { RELAY_KEY: KEY };
  const res = await api(noDb, "/api/links", { method: "POST", body: JSON.stringify({ links: [link("Diag1234")] }) });
  assert.equal(res.status, 500);
  assert.match((await res.json()).error, /Base D1 non liée.*DB.*rdv-db/);
  assert.equal((await call(noDb, "/Diag1234")).status, 500, "le patient voit une page d'indisponibilité");

  const empty = { DB: (() => { const d = fakeD1(); d.raw.exec("DROP TABLE links; DROP TABLE responses;"); return d; })(), RELAY_KEY: KEY };
  const created = await api(empty, "/api/links", { method: "POST", body: JSON.stringify({ links: [link("Diag1234")] }) });
  assert.equal(created.status, 500);
  assert.match((await created.json()).error, /Tables absentes.*schema\.sql/);
  const ping = await api(empty, "/api/ping");
  assert.equal(ping.status, 500, "le test de l'administration détecte les tables absentes");
  assert.match((await ping.json()).error, /schema\.sql/);
});

test("la page d'accueil indique ce qui manque à l'installation", async () => {
  assert.match(await (await call({ RELAY_KEY: KEY }, "/")).text(), /Installation incomplète[\s\S]*Base D1 non liée/);
  assert.match(await (await call({ DB: fakeD1() }, "/")).text(), /RELAY_KEY/);
  const empty = fakeD1(); empty.raw.exec("DROP TABLE links; DROP TABLE responses;");
  assert.match(await (await call({ DB: empty, RELAY_KEY: KEY }, "/")).text(), /Tables absentes/);
  assert.match(await (await call(env(), "/")).text(), /en ligne/);
});
