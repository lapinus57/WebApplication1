# Analyse du code (novembre 2026)

Revue du serveur `Serveur/`, du hub SignalR et de l'outillage. Les points marqués **corrigé** sont réglés dans ce même changement. Les autres demandent une décision, parce qu'ils changent le protocole entre le client et le serveur.

## Corrigé

| Sujet | Problème | Correction |
|---|---|---|
| Envoi d'avatar (`UploadAvatar`) | Le serveur gardait l'extension envoyée par le client et écrivait le fichier dans `wwwroot/avatars`, qui est servi par le site d'administration. Un poste du réseau pouvait donc déposer un fichier `.html` ou `.svg` contenant du script, et ce script s'exécutait ensuite sur le site d'administration pour l'administrateur connecté. Un base64 invalide faisait aussi planter l'appel. | `AvatarUpload` lit les premiers octets du fichier. Il n'accepte que PNG, JPEG, GIF, WebP et BMP, jusqu'à 1,5 Mo, et choisit l'extension d'après le contenu. En cas de refus, le client reçoit un message clair. |
| Mots de passe des groupes protégés | PBKDF2 avec 10 000 itérations, une comparaison qui n'est pas à temps constant, et une exception si le hash en base est corrompu. | Nouveau format `v2:itérations:sel:hash` (210 000 itérations) et comparaison à temps constant. **Les anciens hashs restent acceptés**, donc aucun groupe n'est à recréer. |
| Connexion à l'administration | La comparaison s'arrêtait au premier caractère faux, et rien ne ralentissait les essais répétés. | Comparaison à temps constant, plus une seconde d'attente après chaque échec. |
| `JoinRoom` / `JoinProtectedGroup` | Une `KeyNotFoundException` se produisait si le poste n'avait pas encore appelé `RegisterUser`. | L'appel est simplement ignoré (ou renvoie `not-registered`). |
| `Program.cs` | Sept fonctions presque identiques, environ 250 lignes, ajoutaient chacune une colonne SQLite. | Elles sont remplacées par un seul `EnsureColumn(table, colonne, définition, code)`. Les codes d'erreur `SERxx` sont conservés. |
| Tests | Rien ne vérifiait les mots de passe ni les avatars. | 14 vérifications ajoutées à `tests/Server.Startup.Tests`, qui tourne déjà dans la CI Windows. |

## Deuxième série de corrections (novembre 2026)

| Point | État |
|---|---|
| Hub sans authentification | **Corrigé si configuré.** `Administration:StationKey` exige une clé de poste pour le hub et `/api/configuration` ; chaque poste la demande une fois. L'expéditeur des messages est imposé par le serveur. Seul un membre peut changer le mot de passe d'un groupe protégé. Les suppressions, purges et changements de groupe sont tracés dans le journal du serveur. |
| Mot de passe applicatif en dur | **Corrigé si configuré.** `Administration:ApplicationPassword` côté serveur ; les postes le vérifient auprès du serveur. L'ancien mot de passe ne sert plus que si ce réglage est vide, ou si le serveur est injoignable. |
| État partagé du hub sans verrou | **Corrigé.** Un filtre SignalR (`ChatHubStateFilter`) fait passer un par un les appels qui modifient l'état. Les lectures en base restent parallèles. |
| Base SQLite | **Amélioré.** Mode WAL, attente de 30 s au lieu d'un échec « database is locked », index sur les dates, sauvegarde quotidienne dans `data/sauvegardes` (14 jours). |
| Erreurs invisibles sur les postes | **Corrigé.** 64 blocs d'erreur du client écrivent dans `app.log`, avec une rotation à 5 Mo. |
| Tests et CI | **Ajoutés.** `tests/Hub.Tests` démarre un vrai serveur et simule plusieurs postes (34 vérifications). La CI tourne aussi sur les envois vers `master2`. `dotnet.yml` est supprimé. |
| Poste de chaque personne | **Ajouté.** Info-bulle « Poste : … » (ou « Dernier poste : … ») dans la liste des utilisateurs. |
| `UpgradeLog.htm` | Supprimé. |

Restent à décider :
- **`Client/Client_TemporaryKey.pfx` est dans le dépôt.** Si cette clé signe les paquets distribués, il faut la remplacer et la retirer du dépôt. Je ne l'ai pas fait, parce que cela casserait la signature locale dans Visual Studio.
- **Conservation des messages :** les messages supprimés restent en base (masqués). Leur purge automatique est une décision de politique de conservation.
- **Découpage de `ChatHub.cs` (~1 900 lignes) et de `SignalRService.cs` (~2 500 lignes) :** utile pour la maintenance. C'est plus sûr maintenant qu'il y a des tests du hub.

## Même compte ouvert sur deux postes

Fonctionnement prévu : le second poste voit « Connexion déjà active ». Le premier poste a 10 s pour accepter ou refuser. Passé ce délai, le second poste peut forcer la connexion, et le premier est alors déconnecté (`ForceLogout`).

Défauts corrigés dans ce mécanisme :

| Cas | Avant | Maintenant |
|---|---|---|
| Coupure réseau ou Wi-Fi, puis reconnexion du **même** poste | Le serveur garde l'ancienne connexion jusqu'à 30 s. Le poste se voyait donc « déjà connecté sur lui-même » : la fenêtre de conflit s'affichait, et il fallait attendre 10 s puis cliquer « Oui ». | Une connexion qui vient du même nom de poste remplace l'ancienne sans demande. Une éventuelle deuxième fenêtre EyeChat sur ce poste est déconnectée. |
| Prise de relais par un autre poste | L'ancien poste restait rattaché au compte jusqu'à sa déconnexion effective, donc il recevait encore les messages privés (poste en veille ou réseau lent). De plus, l'ancien poste était déconnecté *avant* que le nouveau soit enregistré, ce qui pouvait passer le compte « hors ligne » au mauvais moment. | Le nouveau poste est enregistré d'abord. L'ancien est retiré du compte immédiatement, puis déconnecté. |
| Salle affichée après un changement de poste | La nouvelle salle s'**ajoutait** aux anciennes : la personne apparaissait par exemple dans « Salle 1, Salle 3 » jusqu'à son prochain changement de salle, ou avec « 👻👻👻 » si l'ancien poste était en veille. | La salle du nouveau poste remplace l'ancienne. Un poste sans salle configurée affiche son nom de poste. |
| Changement d'avatar ou de statut | Les autres postes ignoraient une mise à jour de la liste qui ne changeait que l'avatar ou le statut. | Ces deux champs font partie de la comparaison côté client. |
| Même nom avec une casse différente (« marie » / « Marie ») | Le conflit était détecté, mais la prise de relais créait un second compte, en double dans la liste et dans la base. | Le nom est ramené à l'orthographe déjà connue. |

Limites restantes :
- Il n'y a pas de mot de passe par compte. N'importe quel poste peut donc prendre n'importe quel compte, en attendant 10 s. C'est le point 1 de la section précédente.
- Deux postes qui se connectent au même compte à la même seconde peuvent tous deux passer sans conflit. C'est le point 3 de la section précédente (état partagé non verrouillé).
