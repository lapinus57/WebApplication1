# FbExplore — structure d'une base Firebird (.fdb)

Ouvre une **copie** d'une base Firebird (ici celle du logiciel de carte Vitale) en **lecture seule** et liste
**uniquement sa structure** : tables, colonnes, types, nombre de lignes. **Aucune donnée patient** n'est lue
(seulement les tables système de Firebird et des `COUNT(*)`) : le rapport `structure-….txt` peut être transmis.

Il met en tête les colonnes probablement utiles pour les familles et les coordonnées : ayants droit, assuré,
qualité, rang, NIR, naissance, téléphone, mail, adresse… C'est ce qui dira si la base peut enrichir FamilyScan.

## Copier la base

Faire la copie du `.fdb` **logiciel Vitale fermé** (une copie pendant l'utilisation peut être incohérente).
Ne jamais lancer l'outil sur la base en service.

## Version de Firebird

Au démarrage, l'outil lit dans l'en-tête du fichier la version de Firebird qui l'a créé (sans l'ouvrir) :
`Firebird 2.x`, `Firebird 3` ou `Firebird 4 ou 5`. Il faut un moteur de la même famille pour l'ouvrir.

Deux façons :

1. **Mode embarqué (le plus simple, rien à installer)** : télécharger sur firebirdsql.org le **« Zip kit »** de la
   version indiquée (32 ou 64 bits comme l'outil publié), et copier son contenu (`fbclient.dll` et les dossiers
   `plugins`, `intl`… pour Firebird 3+, ou `fbembed.dll` pour Firebird 2.5) **dans le dossier de `FbExplore.exe`**.
   L'outil les trouve seul.
2. **Serveur Firebird** : si un service Firebird de la bonne version tourne sur le PC (souvent installé par le
   logiciel Vitale), lancer avec `--serveur localhost`.

## Compiler

```bash
dotnet publish tools/FbExplore/FbExplore.csproj -c Release -r win-x64 --self-contained true -o D:\FbExplore
```

(`-r win-x86` si le kit Firebird téléchargé est en 32 bits : l'outil et `fbclient.dll` doivent avoir la même architecture.)

## Lancer

Déposer la copie du `.fdb` (et le kit embarqué) dans le dossier de `FbExplore.exe`, puis double-cliquer : il trouve
la base, demande l'utilisateur (`SYSDBA` par défaut) et le mot de passe (vide = `masterkey`, valeur par défaut de
Firebird ; sinon celui de l'installation du logiciel Vitale). Ou :

```bash
FbExplore.exe "D:\FbExplore\base.fdb" --client "D:\FbExplore\fbclient.dll" --utilisateur SYSDBA --motdepasse MOT
```

Options : `--serveur hôte`, `--port 3050`, `--charset WIN1252` (si des noms de colonnes accentués s'affichent mal),
`--sortie fichier.txt`.

## Tests

```bash
dotnet run --project tests/FbExplore.Tests
```
