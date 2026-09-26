# EyeChat ne se lance pas après l'installation

## Visual Studio ne peut pas se connecter au serveur Web « http »

Les profils de développement démarrent le serveur et ouvrent l'administration
sur `http://localhost:5000`. Aucun certificat n'est nécessaire pour une
exécution locale depuis Visual Studio. Les identifiants réservés au poste de
développement sont `admin` / `development-only` ; ces valeurs ne sont pas
utilisées lors d'un déploiement.

Si Visual Studio signale encore que le serveur Web ne fonctionne plus, fermez
les anciennes instances de `Serveur.exe`, supprimez les dossiers `Serveur\bin`
et `Serveur\obj`, puis reconstruisez le projet. Vérifiez aussi que le profil
**http** ou **https** est sélectionné plutôt que le lancement direct de
l'exécutable compilé.

Pour une installation réelle,
configurez toujours les identifiants et le certificat PFX comme indiqué dans le
[guide de déploiement](deploiement-serveur-windows.md) ; le serveur continue de
refuser une configuration de production incomplète.

## « Erreur d'analyse du package de l'application »

Ce message est affiché par Windows avant le lancement d'EyeChat : réparer ou
réinitialiser l'application ne peut donc pas le corriger. Supprimez le fichier
téléchargé, puis téléchargez de nouveau le fichier `.msix` correspondant à votre
PC depuis la page **Releases**. N'essayez pas d'installer une page web enregistrée
avec une extension `.msix`, un fichier `.xml` du dossier `BundleArtifacts`, ni le
contenu décompressé de l'archive de code source.

Vérifiez ensuite les points suivants :

1. Le fichier a une taille cohérente et son nom se termine réellement par
   `.msix` (et non par `.msix.html` ou `.msix.xml`).
2. Dans **Propriétés > Signatures numériques**, la signature est présente et
   valide. Installez le certificat `.cer` fourni avec la même version dans
   **Ordinateur local > Personnes de confiance** si Windows ne fait pas confiance
   au certificat de test.
3. Utilisez `x64` sur la majorité des PC Intel/AMD, `ARM64` sur Windows ARM et
   `x86` uniquement sur Windows 32 bits.

Si le message persiste, ouvrez PowerShell et exécutez :

```powershell
Add-AppxPackage -Path .\Client_<version>_x64.msix
```

Conservez le code d'erreur complet (`0x...`) retourné par PowerShell : il permet
de distinguer un téléchargement incomplet, un manifeste invalide, une mauvaise
architecture et un problème de certificat.

## Vérifications rapides

1. Vérifiez que le PC utilise Windows 10 (1809 ou plus récente) ou Windows 11.
2. Installez le paquet correspondant à l'architecture du PC : `x64` pour la plupart des PC Intel/AMD, `ARM64` pour un PC Windows ARM, ou `x86` pour un ancien Windows 32 bits.
3. Ouvrez **Paramètres > Applications > Applications installées > EyeChat > Options avancées**, puis utilisez **Réparer**. Si nécessaire, essayez ensuite **Réinitialiser**.
4. Relancez EyeChat depuis le menu Démarrer.

> Pour la publication, ne réactivez pas `PublishTrimmed` pour le client WinUI :
> le rognage peut retirer des types chargés dynamiquement par XAML et produire un
> paquet qui s'installe mais se ferme au lancement. Chaque paquet doit aussi être
> publié avec le profil correspondant à son architecture (`win-x64`, `win-x86` ou
> `win-arm64`).

## Récupérer le diagnostic

À partir de la prochaine version, l'application installée écrit son journal dans
son dossier de données Windows :

```text
%LOCALAPPDATA%\Packages\<famille-du-package-EyeChat>\LocalState\EyeChat\app.log
```

Le chemin complet réellement utilisé est affiché dans le message d'erreur de
démarrage. Pour les versions antérieures, dont la version `0.1.32.0`, Windows
peut avoir redirigé `%LOCALAPPDATA%\EyeChat` dans le cache privé du paquet :

```text
%LOCALAPPDATA%\Packages\<famille-du-package-EyeChat>\LocalCache\Local\EyeChat\app.log
```

Pour retrouver le fichier sans connaître le nom de famille du paquet, exécutez
la commande suivante dans PowerShell :

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter app.log -Recurse -ErrorAction SilentlyContinue |
    Where-Object FullName -Match 'EyeChat|a919e7cf-8df8-4c86-843a-a89bdf523ccd' |
    Select-Object -ExpandProperty FullName
```

Ouvrez le chemin retourné, puis transmettez `app.log` au support. Une erreur
survenant pendant la création ou l'initialisation de la fenêtre affiche également
le chemin du log et le code `CLI26` ou `CLI28`. Une impossibilité d'activer les raccourcis clavier globaux
est enregistrée avec le code `CLI25`, mais elle ne bloque plus l'ouverture de
l'application.

## Si aucun journal n'est créé

L'échec se produit probablement avant l'exécution d'EyeChat. Vérifiez alors :

- que le certificat utilisé pour signer le paquet est approuvé et encore valide ;
- que l'architecture du paquet correspond au PC ;
- dans **Observateur d'événements > Journaux Windows > Application**, les erreurs `AppModel-Runtime`, `.NET Runtime` ou `Application Error` enregistrées à l'heure du lancement.

Joignez le détail de cette erreur, la version de Windows (`winver`) et l'architecture du PC à la demande de support.
