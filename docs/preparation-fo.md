# Préparation FO

La page **Préparation FO** affiche les patients susceptibles d’avoir un fond d’œil, à partir des champs **Observation** et **REFRACTION** de la table Access **Consultation**, rapprochés des prochains rendez-vous. La période initiale couvre les 7 prochains jours et peut être modifiée. Elle est accessible depuis le nouvel onglet EyeChat ou `https://serveur:5443/Fundus`. Elle utilise la connexion d’administration du serveur existante (cookie HTTPS). Les comptes du chat ne donnent pas, à eux seuls, accès aux observations.

## Installation et première utilisation

1. Mettre à jour **le serveur et le client**. La page s’ouvre dans le WebView2 déjà utilisé par le client. Le certificat HTTPS du serveur doit être reconnu par Windows ; aucun contournement de certificat n’est ajouté.
2. Donner au compte Windows du service serveur l’accès en lecture à la base Access. Par défaut, le chemin provient des paramètres **Recherche RDV** conservés dans `ServerConfigs.AppointmentSearchJson`. Si ce chemin ne convient qu’aux postes clients, définir `FoAccess:DatabasePath` sur le serveur.
3. Installer un fournisseur ACE de la même architecture que le processus serveur. Pour une base protégée par un groupe de travail, configurer sur le serveur `FoAccess:Provider`, `FoAccess:WorkgroupPath`, `FoAccess:UserName`, `FoAccess:Password`. Ces valeurs ne sont ni envoyées au navigateur, ni stockées dans les nouvelles tables SQLite. Les réglages du poste client ne sont pas transférés automatiquement.
4. Lors de la première ouverture, la page propose les **Réglages Access**. Les tables et colonnes proviennent réellement de `GetOleDbSchemaTable`. Vérifier les clés de rapprochement, les noms/prénoms et le champ d’annulation. Les deux champs médicaux sont fixés à `Observation` et `REFRACTION` ; leur présence est vérifiée avant lecture. L’absence d’un champ d’annulation exige une confirmation explicite.
5. Cliquer sur **Enregistrer et rechercher les patients**. Ensuite, l’ouverture de la page et le changement de période ou d’état lancent automatiquement une recherche. **Rechercher les patients dans Access** permet de la relancer manuellement. Le chargement est annoncé ; un échec est affiché comme tel, avec les anciennes lignes identifiées comme résultats précédents.

ASP.NET Core accepte aussi les variables d’environnement, par exemple `FoAccess__DatabasePath` et `FoAccess__TimeZoneId`. Le fuseau du cabinet est `Europe/Paris` par défaut. Ne pas ajouter de mot de passe réel dans le dépôt ou les journaux.

## Schéma confirmé et rapprochement

Aucun `.mdb` n’est livré dans le dépôt. L’utilisateur a fourni la structure de `Consultation` :

| Rôle | Champ confirmé |
| --- | --- |
| Clé de consultation | `N° consultation` |
| Liaison patient | `Code patient` |
| Date de consultation | `Date` |
| Texte médical | `Observation` |
| Second texte médical | `REFRACTION` |

Les identifiants et dates sont **suggérés uniquement s’ils existent dans le schéma lu**. Les types réels, le format du texte, les champs de nom/prénom de Patients et le champ d’annulation de l’agenda restent à vérifier sur la base du cabinet. `Observation` et `REFRACTION` sont obligatoires pour cette analyse. `ProchainRDV`, signalé comme toujours vide par l’utilisateur, n’est plus utilisé ni lu dans les requêtes médicales.

Les accès SMS/mail existants utilisent `Patients.[Code Patient]` et `Ag_rdv.[Code Patient]`, avec date et heure configurables. Le nouveau lecteur reprend la même connexion OLE DB, extraite dans `Shared/ReadOnlyAccess.cs`, et force `Mode=Read`. Le formulaire permet de choisir les noms exacts de chaque table et champ ; aucune requête ne suppose un champ absent. Les identifiants SQL sont issus du schéma puis échappés et les valeurs sont paramétrées avec le type OLE DB du champ.

Pour une période, le lecteur :

- écarte les rendez-vous annulés et déjà passés, et retient le premier rendez-vous par patient ;
- retrouve le patient par la clé choisie puis toutes ses consultations datées au plus tard à l’instant courant ;
- analyse les textes par ordre chronologique ; une date de consultation identique ne permet pas de départager des consignes contradictoires ;
- conserve au maximum une alerte par patient et source Access.

Les horaires viennent uniquement de l’agenda `Ag_Rdv`. Aucun horaire ni dossier Access n’est modifié. La recherche par nom/prénom/identifiant dans la liste se fait dans le navigateur : les noms saisis ne sont pas envoyés dans une URL ni enregistrés dans les journaux.

## Détection et relecture

Les expressions sont centralisées dans `Shared/Fundus/FundusAnalysis.cs`, avec des tests positifs, négatifs et ambigus. La recherche ignore casse, accents, ligature œ et variantes FO / F.O. Les extraits affichés conservent le texte original.

- **FO à prévoir** : instruction explicite non clôturée.
- **À vérifier** : formulation conditionnelle, incertaine, chronologie ambiguë ou consultation ultérieure sans preuve claire de réalisation.
- **FO probablement réalisé** : mention explicite de réalisation dans la chronologie de la demande, à confirmer si nécessaire.

Une négation de réalisation ne clôture pas une demande. Une annulation explicite ultérieure la retire. Une nouvelle consigne peut rouvrir une demande après une réalisation. Une mention historique de réalisation ne suffit pas à clôturer une consigne récente. Observation et REFRACTION décrivent une même consultation : une réalisation courante dans un champ ne clôture pas une consigne explicitement future dans l’autre ; des consignes contradictoires restent à vérifier. Les règles ne constituent ni une interprétation exhaustive du langage médical, ni une décision médicale. Elles doivent être vérifiées sur des exemples représentatifs du cabinet avant utilisation quotidienne.

La liste principale montre **FO à prévoir** et **À vérifier**. Les cas **FO probablement réalisé** restent accessibles par le filtre d’état. Le tableau présente le nom, l’identifiant, le rendez-vous, la consigne et l’état ; les détails de suivi sont repliables.

Le lien **Lire la consultation source** relit directement Observation et REFRACTION dans Access, affichés dans deux sections distinctes. Le texte est encodé par Razor et présenté comme texte simple. Il n’est pas inséré comme HTML. Les champs RTF/HTML, les dates/heures invalides, les clés manquantes et les volumes excessifs interrompent l’analyse avec une explication plutôt que produire silencieusement une liste partielle.

## SQLite et actualisation

`FoSettings` conserve la correspondance des champs. `FoAlerts` conserve la clé de source, l’identifiant et le nom du patient, son prochain rendez-vous, la clé/date de consultation source, un extrait de 600 caractères maximum, l’état, un éventuel extrait ultérieur, l’empreinte de la chronologie et les dates d’analyse/relecture. Les comptes rendus complets ne sont pas recopiés.

La création des tables est idempotente et compatible avec un `chat.db` existant. Une contrainte unique `(SourceKey, PatientId)` évite les doublons. Une actualisation est appliquée en transaction après la lecture et l’analyse complètes ; un échec ne remplace pas l’ancienne liste par un résultat incomplet. Les rendez-vous déplacés ou annulés disparaissent de la période actualisée. Les autres périodes nécessitent leur propre actualisation.

Une validation manuelle garde son auteur et sa date. Elle est conservée si seuls les rendez-vous changent, mais réinitialisée si les consultations/consignes ont changé. Une validation envoyée depuis un affichage dont l’empreinte est dépassée est refusée. Un changement de chemin ou de clés de rapprochement crée une autre source ; les anciennes alertes ne sont pas mélangées. Les configurations historiques utilisant ProchainRDV sont corrigées automatiquement vers Observation + REFRACTION lors d’une recherche réussie. Ce changement est enregistré dans la même transaction que les résultats ; une erreur de lecture laisse les réglages précédents intacts. Les validations des patients réanalysés sont conservées lorsque la consultation source et son texte analysé sont inchangés.

Les lectures et écritures passent par la page Razor authentifiée ; aucune observation n’est publiée sur le hub du chat. Les formulaires utilisent la protection anti-CSRF existante de Razor. Le chargement automatique soumet une seule fois le formulaire de recherche protégé ; aucun GET n’enregistre d’analyse. Les réponses à cette recherche, les vues de configuration/source et les erreurs ne déclenchent pas de boucle de recherche. Les réponses médicales sont `no-store`, sans journalisation des observations ni des erreurs brutes du fournisseur. Aucun service externe d’analyse n’est utilisé.

## Vérification et limites

- `dotnet run --project tests/Fundus.Tests` : moteur, chronologie, schéma, mises à jour SQLite, validations, authentification Razor et anti-CSRF, avec données fictives.
- `dotnet run --project tests/SmsMail.Tests` : régressions du module SMS/mail.
- `dotnet build Serveur/Serveur.csproj --configuration Release` : serveur et vues Razor.
- Le workflow Windows compile aussi le client WinUI.

La lecture de la véritable base exige Windows/ACE et l’accès au fichier du cabinet, indisponibles dans l’environnement de développement. Pas de tâche planifiée : la page lance l’analyse à son ouverture après connexion/configuration, ou via le bouton de recherche. Période maximale : 93 jours ; maximum de 1 000 patients par actualisation et 10 000 consultations par patient. Les formats médicaux RTF/HTML doivent être adaptés après vérification de leur structure réelle.
