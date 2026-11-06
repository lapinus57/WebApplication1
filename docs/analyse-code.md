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

## À décider (non modifié)

1. **Le hub `/chatHub` n'a pas d'authentification.** Tout poste qui atteint le port 5000 peut appeler `GetPatients`, `ClearTodayPatients`, `ClearTodayMessages`, `DeleteMessage`, `RenameGroup`, `ChangeGroupPassword` ou `RemoveUserFromGroup`. Il peut aussi envoyer des messages au nom d'un autre utilisateur, car `SendMessage` fait confiance au paramètre `sender`.
   - `ChangeGroupPassword` permet en particulier de remplacer le mot de passe d'un groupe protégé, puis d'y entrer.
   - Piste : une clé partagée par poste, transmise à la connexion (`accessTokenProvider`) et vérifiée par un filtre de hub. Le serveur déduirait aussi l'expéditeur de la connexion au lieu de le lire dans le paramètre.
2. **Le mot de passe applicatif `901027` est écrit en dur**, à la fois dans `Client/Helpers/AdministrativeAccess.cs` et dans `Serveur/Hubs/ChatHub.cs`. Il est donc lisible dans le dépôt et dans le binaire du client. Mieux vaudrait le placer dans `appsettings.json` côté serveur (comme `Administration:Password`) et le saisir côté client.
3. **L'état partagé du hub n'est pas protégé contre les accès simultanés.** `ConnectedUsers`, `AllUsers`, `GroupMembers` et `_userToConnectionId` sont des `Dictionary` statiques, utilisés à environ 130 endroits. SignalR exécute les appels de plusieurs postes en parallèle, alors que seuls deux blocs utilisent `UserStateLock`. Des connexions ou déconnexions simultanées peuvent corrompre ces dictionnaires, ce qui se traduit par des exceptions aléatoires ou des utilisateurs « fantômes ». Piste : `ConcurrentDictionary`, ou un verrou systématique autour de toutes les lectures et écritures.
4. **La clé de signature `Client/Client_TemporaryKey.pfx` est versionnée.** Si elle sert à signer les paquets distribués, il faut la retirer du dépôt et la remplacer.
5. **Le workflow `.github/workflows/dotnet.yml` vise la branche `master`, qui n'existe pas.** Il compile sous Ubuntu, ce qui est incompatible avec le client WinUI. Il ne s'exécute donc jamais. La vraie CI est `windows-client.yml` : on peut supprimer `dotnet.yml`.
6. **Fichiers parasites dans le dépôt :** `UpgradeLog.htm`, les avatars d'exemple dans `Serveur/wwwroot/avatars/`, et la ligne « lapin est bien » dans `docs/README.md`.

## Même compte ouvert sur deux postes

Fonctionnement prévu : le second poste voit « Connexion déjà active ». Le premier poste a 10 s pour accepter ou refuser. Passé ce délai, le second poste peut forcer la connexion, et le premier est alors déconnecté (`ForceLogout`).

Défauts corrigés dans ce mécanisme :

| Cas | Avant | Maintenant |
|---|---|---|
| Coupure réseau ou Wi-Fi, puis reconnexion du **même** poste | Le serveur garde l'ancienne connexion jusqu'à 30 s. Le poste se voyait donc « déjà connecté sur lui-même » : la fenêtre de conflit s'affichait, et il fallait attendre 10 s puis cliquer « Oui ». | Une connexion qui vient du même nom de poste remplace l'ancienne sans demande. Une éventuelle deuxième fenêtre EyeChat sur ce poste est déconnectée. |
| Prise de relais par un autre poste | L'ancien poste restait rattaché au compte jusqu'à sa déconnexion effective, donc il recevait encore les messages privés (poste en veille ou réseau lent). De plus, l'ancien poste était déconnecté *avant* que le nouveau soit enregistré, ce qui pouvait passer le compte « hors ligne » au mauvais moment. | Le nouveau poste est enregistré d'abord. L'ancien est retiré du compte immédiatement, puis déconnecté. |
| Même nom avec une casse différente (« marie » / « Marie ») | Le conflit était détecté, mais la prise de relais créait un second compte, en double dans la liste et dans la base. | Le nom est ramené à l'orthographe déjà connue. |

Limites restantes :
- Il n'y a pas de mot de passe par compte. N'importe quel poste peut donc prendre n'importe quel compte, en attendant 10 s. C'est le point 1 de la section précédente.
- Deux postes qui se connectent au même compte à la même seconde peuvent tous deux passer sans conflit. C'est le point 3 de la section précédente (état partagé non verrouillé).
