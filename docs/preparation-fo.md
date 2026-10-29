# Préparation FO

La page **Préparation FO** affiche les patients susceptibles d’avoir un fond d’œil, à partir des champs **Observation** et **REFRACTION** de la table Access **Consultation**, des actes CCAM liés via **Tarif**, et des prochains rendez-vous. La période initiale couvre les 7 prochains jours et peut être modifiée. Le client affiche un tableau Windows ; une page d’administration serveur reste disponible séparément.

## Onglet Windows (client)

L’onglet est maintenant une **page WinUI native** : aucun navigateur intégré, redirection web, port 5443 ou certificat serveur n’est nécessaire pour afficher les patients. La connexion EyeChat reste nécessaire pour récupérer le chemin déjà configuré dans **Paramètres → Recherche RDV**. La lecture Access et l’analyse tournent sur le poste, en arrière-plan.

1. Mettre à jour le client. Le compte Windows du poste doit avoir accès en lecture au fichier Access et disposer du fournisseur ACE de la même architecture que le client. Les réglages locaux ACE/groupe de travail des paramètres RDV sont réutilisés.
2. Ouvrir **Préparation FO** : la période proposée couvre les 7 prochains jours, mais aucune connexion Access ni recherche ne démarre à l’ouverture, y compris après un retour dans l’onglet. **Diagnostic Access** est disponible immédiatement et lit uniquement les déclarations des moteurs sur le poste, sans lancer Office ou ouvrir la base.
3. Choisir aujourd’hui, demain, 7 jours ou une période personnalisée, puis **Rechercher les patients** pour lancer la lecture Access. Les noms de champs sont suggérés à partir du schéma réel et des paramètres RDV existants. Si des champs manquent, utiliser **Champs Access**, qui ouvre aussi la base, pour vérifier les correspondances. L’absence d’un champ d’annulation exige une confirmation explicite. La liste est triée par prochain rendez-vous ; nom, identifiant, consigne, date de consultation et état sont visibles dans le tableau. La recherche par nom et les filtres d’état agissent immédiatement sur cette liste, sans nouvelle connexion Access.
4. La colonne **Dernière consultation** montre la date la plus récente et ses codes CCAM, séparément de la consultation source du repérage. La recherche locale accepte nom, identifiant, code CCAM ou motif affiché. **Consulter** relit la chronologie, affiche les trois consultations les plus récentes dans des blocs dépliables puis la consultation source, avec Observation et REFRACTION en texte lisible. Si le dossier ou les actes ont changé, une nouvelle recherche est demandée. La relecture manuelle vérifie aussi l’empreinte avant enregistrement.

Le chargement est protégé contre les appels simultanés et les notifications Loaded répétées. Quitter l’onglet demande l’annulation du travail et interdit à une réponse tardive de remplir une ancienne page ; cela ne peut pas interrompre un appel natif ACE déjà bloqué. Un échec conserve la dernière liste avec un avertissement et la période effectivement analysée. Aucune recherche n’est relancée automatiquement après un échec ou une notification Loaded. L’enregistrement des correspondances via **Enregistrer et rechercher** conserve sa recherche explicite.

Le contrôle d’accès du client repose sur les droits Windows du fichier Access et, si présents, ses identifiants de groupe de travail, comme les autres lecteurs Access du poste. Le nom choisi dans le chat ne constitue pas une authentification à la base. Ne donner ces droits qu’aux postes/comptes habilités. Aucune observation ne transite par le hub du chat et aucun nouveau point d’accès réseau médical n’est créé.

Les correspondances de champs et relectures sont propres au **profil Windows de ce poste**, chiffrées avec DPAPI CurrentUser dans `%LOCALAPPDATA%/EyeChat/Fundus`. Seuls une clé de patient hachée avec la source, une empreinte de consultation, l’état et l’auteur/date de relecture sont conservés ; aucun nom de patient, extrait, compte rendu ou mot de passe Access n’y est copié. Une modification clinique invalide la relecture ; un déplacement du rendez-vous seul la conserve. Ces relectures ne sont pas synchronisées avec les validations de la page d’administration serveur.

### Erreur StackOverflowException

La pile fournie pointait vers `App_UnhandledException`, lors de l’accès natif à `window.Content`. Le gestionnaire d’erreur ne consulte plus Window.Content/XamlRoot et ne crée plus de ContentDialog. Il journalise l’exception initiale (CLI01), protège toute la notification contre la réentrance et programme une boîte Windows indépendante de XAML après le retour du gestionnaire. Les échecs de journalisation, de dispatcher ou de notification ne relancent pas de dialogue. Cela supprime le chemin de récursion identifié ; une éventuelle erreur initiale distincte reste à diagnostiquer dans CLI01.

### Fiches Access ignorées, échéance et relecture

- **Fiche mal remplie** : dans le client, un rendez-vous dont le patient est introuvable dans Patients, dont la date, l’heure ou l’annulation est illisible, ou dont les consultations sont incohérentes n’empêche plus la recherche. Il est écarté et listé dans le bandeau **Fiches Access ignorées** (identifiant, nom si connu, rendez-vous et raison, jamais de texte médical). Les erreurs de champs ou de volume arrêtent toujours la recherche. La page serveur garde son comportement « tout ou rien », car son actualisation retire les alertes absentes.
- **Colonne Échéance FO** : date calculée depuis la consigne (« dans 6 mois », « dans cinq ans », « 1 an 1/2 »…) ou, pour le suivi annuel diabète/HTO, un an après le dernier FO retrouvé. Pour la règle des 6 ans, la date de début de préparation (5 ans) est affichée dessous. **Trier par → Échéance FO** classe les échéances les plus proches en premier ; les lignes sans échéance écrite viennent en dernier.
- **Relecture** : l’enregistrement relit uniquement la chronologie du patient concerné, avec le rendez-vous de la recherche. Il reste possible après l’heure du rendez-vous.

### Avis de l’IA locale

**Consulter** propose aussi **Demander à l’IA si un FO est à faire** : un avis Oui / Non / Incertain du modèle local `qwen2.5:7b`, avec citations du dossier, raison d’un FO antérieur et questions fermées. Seuls les patients déjà repérés par la recherche y ont accès, un à la fois, et l’avis ne change jamais l’état. Détails : [IA locale](ia-locale-ollama.md#avis-de-lia-dans-la-fiche-patient).

### Délais reconnus

Chiffres (jusqu’à 3 : « dans 120 jours ») et nombres en lettres : un/une, deux, trois, quatre, cinq, six, sept, huit, neuf, dix, onze, douze, quinze, dix-huit, vingt-quatre, trente-six. Les demis sont acceptés : « 1 an 1/2 », « 1an1/2 », « un an et demi » (18 mois), « 6 mois et demi » (6 mois + 15 jours). « FAIRE FO DE CTRL » et « Ctrl FO dans 6 mois » sont des consignes ; un « => REVOIR DANS 1 AN » isolé de la même consultation leur donne leur échéance.

### Baisse d’acuité

`Shared/Fundus/FoAcuity.cs` lit l’acuité de loin en dixièmes dans Observation et REFRACTION : « OD 8/10 OG 10/10 », « 8/10 OD », et le format de réfraction du cabinet (« OD= -1,50 (-0,25 à 100°)= 10 /10 », ou « OD: » suivi de lignes de lentilles). L’œil indiqué en début de bloc s’applique aux lignes suivantes. « AB = 12 /10 » (acuité binoculaire), ODG et AO ne sont attribués à aucun œil. La vision de près (« P 2 »), les dates (« 10/10/2030 ») et les mentions anciennes, familiales ou interrogatives sont ignorées. « 3-4 /10f » vaut 4/10 ; « noir », CLD, VBLM et PL valent « < 1/10 ».

Pour chaque œil, la meilleure valeur d’une consultation est comparée à la meilleure valeur de la consultation précédente qui mesurait cet œil. Une perte d’au moins **3/10** à la dernière consultation donne le motif « Baisse d’acuité : OD 10/10 le … → 6/10 le … FO à discuter. » :

- sans autre motif, ou si le FO était seulement « probablement réalisé », le patient passe en **À vérifier** ;
- une consigne FO existante garde son état ; la baisse est ajoutée au motif ;
- un FO réalisé ou codé lors de cette même dernière consultation répond à la baisse : rien n’est ajouté.

Une acuité mesurée uniquement sans correction peut paraître basse : c’est une piste de relecture, pas un diagnostic.

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

Le serveur propose aussi un [registre de tous les patients pour le diabète et l’HTO](registre-patients.md),
avec import complet d’Access, états distincts de la préparation FO et corrections manuelles conservées.

Les expressions sont centralisées dans `Shared/Fundus/FundusAnalysis.cs`, `FoClinicalContext.cs`, `PatientConditions.cs` et `FoAnnualFollowUp.cs`, avec des tests positifs, négatifs et ambigus. La recherche ignore casse, accents, ligature œ et variantes FO / F.O. Les extraits affichés conservent le texte original.

- **FO à prévoir** : instruction explicite non clôturée.
- **À vérifier** : formulation conditionnelle, incertaine, chronologie ambiguë, consultation ultérieure sans preuve claire de réalisation, bilan maculaire/comparatif de la dernière consultation, échéance future ou contradiction entre texte et acte CCAM, ou suivi annuel lié à un contexte diabétique/HTO documenté.
- **FO probablement réalisé** : mention explicite de réalisation dans la chronologie de la demande, à confirmer si nécessaire.

Les consignes « refaire FO », « refaire l’examen des FO », « recontrôler les FO », « FO dans 6 mois », « REFAIRE V3FODG » et les variantes espacées V3 FODG sont reconnues. Les variantes DS, MOS, mois/an et les retours à la ligne entre action, examen et délai sont acceptés. Une consigne de recontrôle de la périphérie, accompagnée d’un contexte rétinien/FOD/FOG dans la même consultation, est prise en compte ; sa forme conditionnelle reste **À vérifier**. Les chiffres de réfraction, douleurs oculaires ou migraines seuls ne déclenchent pas de préparation.

Les délais explicites en jours, semaines, mois ou années sont calculés depuis la date de la consultation source et comparés **à la date du rendez-vous**. Un « revoir dans … » isolé de la même consultation peut donner le délai d’une consigne FO ; un délai attaché au tonus, à l’OCT ou à un autre examen ne lui est pas transféré. Des délais contradictoires restent à vérifier. L’échéance calculée apparaît dans le motif, avec le texte d’origine.

**Règle demandée par le cabinet : une consigne de contrôle à 6 ans est classée FO à prévoir dès 5 ans**, si elle reste explicite et non clôturée. L’échéance initiale à 6 ans et la date de début de préparation à 5 ans restent affichées. Les autres intervalles ne sont pas avancés. Avant la période de préparation, l’instruction reste visible en **À vérifier** ; une chronologie incertaine ou une instruction conditionnelle n’est pas promue automatiquement. Aucun rendez-vous, message, acte ni dossier Access n’est modifié.

Un compte rendu **OCT / FOD / FOG normal**, y compris « OCT non dilaté » ou « sans dilater », et un « revoir dans 1 an » générique ne suffisent plus à signaler un nouveau FO. Un bilan maculaire ou comparatif peut proposer une relecture lorsqu’il figure dans **la dernière consultation** ; le bilan comparatif nécessite un contexte OCT/FOD/FOG associé. Un ancien contexte faible ne supplante pas une consultation plus récente. En revanche, une ancienne consigne explicite n’est pas effacée par le silence ou un compte rendu normal ultérieur : elle devient à vérifier jusqu’à une réalisation ou une annulation documentée. Les règles annuelles diabète/HTO restent indépendantes de l’aspect normal d’un compte rendu.

### Actes CCAM et consultations récentes

Le lecteur repère dans le schéma réel `Consultation.Code acte`, `Tarif.Code acte` et `Tarif.ACTE` (casse ignorée). Il utilise une jointure externe gauche **Consultation.Code acte → Tarif.Code acte** : l’identifiant local sert au rapprochement, les codes CCAM viennent uniquement de **Tarif.ACTE**. Une consultation sans acte ou sans correspondance reste analysée. Si la liaison n’est pas disponible, l’analyse des textes continue et le client l’indique. Une clé Tarif dupliquée provoquant plusieurs lignes pour une consultation interrompt la recherche sans enregistrer de liste partielle ; aucun doublon n’est choisi arbitrairement.

Les codes exacts BGQP002, BGQP003 et BGQP004 décrivent des examens du fond d’œil dans le [chapitre 2 de la CCAM publié par l’ATIH](https://www.atih.sante.fr/sites/default/files/public/content/1966/Chap2_1.pdf). **BZQK001 désigne l’OCT dans cette nomenclature ; le cabinet a explicitement demandé de l’utiliser comme son code OCT-FO.** Cette convention locale est prise en compte dans la préparation, sans prétendre qu’un OCT prouve universellement un FO dilaté ou un examen complet de la périphérie. Il ne s’agit pas d’une validation de facturation. Les codes combinés (par exemple séparés par + ou /) sont reconnus, normalisés et dédupliqués ; les codes inconnus n’ont aucun effet automatique sur l’état FO.

Un acte FO/OCT-FO enregistré dans une consultation ultérieure peut classer une ancienne consigne en **FO probablement réalisé**, avec la date et le code visibles, et servir de repère au suivi annuel. Un code ne supprime pas une nouvelle consigne de contrôle. Une contradiction avec un texte « non réalisé », un refus ou une annulation reste **À vérifier**. Des consultations distinctes de même date/heure ne permettent pas d’inventer leur ordre. Les consultations datées après le moment de l’analyse sont exclues, même si le rendez-vous est futur. L’absence de code ne prouve ni réalisation, ni absence de besoin.

La dernière consultation analysée est mentionnée dans le motif, même lorsque l’instruction initiale est ancienne. Les consultations et actes contribuent à l’empreinte de relecture ; une correction de Tarif.ACTE ou de Code acte invalide une validation devenue obsolète. Le changement de règles invalide les anciennes empreintes. Le registre serveur diabète/HTO continue à lire seulement les deux champs cliniques, sans dépendre de Tarif.

Le cabinet demande aussi de repérer le **suivi annuel du FO pour les patients diabétiques ou avec hypertension oculaire (HTO)**. Une mention dans Observation ou REFRACTION suffit à proposer **À vérifier**, même sans consigne de FO : diabète/diabétique, DID/DNID, contexte de rétinopathie diabétique, HTO, hypertension/hypertonie oculaire ou tension oculaire explicitement élevée. La raison « Suivi annuel du FO » et l’extrait sont visibles dans le tableau. « Pas de rétinopathie diabétique » ne signifie pas « pas de diabète » et conserve cette piste de relecture. Il s’agit de la règle de suivi demandée par le cabinet, pas d’un diagnostic ni d’une prescription automatique.

La dernière mention de chaque contexte est conservée au fil des consultations silencieuses. Les négations, corrections, suspicions seules et antécédents familiaux ne permettent pas d’établir ce contexte chez le patient. Une correction explicite retire le contexte concerné ; une annulation explicite de FO neutralise les contextes antérieurs jusqu’à une nouvelle mention. Une consigne explicite, notamment un contrôle à six mois, reste prioritaire sur la règle annuelle. Lorsqu’il existe plusieurs motifs dans une même consultation, ils partagent une seule ligne ; l’extrait et la raison se rapportent à la consultation source affichée.

Si une réalisation explicite non historique ou un acte FO/OCT-FO non contradictoire est retrouvé, le motif annuel seul reste suspendu pendant un an à compter de **la date de cette consultation**, puis peut réapparaître en **À vérifier**, y compris après un ancien épisode « FO probablement réalisé ». Cette date est un repère de relecture, pas une date d’examen reconstituée. Le seuil est évalué à la date du rendez-vous à préparer ; aucune convocation ni modification de rendez-vous n’est faite. Sans réalisation clairement datée, la raison demande de vérifier la date du dernier FO. Les comptes rendus FOD/FOG ou OCT seuls ne prouvent pas cette réalisation. La réapparition annuelle invalide une ancienne validation de l’épisode terminé, tout en conservant une empreinte stable entre deux recherches identiques.

L’hypertension artérielle (HTA), un chiffre de tonus, un médicament ou une surveillance de cataracte seuls ne déclenchent pas le motif annuel. Les mentions historiques et négatives d’imagerie sont écartées ; « OCT » n’est pas recherché dans les noms de médicaments et une date telle que « 23 oct. 2026 » n’est pas assimilée à l’examen. Les tests utilisent des extraits synthétiques courts, sans recopier de dossiers, mesures ou traitements réels dans le dépôt.

Une négation de réalisation ne clôture pas une demande. Une annulation explicite ultérieure la retire. Une nouvelle consigne peut rouvrir une demande après une réalisation. Une mention historique de réalisation ne suffit pas à clôturer une consigne récente. Observation et REFRACTION décrivent une même consultation : une réalisation courante dans un champ ne clôture pas une consigne explicitement future dans l’autre ; des consignes contradictoires restent à vérifier. Les règles ne constituent ni une interprétation exhaustive du langage médical, ni une décision médicale. Elles doivent être vérifiées sur des exemples représentatifs du cabinet avant utilisation quotidienne.

La liste principale montre **FO à prévoir** et **À vérifier**. Les cas **FO probablement réalisé** restent accessibles par le filtre d’état. Le tableau présente le nom, l’identifiant, le rendez-vous, la consigne et l’état ; les détails de suivi sont repliables.

**Consulter** dans le client, ou **Lire la consultation source** dans la page serveur, relit Observation et REFRACTION directement dans Access. Les deux champs sont affichés séparément en texte lisible. `Shared/Fundus/FoMedicalText.cs` convertit automatiquement les champs RTF et HTML avant l’analyse et avant l’affichage : aucune « version texte » distincte n’est nécessaire dans la base. Le texte brut est conservé tel quel ; les paragraphes, accents ANSI/Unicode, listes et cellules de tableau des formats enrichis sont restitués en texte.

La conversion utilise RtfPipe et HtmlAgilityPack, entièrement en mémoire sur le poste ou le serveur. Elle n’ouvre aucun navigateur, ne charge aucun lien, image, feuille de style ou pièce jointe et n’envoie aucun compte rendu à un service externe. Les instructions de mise en forme, métadonnées, commentaires, scripts, textes explicitement masqués et textes barrés/supprimés sont exclus. Les styles externes/classes CSS ne sont pas calculés et les images ne font pas l’objet d’une reconnaissance de caractères. Le résultat est affiché comme texte par WinUI, et encodé par Razor sur le serveur ; aucune balise issue du dossier n’est exécutée.

Le convertisseur limite la taille et la profondeur des documents et vérifie la structure RTF avant analyse. Un document invalide, trop imbriqué ou trop volumineux provoque un message explicite sans enregistrer de résultats partiels ; les anciennes lignes restent identifiées comme non actualisées. Les erreurs du parseur ne sont ni affichées ni journalisées, car elles peuvent contenir le texte médical. La base Access reste strictement en lecture seule.

## SQLite et actualisation de la page serveur

`FoSettings` conserve la correspondance des champs. `FoAlerts` conserve la clé de source, l’identifiant et le nom du patient, son prochain rendez-vous, la clé/date de consultation source, un extrait de 600 caractères maximum, l’état, un éventuel extrait ultérieur, l’empreinte de la chronologie et les dates d’analyse/relecture. Les comptes rendus complets ne sont pas recopiés.

La création des tables est idempotente et compatible avec un `chat.db` existant. Une contrainte unique `(SourceKey, PatientId)` évite les doublons. Une actualisation est appliquée en transaction après la lecture et l’analyse complètes ; un échec ne remplace pas l’ancienne liste par un résultat incomplet. Les rendez-vous déplacés ou annulés disparaissent de la période actualisée. Les autres périodes nécessitent leur propre actualisation.

Une validation manuelle garde son auteur et sa date. Elle est conservée si les rendez-vous changent sans franchir une échéance de préparation, mais réinitialisée si les consultations, actes, règles ou l’éligibilité temporelle ont changé. Une validation envoyée depuis un affichage dont l’empreinte est dépassée est refusée. Un changement de chemin ou de clés de rapprochement crée une autre source ; les anciennes alertes ne sont pas mélangées. Les configurations historiques utilisant ProchainRDV sont corrigées automatiquement vers Observation + REFRACTION lors d’une recherche réussie. Ce changement est enregistré dans la même transaction que les résultats ; une erreur de lecture laisse les réglages précédents intacts. Les validations des patients réanalysés sont conservées lorsque la source, l’ensemble des preuves et l’éligibilité temporelle sont inchangés.

Les lectures et écritures passent par la page Razor authentifiée ; aucune observation n’est publiée sur le hub du chat. Les formulaires utilisent la protection anti-CSRF existante de Razor. Le chargement automatique soumet une seule fois le formulaire de recherche protégé ; aucun GET n’enregistre d’analyse. Les réponses à cette recherche, les vues de configuration/source et les erreurs ne déclenchent pas de boucle de recherche. Les réponses médicales sont `no-store`, sans journalisation des observations ni des erreurs brutes du fournisseur. Aucun service externe d’analyse n’est utilisé.

## Vérification et limites

- `dotnet run --project tests/Fundus.Tests` : moteur, chronologie, schéma, mises à jour SQLite, validations, authentification Razor et anti-CSRF, filtres natifs, annulation/rechargement de page, réentrance du gestionnaire d’erreur et conversion RTF/HTML (accents, négations, paragraphes, contenu masqué, liens et limites), avec données fictives.
- `dotnet run --project tests/SmsMail.Tests` : régressions du module SMS/mail.
- `dotnet build Serveur/Serveur.csproj --configuration Release` : serveur et vues Razor.
- Le workflow Windows compile aussi le client WinUI.

La lecture de la véritable base exige Windows/ACE et l’accès au fichier du cabinet, indisponibles dans l’environnement de développement. Pas de tâche planifiée : le client attend une recherche explicite ; seule la page d’administration serveur conserve sa recherche à l’ouverture après connexion/configuration. Période maximale : 93 jours ; maximum de 1 000 patients par actualisation et 10 000 consultations par patient. Les formats RTF/HTML usuels sont convertis automatiquement ; les variantes réelles du cabinet restent à vérifier sur le poste. Limites par champ : 1 000 000 de caractères source, 100 000 caractères de texte extrait et 64 niveaux d’imbrication. Le lecteur commun limite les textes cumulés Observation + REFRACTION à 100 000 caractères par consultation et 5 000 000 par patient.


### Validation du rapprochement et de la chronologie

Les tests utilisent des consultations et tarifs synthétiques : codes multiples, actes absents/non rapprochés, jointure externe, clé Tarif dupliquée, code local distinct du CCAM, actes plus récents, dates futures/identiques, contradictions, renouvellement après réalisation, échéances au rendez-vous et règle 6 ans/5 ans. La requête SELECT commune est exécutée sur une base SQLite temporaire pour vérifier les liaisons et les paramètres ; l’exécution Jet/ACE sur la base réelle doit encore être vérifiée sur un poste Windows. Aucun dossier fourni par l’utilisateur n’est recopié dans les tests. Le fournisseur local Jet/ACE reste celui choisi dans les paramètres du poste.
