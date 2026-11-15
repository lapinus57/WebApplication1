# MdbCompare — comparer deux bases Access

Petit logiciel Windows qui compare deux fichiers `.mdb` (ou `.accdb`) **en lecture seule** : aucune des deux bases n’est modifiée.

## Ce qu’il compare

1. **Les tables** : présentes dans les deux bases, ou seulement dans l’une.
2. **La structure** de chaque table commune : colonnes ajoutées/supprimées, type ou longueur changés, clé primaire différente.
3. **Les données** (case « Comparer aussi les données », cochée par défaut) :
   - si la table a la même clé primaire des deux côtés, les lignes sont rapprochées par cette clé : lignes **seulement dans 1**, **seulement dans 2**, et lignes **modifiées** avec, pour chaque colonne changée, l’ancienne et la nouvelle valeur ;
   - sans clé primaire, les lignes sont comparées entières (doublons compris).
   Seules les colonnes communes aux deux bases sont comparées. L’option « Ignorer majuscules/minuscules et espaces de fin » évite de signaler `Metz ` et `METZ` comme différents.

Les 1000 premières différences de chaque table sont listées ; les totaux restent exacts.

Les lignes sont lues **en flux, une table à la fois** : seule une empreinte de 16 octets par ligne de la base 2 est gardée en mémoire, ce qui permet de comparer de grosses bases (comptes rendus en Mémo, images) même en 32 bits. Une ligne n’est relue que pour afficher le détail d’une différence.

## Utilisation

1. Lancer `MdbCompare.exe`.
2. Choisir la **base 1** et la **base 2** (bouton *Parcourir…* ou glisser-déposer le fichier).
3. Si la base est protégée par un groupe de travail, indiquer le fichier `.mdw`, l’utilisateur et le mot de passe (utilisés pour les deux bases).
4. Cliquer sur **Comparer**. La liste de gauche montre les tables différentes ; cliquer sur une table affiche le détail à droite.
5. **Exporter le rapport…** enregistre un rapport `.txt` lisible, ou un `.csv` à ouvrir dans Excel.

## Compiler

Avec Visual Studio ou le SDK .NET 8 sous Windows :

```bash
dotnet build tools/MdbCompare/MdbCompare.csproj -c Release -p:Platform=x86
```

Le programme doit avoir **la même architecture que le moteur Access (ACE)** installé : `x86` pour Office/Access 32 bits (le plus courant), `-p:Platform=x64` pour Office 64 bits. Sinon l’ouverture échoue avec « Aucun fournisseur Access OLE DB compatible » (voir `docs/depannage-access.md`). Les fournisseurs ACE 16.0, ACE 12.0 puis Jet 4.0 sont essayés dans cet ordre, comme dans le reste d’EyeChat.

Les vérifications de la comparaison (sans Access) : `dotnet run --project tests/MdbCompare.Tests`.
