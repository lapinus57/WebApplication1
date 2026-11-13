# Base patients EyeChat (EyeMail)

EyeChat a sa propre base de patients, sur le serveur : `data\eyemail.db`. C'est un fichier séparé de `chat.db`, qui sert de point de départ pour remplacer la base de l'application EyeEmail (familles, coordonnées, historique des envois).

## Première table : tous les patients

La table `Patients` recopie la table **Patients** de la base Access du cabinet. Access n'est jamais modifié.

| Champ | Colonnes Access reconnues |
|---|---|
| Identifiant | Code Patient, CodePatient, IDPatient… |
| Civilité, nom, prénom, nom de jeune fille | Titre/Civilité, Nom, Prénom, Nom jeune fille |
| Date de naissance | Date de naissance, DateNaissance, DDN… |
| Adresse, complément, code postal, ville | Adresse, Adresse 2, Code postal/CP, Ville (une colonne « CP Ville » comme « 57000 Metz » est séparée) |
| Téléphones et leurs remarques | Tel 1, Tel 1 Info, Tel 2/Portable, Tel 2 Info |
| E-mail et sa remarque | E-mail, Email, Courriel…, Email Info |

Les colonnes sont reconnues d'après leur nom, sans réglage. La comparaison ignore la casse, les accents et les espaces (« Code patient », « NOM » conviennent). Aucune colonne médicale (observation, réfraction…) n'est recopiée.

La table lue est, dans l'ordre :
1. celle indiquée par `EyeMail:PatientsTable` ;
2. sinon celle choisie dans les **Réglages Access** du registre « Patients · Diabète / HTO », dont l'identifiant, le nom et le prénom sont alors repris tels quels ;
3. sinon la table `Patients`.

Les tables liées à une base dorsale sont acceptées.

Chaque patient garde aussi sa date de création, sa dernière modification, la date du dernier import où il était présent, et s'il est encore dans Access.

## Utilisation

1. Ouvrir l'administration : **Base patients EyeMail** (`https://serveur:5443/EyeMail`).
2. Cliquer **Importer / actualiser tous les patients**. L'import se fait en arrière-plan sur le serveur, avec les réglages Access déjà utilisés par le serveur (paramètres RDV ou `FoAccess:DatabasePath`).
3. Rechercher par nom, prénom, nom de jeune fille ou identifiant Access.

À chaque import :
- **Patient nouveau :** il est ajouté.
- **Patient modifié dans Access :** il est mis à jour. Les autres patients ne sont pas réécrits.
- **Patient qui n'est plus dans Access :** il est gardé et marqué « absent du dernier import », jamais supprimé.
- **Identifiant en double, ou erreur de lecture :** rien n'est modifié.

Chaque import est noté dans la table `ImportRuns` (date, table lue, nombre de patients ajoutés, modifiés et absents).

## Sauvegarde et protection

`eyemail.db` contient des données personnelles (coordonnées). Elle est sauvegardée chaque jour, comme `chat.db`, dans `data\sauvegardes\eyemail-AAAA-MM-JJ.db` (14 jours gardés). La page n'est accessible qu'après connexion à l'administration, en HTTPS.
