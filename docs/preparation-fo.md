# Préparation FO

La page **Préparation FO** affiche les patients susceptibles d’avoir un fond d’œil, à partir des champs **Observation** et **REFRACTION** de la table Access **Consultation**, rapprochés des prochains rendez-vous. La période initiale couvre les 7 prochains jours et peut être modifiée. Le client affiche un tableau Windows ; une page d’administration serveur reste disponible séparément.

## Onglet Windows (client)

L’onglet est maintenant une **page WinUI native** : aucun navigateur intégré, redirection web, port 5443 ou certificat serveur n’est nécessaire pour afficher les patients. La connexion EyeChat reste nécessaire pour récupérer le chemin déjà configuré dans **Paramètres → Recherche RDV**. La lecture Access et l’analyse tournent sur le poste, en arrière-plan.

1. Mettre à jour le client. Le compte Windows du poste doit avoir accès en lecture au fichier Access et disposer du fournisseur ACE de la même architecture que le client. Les réglages locaux ACE/groupe de travail des paramètres RDV sont réutilisés.
2. Ouvrir **Préparation FO** : une seule recherche démarre, sur les 7 prochains jours. Les noms de champs sont suggérés à partir du schéma réel et des paramètres RDV existants. Si des champs manquent, utiliser **Champs Access** pour vérifier les correspondances. L’absence d’un champ d’annulation exige une confirmation explicite.
3. Choisir aujourd’hui, demain, 7 jours ou une période personnalisée, puis **Rechercher les patients**. La liste est triée par prochain rendez-vous ; nom, identifiant, consigne, date de consultation et état sont visibles dans le tableau. La recherche par nom et les filtres d’état agissent immédiatement sur cette liste.
4. **Consulter** ouvre les champs Observation et REFRACTION en texte lisible et permet une relecture manuelle. Avant de l’enregistrer, le dossier est relu et son empreinte vérifiée pour refuser une validation obsolète.

Le chargement est protégé contre les appels simultanés et les notifications Loaded répétées. Quitter l’onglet annule le travail et interdit à une réponse tardive de remplir une ancienne page. Un échec conserve la dernière liste avec un avertissement et la période effectivement analysée. Aucun appel récursif au gestionnaire Loaded n’est utilisé.

Le contrôle d’accès du client repose sur les droits Windows du fichier Access et, si présents, ses identifiants de groupe de travail, comme les autres lecteurs Access du poste. Le nom choisi dans le chat ne constitue pas une authentification à la base. Ne donner ces droits qu’aux postes/comptes habilités. Aucune observation ne transite par le hub du chat et aucun nouveau point d’accès réseau médical n’est créé.

Les correspondances de champs et relectures sont propres au **profil Windows de ce poste**, chiffrées avec DPAPI CurrentUser dans `%LOCALAPPDATA%/EyeChat/Fundus`. Seuls une clé de patient hachée avec la source, une empreinte de consultation, l’état et l’auteur/date de relecture sont conservés ; aucun nom de patient, extrait, compte rendu ou mot de passe Access n’y est copié. Une modification clinique invalide la relecture ; un déplacement du rendez-vous seul la conserve. Ces relectures ne sont pas synchronisées avec les validations de la page d’administration serveur.

### Erreur StackOverflowException

La pile fournie pointait vers `App_UnhandledException`, lors de l’accès natif à `window.Content`. Le gestionnaire d’erreur ne consulte plus Window.Content/XamlRoot et ne crée plus de ContentDialog. Il journalise l’exception initiale (CLI01), protège toute la notification contre la réentrance et programme une boîte Windows indépendante de XAML après le retour du gestionnaire. Les échecs de journalisation, de dispatcher ou de notification ne relancent pas de dialogue. Cela supprime le chemin de récursion identifié ; une éventuelle erreur initiale distincte reste à diagnostiquer dans CLI01.

## Page d’administration serveur (optionnelle)

La page `https://serveur:5443/Fundus` reste disponible séparément pour les utilisateurs de l’administration. Elle conserve son authentification HTTPS et ses résultats/validations partagés en SQLite. Elle n’est plus embarquée dans l’onglet Windows.

### Installation de la page serveur

1. Mettre à jour le serveur puis ouvrir son adresse HTTPS dans un navigateur. Le certificat HTTPS doit être reconnu par Windows ; aucun contournement n’est ajouté.
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

Les accès SMS/mail existants utilisent `Patients.[Code Patient]` et `Ag_rdv.[Code Patient]`, avec date et heure configurables. Le lecteur commun `Shared/Fundus/FoAccessData.cs` utilise la connexion OLE DB de `Shared/ReadOnlyAccess.cs` et force `Mode=Read`. Le serveur conserve un adaptateur pour ses propres paramètres ; le client réutilise le même lecteur. Le formulaire permet de choisir les noms exacts de chaque table et champ ; aucune requête ne suppose un champ absent. Les identifiants SQL sont issus du schéma puis échappés et les valeurs sont paramétrées avec le type OLE DB du champ.

Pour une période, le lecteur :

- écarte les rendez-vous annulés et déjà passés, et retient le premier rendez-vous par patient ;
- retrouve le patient par la clé choisie puis toutes ses consultations datées au plus tard à l’instant courant ;
- analyse les textes par ordre chronologique ; une date de consultation identique ne permet pas de départager des consignes contradictoires ;
- conserve au maximum une alerte par patient et source Access.

Les horaires viennent uniquement de l’agenda `Ag_Rdv`. Aucun horaire ni dossier Access n’est modifié. La recherche par nom/prénom/identifiant dans la liste se fait dans le navigateur : les noms saisis ne sont pas envoyés dans une URL ni enregistrés dans les journaux.

## Détection et relecture

Les expressions sont centralisées dans `Shared/Fundus/FundusAnalysis.cs` et `FoClinicalContext.cs`, avec des tests positifs, négatifs et ambigus. La recherche ignore casse, accents, ligature œ et variantes FO / F.O. Les extraits affichés conservent le texte original.

- **FO à prévoir** : instruction explicite non clôturée.
- **À vérifier** : formulation conditionnelle, incertaine, chronologie ambiguë, consultation ultérieure sans preuve claire de réalisation ou mention contextuelle de FOD/FOG, OCT ou bilan maculaire sans consigne explicite de prochain FO.
- **FO probablement réalisé** : mention explicite de réalisation dans la chronologie de la demande, à confirmer si nécessaire.

Les consignes de renouvellement sont reconnues : « refaire FO », « refaire l’examen des FO », « recontrôler les FO », ainsi que « FO dans 6 mois ». Les variantes DS, MOS, mois/an et les retours à la ligne entre action, examen et délai sont acceptés. L’extrait conserve le délai indiqué et la date de consultation reste visible. Aucune échéance n’est calculée automatiquement et aucun examen n’est avancé : il faut vérifier que le délai correspond au rendez-vous à préparer. Une négation de diagnostic telle que « pas de rétinopathie » ne vaut pas annulation d’une consigne de FO qui suit.

Les mentions FOD/FOG (y compris le préfixe V3FOD), OCT et bilan maculaire peuvent faire apparaître un patient en **À vérifier**. Le bilan comparatif (y compris COMPATARATIF) nécessite une mention associée d’OCT ou de FOD/FOG dans Observation ou REFRACTION. Ces éléments donnent une piste de relecture, sans prescrire un FO ni confirmer qu’il a été réalisé. La raison est affichée dans le tableau. Une consigne explicite de FO reste prioritaire ; une annulation/réalisation explicite de même date ou ultérieure neutralise le contexte précédent. La mention contextuelle la plus récente est utilisée lorsqu’il n’y a pas de consigne explicite active.

Un tonus, un traitement, une surveillance du diabète ou de la cataracte seuls ne déclenchent pas ce signalement. Les mentions historiques et négatives sont écartées ; « OCT » n’est pas recherché dans les noms de médicaments et une date telle que « 23 oct. 2026 » n’est pas assimilée à l’examen. Les tests utilisent des extraits synthétiques courts, sans recopier de dossiers, mesures ou traitements réels dans le dépôt.

Une négation de réalisation ne clôture pas une demande. Une annulation explicite ultérieure la retire. Une nouvelle consigne peut rouvrir une demande après une réalisation. Une mention historique de réalisation ne suffit pas à clôturer une consigne récente. Observation et REFRACTION décrivent une même consultation : une réalisation courante dans un champ ne clôture pas une consigne explicitement future dans l’autre ; des consignes contradictoires restent à vérifier. Les règles ne constituent ni une interprétation exhaustive du langage médical, ni une décision médicale. Elles doivent être vérifiées sur des exemples représentatifs du cabinet avant utilisation quotidienne.

La liste principale montre **FO à prévoir** et **À vérifier**. Les cas **FO probablement réalisé** restent accessibles par le filtre d’état. Le tableau présente le nom, l’identifiant, le rendez-vous, la consigne et l’état ; les détails de suivi sont repliables.

**Consulter** dans le client, ou **Lire la consultation source** dans la page serveur, relit Observation et REFRACTION directement dans Access. Les deux champs sont affichés séparément en texte lisible. `Shared/Fundus/FoMedicalText.cs` convertit automatiquement les champs RTF et HTML avant l’analyse et avant l’affichage : aucune « version texte » distincte n’est nécessaire dans la base. Le texte brut est conservé tel quel ; les paragraphes, accents ANSI/Unicode, listes et cellules de tableau des formats enrichis sont restitués en texte.

La conversion utilise RtfPipe et HtmlAgilityPack, entièrement en mémoire sur le poste ou le serveur. Elle n’ouvre aucun navigateur, ne charge aucun lien, image, feuille de style ou pièce jointe et n’envoie aucun compte rendu à un service externe. Les instructions de mise en forme, métadonnées, commentaires, scripts, textes explicitement masqués et textes barrés/supprimés sont exclus. Les styles externes/classes CSS ne sont pas calculés et les images ne font pas l’objet d’une reconnaissance de caractères. Le résultat est affiché comme texte par WinUI, et encodé par Razor sur le serveur ; aucune balise issue du dossier n’est exécutée.

Le convertisseur limite la taille et la profondeur des documents et vérifie la structure RTF avant analyse. Un document invalide, trop imbriqué ou trop volumineux provoque un message explicite sans enregistrer de résultats partiels ; les anciennes lignes restent identifiées comme non actualisées. Les erreurs du parseur ne sont ni affichées ni journalisées, car elles peuvent contenir le texte médical. La base Access reste strictement en lecture seule.

## SQLite et actualisation de la page serveur

`FoSettings` conserve la correspondance des champs. `FoAlerts` conserve la clé de source, l’identifiant et le nom du patient, son prochain rendez-vous, la clé/date de consultation source, un extrait de 600 caractères maximum, l’état, un éventuel extrait ultérieur, l’empreinte de la chronologie et les dates d’analyse/relecture. Les comptes rendus complets ne sont pas recopiés.

La création des tables est idempotente et compatible avec un `chat.db` existant. Une contrainte unique `(SourceKey, PatientId)` évite les doublons. Une actualisation est appliquée en transaction après la lecture et l’analyse complètes ; un échec ne remplace pas l’ancienne liste par un résultat incomplet. Les rendez-vous déplacés ou annulés disparaissent de la période actualisée. Les autres périodes nécessitent leur propre actualisation.

Une validation manuelle garde son auteur et sa date. Elle est conservée si seuls les rendez-vous changent, mais réinitialisée si les consultations/consignes ont changé. Une validation envoyée depuis un affichage dont l’empreinte est dépassée est refusée. Un changement de chemin ou de clés de rapprochement crée une autre source ; les anciennes alertes ne sont pas mélangées. Les configurations historiques utilisant ProchainRDV sont corrigées automatiquement vers Observation + REFRACTION lors d’une recherche réussie. Ce changement est enregistré dans la même transaction que les résultats ; une erreur de lecture laisse les réglages précédents intacts. Les validations des patients réanalysés sont conservées lorsque la consultation source et son texte analysé sont inchangés.

Les lectures et écritures passent par la page Razor authentifiée ; aucune observation n’est publiée sur le hub du chat. Les formulaires utilisent la protection anti-CSRF existante de Razor. Le chargement automatique soumet une seule fois le formulaire de recherche protégé ; aucun GET n’enregistre d’analyse. Les réponses à cette recherche, les vues de configuration/source et les erreurs ne déclenchent pas de boucle de recherche. Les réponses médicales sont `no-store`, sans journalisation des observations ni des erreurs brutes du fournisseur. Aucun service externe d’analyse n’est utilisé.

## Vérification et limites

- `dotnet run --project tests/Fundus.Tests` : moteur, chronologie, schéma, mises à jour SQLite, validations, authentification Razor et anti-CSRF, filtres natifs, annulation/rechargement de page, réentrance du gestionnaire d’erreur et conversion RTF/HTML (accents, négations, paragraphes, contenu masqué, liens et limites), avec données fictives.
- `dotnet run --project tests/SmsMail.Tests` : régressions du module SMS/mail.
- `dotnet build Serveur/Serveur.csproj --configuration Release` : serveur et vues Razor.
- Le workflow Windows compile aussi le client WinUI.

La lecture de la véritable base exige Windows/ACE et l’accès au fichier du cabinet, indisponibles dans l’environnement de développement. Pas de tâche planifiée : la page lance l’analyse à son ouverture après connexion/configuration, ou via le bouton de recherche. Période maximale : 93 jours ; maximum de 1 000 patients par actualisation et 10 000 consultations par patient. Les formats RTF/HTML usuels sont convertis automatiquement ; les variantes réelles du cabinet restent à vérifier sur le poste. Limites par champ : 1 000 000 de caractères source, 100 000 caractères de texte extrait et 64 niveaux d’imbrication. La limite existante de 100 000 caractères cumulés Observation + REFRACTION par consultation reste appliquée lors de la recherche.
