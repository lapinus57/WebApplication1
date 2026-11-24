# FamilyScan — reconstitution des familles (outil de mesure)

Lit **tout l'historique de l'agenda** (table `AG_rdv`) d'une **copie** de la base Access, en **lecture seule**,
et calcule quels patients sont liés et lesquels forment aujourd'hui un même foyer. Rien n'est modifié :
c'est un outil de **mesure**, avant d'intégrer la base des familles au serveur EyeChat.

## Indices utilisés, selon les années

| Période | Indices |
|---|---|
| 2001 → 2005-2006 | **NoSS commun** (les ayants droit partageaient le numéro de l'assuré), venues le même jour à des créneaux voisins, nom, adresse, téléphone |
| 2006 → fin 2007 | venues le même jour à des créneaux voisins, nom, adresse, téléphone, mail |
| fin 2007 → aujourd'hui | en plus : **RDV pris à la même minute** (« Pris le ») et **arrivés ensemble** (« Arrivé à ») |

Deux personnes à des créneaux voisins ne sont liées que si un autre indice les rapproche (même NoSS, même
réservation, téléphone, mail, nom ou adresse). L'heure d'arrivée seule ne suffit pas (beaucoup de patients
arrivent dans les mêmes minutes), et un RDV pris à la même minute **une seule fois**, sans nom, adresse ni contact
commun, reste un lien faible (report de RDV en série). Une famille est « à vérifier » si deux membres sont à trois
liens ou plus l'un de l'autre, si elle compte plus de 6 personnes, ou si un lien est faible. Une même adresse seule ne suffit jamais. Un
téléphone ou un mail présent chez plus de 6 patients (EHPAD, foyer, taxi…) est ignoré.

Pour chaque paire, l'outil donne une **force** (forte / moyenne / faible) et sa **raison**, sans nommer la
relation (conjoint, enfant…). **Même foyer aujourd'hui** = lien au moins moyen **et** venue commune depuis
moins de 3 ans ou téléphone / mail actuel commun. Une parenté ancienne (NoSS commun en 2003, jamais revenus
ensemble) reste un lien, mais ne regroupe pas les rappels.

## Données sensibles

- Le **NoSS** n'est lu que pour comparer deux dossiers : il n'est écrit dans **aucun** fichier.
- `rapport-anonymise.txt` : **aucun** nom, numéro de patient, NoSS, téléphone ni mail. Peut être transmis.
- `detail-familles-NE-PAS-DIFFUSER.csv` : nominatif (code patient, nom, téléphones et mails actuels,
  raisons). **À garder au cabinet**, à ouvrir dans Excel pour vérifier quelques familles.

## Compiler

```bash
dotnet publish tools/FamilyScan/FamilyScan.csproj -c Release -p:Platform=x86 -r win-x86 --self-contained true -o FamilyScan
```

`x86` pour Office / Access 32 bits (le plus courant) ; `-p:Platform=x64 -r win-x64` pour Office 64 bits.

## Lancer

**Par double-clic** : déposer la copie de la base (`.mdb` / `.accdb`) et, si elle est protégée, le fichier de groupe de
travail (`.mdw`) **dans le dossier de `FamilyScan.exe`**, puis double-cliquer sur `FamilyScan.exe`. Il les trouve
seul et demande l'utilisateur et le mot de passe Access (le mot de passe ne s'affiche pas). Ou en ligne de commande :

```bash
FamilyScan.exe "D:\Copie\Base.mdb"
```

Options :

- `--table AG_rdv` (par défaut) ;
- `--mdw chemin\system.mdw --utilisateur NOM --motdepasse MOT` si la base est protégée par un groupe de travail ;
- `--sortie dossier` (par défaut : `Familles-AAAA-MM-JJ-HHmm` dans le dossier courant) ;
- `--colonne role=NomDeLaColonne` si une colonne n'est pas reconnue. Rôles : `patient`, `date`, `heure`, `creation`,
  `arrivee`, `noss`, `nom`, `nomjf`, `prenom`, `adresse`, `ville`, `tel1`, `tel2`, `email`, `email2`, `annule`.

Au démarrage, l'outil affiche les colonnes reconnues (`noss → NoSS`, `creation → Pris le`…) : vérifier qu'elles
sont justes. Puis il lit l'agenda **année par année** (affichage de l'avancement) et écrit les deux fichiers.

## Tests

```bash
dotnet run --project tests/FamilyScan.Tests
```

## Ajouter la carte Vitale (logiciel Vitale, base Firebird)

Les fiches `BENEFICIAIRE` du logiciel Vitale disent quels patients sont sur **la même carte Vitale** et avec quelle
**qualité** (0 assuré, 2 conjoint, 4 concubin, 6 enfant ; 3, 5, 8 divorcé, séparé, veuf). Ce sont des liens
**déclarés** : forts, et nommés (« parent et enfant », « conjoints »). Un rattachement en cours regroupe les conjoints
et les enfants de **moins de 20 ans** ; un enfant plus âgé encore « actif » sur la carte (fiches rarement mises à jour)
n'est regroupé que si l'agenda le confirme (venue commune récente, téléphone ou mail commun). Des conjoints séparés,
divorcés ou veufs ne sont plus regroupés.

Aucun numéro n'est commun aux deux logiciels : chaque patient Vitale est rapproché d'un **Code Patient** de l'agenda par
**date de naissance + premier prénom + nom** (d'usage ou de naissance). En cas d'homonymes, il n'est pas rapproché.

### 1. Exporter depuis DBeaver

Exécuter cette requête sur la **copie** de la base Vitale. Remplacer `mot-secret` par un mot de votre choix : le
numéro de l'assuré n'est **jamais exporté**, seulement une empreinte (`HASH`) qui est la même pour tous les patients de
la carte.

```sql
SELECT p.PAT_ID, p.PAT_NOM AS NOM, p.PAT_NOMNAISSANCE AS NOM_NAISSANCE, p.PAT_PRENOM AS PRENOM,
       p.PAT_DATEHNAISSANCE AS NAISSANCE, HASH('mot-secret' || TRIM(b.BEN_NUMIMMATRI)) AS CARTE,
       b.BEN_QUALITE AS QUALITE, b.BEN_ISACTIVE AS ACTIF, b.BEN_DATEHCREATION AS CREATION,
       b.BEN_DATEFINVALIDITE AS FIN_VALIDITE
FROM PATIENT p JOIN BENEFICIAIRE b ON b.BEN_PAT_ID = p.PAT_ID
WHERE b.BEN_NUMIMMATRI IS NOT NULL AND TRIM(b.BEN_NUMIMMATRI) <> '';
```

Puis, dans les résultats : clic droit → **Exporter les données** → **CSV** (séparateur `;`, en-tête activé), fichier
nommé par exemple `export-vitale.csv`, enregistré **dans le dossier de `FamilyScan.exe`**.

Ce fichier contient des noms et des dates de naissance : **le garder au cabinet** et le supprimer après la mesure.

### 2. Lancer FamilyScan

Double-clic : l'outil trouve seul un fichier `*vitale*.csv` posé à côté de lui. Ou :

```bash
FamilyScan.exe "D:\FamilyScan\Base.mdb" --mdw "D:\FamilyScan\system.mdw" --utilisateur NOM --motdepasse MOT --vitale "D:\FamilyScan\export-vitale.csv"
```

La date de naissance des patients de l'agenda est lue dans `AG_rdv` si elle y est, sinon dans la table `Patients`
(autre nom : `--patients NomDeLaTable`). Le rapport ajoute une partie **Carte Vitale** : patients rapprochés, liens
déclarés par nature, liens **nouveaux grâce à Vitale**, et couples séparés que l'agenda seul **regroupait à tort**.
