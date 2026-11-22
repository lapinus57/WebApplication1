# Service de réponse aux rendez-vous (Cloudflare Worker)

Page « Confirmer / Annuler » des **messages individuels** envoyés depuis EyeChat (onglet SMS et mail → Message individuel).
Le patient ouvre le lien reçu (`https://rdv.mulleroph.workers.dev/Ab3dEf7h`), choisit « Je confirme » ou « J'annule » ;
le serveur EyeChat relève la réponse chaque minute, l'inscrit dans l'historique de tous les postes et prévient par mail
l'adresse choisie lors de l'envoi.

Ce service ne stocke **aucun nom, téléphone ni adresse mail** : seulement un jeton aléatoire, le nom et l'adresse du cabinet,
la date et l'heure du rendez-vous, l'expiration du lien (3 h après le rendez-vous) et le choix du patient. Les liens sont
supprimés 7 jours après le rendez-vous, les réponses après 30 jours.

## Mise en ligne (une seule fois)

Pré-requis : Worker `rdv`, base D1 `rdv-db` et secret `RELAY_KEY` créés (voir la procédure Cloudflare).

1. **Tables** : Cloudflare → *Stockage et bases de données* → *D1* → `rdv-db` → *Console* : coller le contenu de
   [`schema.sql`](schema.sql) et exécuter.
2. **Code** : Worker `rdv` → *Modifier le code* → remplacer tout le contenu par [`worker.js`](worker.js) → *Déployer*.
3. **Liaison de la base** : Worker `rdv` → *Paramètres* → *Liaisons* → *Ajouter* → *Base de données D1* :
   nom de variable **`DB`**, base **`rdv-db`**.
4. **Secret** : vérifier que *Paramètres* → *Variables et secrets* contient **`RELAY_KEY`** (type Secret, au moins 20 caractères).
5. **Aperçus** : *Paramètres* → *Domaines et routes* → désactiver les URL d'aperçu (« Preview URLs ») si l'option existe.
6. **Vérifier** :
   - `https://rdv.mulleroph.workers.dev` affiche « Service de réponse aux rendez-vous — en ligne » ;
   - `https://rdv.mulleroph.workers.dev/ABC123xy` affiche « Lien inconnu ou expiré ».
7. **EyeChat** : administration du serveur → **Réponses des patients** :
   adresse `https://rdv.mulleroph.workers.dev`, clé `RELAY_KEY`, compte mail (SMTP) qui prévient le cabinet.
   Boutons « Tester le service de réponse » et « Envoyer le mail de test ».

Pour une mise à jour du code : refaire l'étape 2 uniquement.

## API utilisée par le serveur EyeChat

Toutes les adresses `/api/…` exigent l'en-tête `Authorization: Bearer <RELAY_KEY>`.

| Méthode | Adresse | Rôle |
|---|---|---|
| `POST` | `/api/links` | Dépose des liens : `{ "links": [{ "token", "cabinet", "address", "appointment", "expiresAt" }] }` (`expiresAt` en millisecondes Unix) |
| `GET` | `/api/responses?after=<id>` | Réponses depuis l'identifiant donné : `{ "responses": [{ "id", "token", "choice", "answeredAt" }] }` ; `choice` vaut `Confirme` ou `Annule` |
| `GET` | `/api/ping` | Vérifie l'adresse et la clé |

## Tests

Sans Cloudflare, avec la base D1 simulée par SQLite (Node 22.5 ou plus récent) :

```bash
node --test relay/test/worker.test.mjs
```
