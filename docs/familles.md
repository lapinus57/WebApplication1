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

Si le serveur ne répond pas, l'onglet fonctionne comme avant (la ligne d'état indique « familles du serveur indisponibles »).
Les familles de la base EyeEmail, si elle est utilisée, gardent la priorité.
