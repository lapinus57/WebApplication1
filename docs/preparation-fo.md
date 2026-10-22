# Préparation FO

La page **Préparation FO** recherche localement les consignes de fond d’œil dans les consultations et les rapproche des prochains rendez-vous. Elle est accessible depuis le nouvel onglet EyeChat ou `https://serveur:5443/Fundus`. Elle utilise la connexion d’administration du serveur existante (cookie HTTPS). Les comptes du chat ne donnent pas, à eux seuls, accès aux observations.

## Installation et première utilisation

1. Mettre à jour **le serveur et le client**. La page s’ouvre dans le WebView2 déjà utilisé par le client. Le certificat HTTPS du serveur doit être reconnu par Windows ; aucun contournement de certificat n’est ajouté.
2. Donner au compte Windows du service serveur l’accès en lecture à la base Access. Par défaut, le chemin provient des paramètres **Recherche RDV** conservés dans `ServerConfigs.AppointmentSearchJson`. Si ce chemin ne convient qu’aux postes clients, définir `FoAccess:DatabasePath` sur le serveur.
3. Installer un fournisseur ACE de la même architecture que le processus serveur. Pour une base protégée par un groupe de travail, configurer sur le serveur `FoAccess:Provider`, `FoAccess:WorkgroupPath`, `FoAccess:UserName`, `FoAccess:Password`. Ces valeurs ne sont ni envoyées au navigateur, ni stockées dans les nouvelles tables SQLite. Les réglages du poste client ne sont pas transférés automatiquement.
4. Ouvrir **Configurer les champs Access**. Les tables et colonnes proposées sont réellement lues avec `GetOleDbSchemaTable`. Vérifier les correspondances, sélectionner les textes médicaux et le champ d’annulation des rendez-vous. L’absence de ce dernier exige une confirmation explicite.
5. Enregistrer, choisir une période, puis **Actualiser l’analyse**. Les filtres et la consultation de la liste ne relancent pas l’analyse : la date de dernière analyse reste affichée pour chaque alerte.

ASP.NET Core accepte aussi les variables d’environnement, par exemple `FoAccess__DatabasePath` et `FoAccess__TimeZoneId`. Le fuseau du cabinet est `Europe/Paris` par défaut. Ne pas ajouter de mot de passe réel dans le dépôt ou les journaux.

## Schéma confirmé et rapprochement

Aucun `.mdb` n’est livré dans le dépôt. L’utilisateur a fourni la structure de `Consultation` :

| Rôle | Champ confirmé |
| --- | --- |
| Clé de consultation | `N° consultation` |
| Liaison patient | `Code patient` |
| Date de consultation | `Date` |
| Texte médical | `Observation` |
| Consignes pour la suite | `ProchainRDV` |

Ces champs sont **suggérés uniquement s’ils existent dans le schéma lu**. `DOMINANTE`, `AutresPrescriptions` et les autres champs peuvent être sélectionnés au besoin. Les types réels, le format du texte, les champs de nom/prénom de Patients et le champ d’annulation de l’agenda restent à vérifier sur la base du cabinet.

Les accès SMS/mail existants utilisent `Patients.[Code Patient]` et `Ag_rdv.[Code Patient]`, avec date et heure configurables. Le nouveau lecteur reprend la même connexion OLE DB, extraite dans `Shared/ReadOnlyAccess.cs`, et force `Mode=Read`. Le formulaire permet de choisir les noms exacts de chaque table et champ ; aucune requête ne suppose un champ absent. Les identifiants SQL sont issus du schéma puis échappés et les valeurs sont paramétrées avec le type OLE DB du champ.

Pour une période, le lecteur :

- écarte les rendez-vous annulés et déjà passés, et retient le premier rendez-vous par patient ;
- retrouve le patient par la clé choisie puis toutes ses consultations datées au plus tard à l’instant courant ;
- analyse les textes par ordre chronologique ; une date de consultation identique ne permet pas de départager des consignes contradictoires ;
- conserve au maximum une alerte par patient et source Access.

`ProchainRDV` est du **texte de consigne**, jamais la source de l’horaire du rendez-vous. Aucun horaire ni dossier Access n’est modifié.

## Détection et relecture

Les expressions sont centralisées dans `Shared/Fundus/FundusAnalysis.cs`, avec des tests positifs, négatifs et ambigus. La recherche ignore casse, accents, ligature œ et variantes FO / F.O. Les extraits affichés conservent le texte original.

- **FO à prévoir** : instruction explicite non clôturée.
- **À vérifier** : formulation conditionnelle, incertaine, mention courte dans `ProchainRDV`, chronologie ambiguë ou consultation ultérieure sans preuve claire de réalisation.
- **FO probablement réalisé** : mention explicite de réalisation dans la chronologie de la demande, à confirmer si nécessaire.

Une négation de réalisation ne clôture pas une demande. Une annulation explicite ultérieure la retire. Une nouvelle consigne peut rouvrir une demande après une réalisation. Une mention historique de réalisation ne suffit pas à clôturer une consigne récente. Les règles ne constituent ni une interprétation exhaustive du langage médical, ni une décision médicale. Elles doivent être vérifiées sur des exemples représentatifs du cabinet avant utilisation quotidienne.

Le lien **Lire la consultation source** relit directement les champs sélectionnés dans Access. Le texte est encodé par Razor et présenté comme texte simple. Il n’est pas inséré comme HTML. Les champs RTF/HTML, les dates/heures invalides, les clés manquantes et les volumes excessifs interrompent l’analyse avec une explication plutôt que produire silencieusement une liste partielle.

## SQLite et actualisation

`FoSettings` conserve la correspondance des champs. `FoAlerts` conserve la clé de source, l’identifiant et le nom du patient, son prochain rendez-vous, la clé/date de consultation source, un extrait de 600 caractères maximum, l’état, un éventuel extrait ultérieur, l’empreinte de la chronologie et les dates d’analyse/relecture. Les comptes rendus complets ne sont pas recopiés.

La création des tables est idempotente et compatible avec un `chat.db` existant. Une contrainte unique `(SourceKey, PatientId)` évite les doublons. Une actualisation est appliquée en transaction après la lecture et l’analyse complètes ; un échec ne remplace pas l’ancienne liste par un résultat incomplet. Les rendez-vous déplacés ou annulés disparaissent de la période actualisée. Les autres périodes nécessitent leur propre actualisation.

Une validation manuelle garde son auteur et sa date. Elle est conservée si seuls les rendez-vous changent, mais réinitialisée si les consultations/consignes ont changé. Une validation envoyée depuis un affichage dont l’empreinte est dépassée est refusée. Un changement de chemin ou de correspondance de champs crée une autre source ; les anciennes alertes ne sont pas mélangées.

Les lectures et écritures passent par la page Razor authentifiée ; aucune observation n’est publiée sur le hub du chat. Les formulaires utilisent la protection anti-CSRF existante de Razor. Les réponses médicales sont `no-store`, sans journalisation des observations ni des erreurs brutes du fournisseur. Aucun service externe d’analyse n’est utilisé.

## Vérification et limites

- `dotnet run --project tests/Fundus.Tests` : moteur, chronologie, schéma, mises à jour SQLite, validations, authentification Razor et anti-CSRF, avec données fictives.
- `dotnet run --project tests/SmsMail.Tests` : régressions du module SMS/mail.
- `dotnet build Serveur/Serveur.csproj --configuration Release` : serveur et vues Razor.
- Le workflow Windows compile aussi le client WinUI.

La lecture de la véritable base exige Windows/ACE et l’accès au fichier du cabinet, indisponibles dans l’environnement de développement. Pas de tâche planifiée : le bouton Actualiser lance l’analyse à la demande. Période maximale : 93 jours ; maximum de 1 000 patients par actualisation et 10 000 consultations par patient. Les formats médicaux RTF/HTML doivent être adaptés après vérification de leur structure réelle.
