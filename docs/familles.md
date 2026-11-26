# Familles reconstituées (serveur)

Le serveur EyeChat reconstitue les familles du cabinet à partir de **tout l'historique de l'agenda** (AG_rdv, depuis 2001),
de la **photo de la carte Vitale** (ancienne base AXI_DATABASE, importée une fois) et des **exports « suivi des paiements »**
de CGM e-Vitale (déposés de temps en temps). Le résultat sert à regrouper les rappels SMS et mail d'un même foyer.
Access n'est jamais modifié. Tout est dans `data\families.db` sur le serveur.

## Page « Familles » (administration du serveur)

- **Recalculer maintenant** : relit tout l'agenda (quelques minutes). Les familles en service restent utilisées jusqu'à la fin.
  Un recalcul a lieu aussi **chaque nuit à 3 h** ; autre heure : `"Families": { "NightlyHour": 2 }` dans `appsettings.json`
  (`-1` pour le désactiver).
- **Photo de la carte Vitale** : importer `export-vitale.csv` (même requête DBeaver que pour FamilyScan). Un nouvel import remplace l'ancien.
- **Exports des paiements** : ajouter chaque nouvel export CSV. Le NIR sert seulement à savoir qui est facturé sous la même
  carte : il est transformé en empreinte en mémoire, puis en simple numéro de groupe propre à l'export. **Il n'est jamais
  enregistré.** Un export peut être supprimé.
- **Familles à vérifier** : chaînes (des membres reliés seulement par un autre), plus de 6 personnes, ou lien faible. Pour chacune :
  les membres, la parenté dans un autre foyer, et **pourquoi** ils sont regroupés.
  - **Valider cette famille** : elle n'est plus à vérifier (tant que ses membres ne changent pas).
  - **Retirer du foyer** : la personne sort de cette famille.
  - **Regrouper** deux codes patient : ils seront mis ensemble.
  Les corrections sont gardées et appliquées à chaque calcul (prises en compte au prochain calcul).
- **Famille d'un patient** : saisir son code patient.
- **Questions simples** : pour chaque famille à vérifier, le serveur choisit les liens qui décident (le « pont » qui, s'il est
  faux, coupe la famille en deux ; les liens moyens) et pose la question « A et B vivent-ils ensemble aujourd'hui ? », avec
  leurs coordonnées, ce qui les relie et, pour un pont, les deux morceaux de la famille si la réponse est non.
  **Oui** / **Non** valent tout de suite pour les rappels et à chaque calcul ; **Je ne sais pas** ne change rien et retire la
  question. Une famille dont toutes les questions ont une réponse n'est plus à vérifier.
- **Journal anonymisé** (lien sous les questions) : toutes les questions et réponses, de la page et des postes, avec les
  numéros patients, l'année de dernière venue, toutes les relations de la famille et les colonnes de l'agenda reconnues.
  Aucun nom, téléphone ni adresse : à transmettre pour améliorer le moteur.

Indices pris dans l'agenda : venues communes, RDV pris à la même minute, heure d'arrivée, **heure de passage** en consultation
(colonnes « Passé à », « Heure passage »…), et **remarque** du RDV : une remarque qui cite le prénom d'un autre patient du
jour avec son nom ou un mot de famille (« avec sa fille Léa ») relie les deux.

Options facultatives (`appsettings.json`, section `Families`) : `AgendaTable` (sinon la table des paramètres RDV),
`PatientsTable` (table qui contient la date de naissance, si l'agenda n'en a pas ; sinon `Patients`).

## Onglet SMS et mail (postes)

Avec « Utiliser les familles » coché, le poste demande au serveur la famille des patients du jour :

- **famille sûre** : rappels regroupés, même si les personnes n'ont pas de téléphone en commun ; l'explication du regroupement
  indique « Famille connue du serveur » et les raisons ;
- **famille à vérifier** : une fenêtre montre qui, à quelle heure et pourquoi, et demande « Regrouper » ou « Garder séparés ».
  **La réponse est envoyée au serveur** et gardée : « Regrouper » note ces personnes comme étant ensemble, « Garder séparés »
  comme n'étant pas ensemble. La question n'est plus posée pour ces personnes, sur aucun poste, dès maintenant et après
  chaque recalcul. Si une autre personne de la famille a aussi rendez-vous ce jour-là, la question est reposée pour le groupe.
  Valider la famille entière reste possible sur la page « Familles ».
- le **pourquoi** ne cite que les personnes de la question ; quand elles ne sont reliées que par un autre membre de la
  famille, celui-ci apparaît comme « une autre personne de la famille (absente ce jour) ».

**Journal anonymisé** (Paramètres › Préparation des rappels et familles) : chaque question « Famille à vérifier » est notée
dans `%LocalAppData%\EyeChat\SmsMail\familles-questions.log` avec les **numéros patients au lieu des noms**, les heures, le
pourquoi affiché, tous les membres et toutes les relations de la famille, et la réponse. Aucun nom, téléphone ni mail :
ce fichier peut être transmis pour améliorer les questions.

Si le serveur ne répond pas, l'onglet fonctionne comme avant (la ligne d'état indique « familles du serveur indisponibles »).
Les familles de la base EyeEmail, si elle est utilisée, gardent la priorité.
