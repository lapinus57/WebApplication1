# Déployer le serveur ChatServeur en tant que service Windows

Ce guide explique comment publier le projet **Serveur**, l'installer comme un service Windows qui démarre automatiquement, le mettre à jour et vérifier son état. Toutes les commandes sont à exécuter dans un terminal PowerShell lancé **en tant qu'administrateur**.

## 1. Publier le serveur pour Windows

1. Ouvrez un terminal à la racine du dépôt.
2. Lancez la publication autoportée pour Windows 64 bits :

   ```powershell
   dotnet publish .\Serveur\Serveur.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
   ```

   Les fichiers publiés se trouvent dans `Serveur/bin/Release/net8.0/win-x64/publish`.

## 2. Configurer l'administration HTTPS

Le serveur refuse volontairement de démarrer tant qu'un compte administrateur robuste et un certificat HTTPS n'ont pas été fournis. Le hub SignalR reste disponible en HTTP sur le port `5000` pour les clients existants, tandis que l'interface d'administration utilise exclusivement HTTPS sur le port `5443`.

1. Créez ou fournissez un certificat PFX correspondant au nom DNS du serveur. Par exemple :

   ```powershell
   $certificate = New-SelfSignedCertificate -DnsName "eyechat-server" -CertStoreLocation "cert:\LocalMachine\My"
   $certificatePassword = Read-Host "Mot de passe du certificat" -AsSecureString
   Export-PfxCertificate -Cert $certificate -FilePath "C:\ProgramData\ChatServeur\admin.pfx" -Password $certificatePassword
   ```

2. Faites approuver ce certificat par les postes qui ouvrent l'administration, ou utilisez un certificat délivré par l'autorité de certification de votre organisation.
3. Configurez les secrets comme variables d'environnement système, sans les enregistrer dans le dépôt :

   ```powershell
   setx /M Administration__Username "administrateur"
   setx /M Administration__Password "utilisez-un-mot-de-passe-unique-et-long"
   setx /M Administration__HttpsCertificatePath "C:\ProgramData\ChatServeur\admin.pfx"
   setx /M Administration__HttpsCertificatePassword "mot-de-passe-du-pfx"
   ```

Le mot de passe d'administration doit contenir au moins 12 caractères. L'interface sera ensuite disponible à l'adresse `https://eyechat-server:5443`. Une requête HTTP vers une page d'administration est automatiquement redirigée vers cette adresse sécurisée.

## 3. Installer le service Windows

1. Copiez le script `ChatServeurService.ps1` dans le dossier de publication ou gardez-le dans le dépôt.
2. Exécutez la commande suivante (adapter le chemin de `-SourcePath` si besoin) :

   ```powershell
   cd .\Serveur\Deployment
   ./ChatServeurService.ps1 -Action Install -SourcePath "..\bin\Release\net8.0\win-x64\publish"
   ```

   Le script :

   - copie les fichiers publiés vers `C:\Program Files\ChatServeur` (modifiez `-InstallPath` pour un autre emplacement) ;
   - crée un service Windows nommé **ChatServeur** configuré en démarrage automatique ;
   - démarre le service immédiatement ;
   - masque la console lors de l'exécution grâce au type d'application Windows (`WinExe`).

   > ℹ️ Le service écoute sur `http://0.0.0.0:5000` pour les clients EyeChat et sur `https://0.0.0.0:5443` pour l'administration.

## 4. Mettre à jour le service (avec sauvegarde automatique)

Lorsqu'une nouvelle version est publiée, relancez le script avec l'action `Update` en pointant vers le nouveau dossier de publication :

```powershell
./ChatServeurService.ps1 -Action Update -SourcePath "..\bin\Release\net8.0\win-x64\publish"
```

Le script :

- arrête le service ;
- réalise une sauvegarde horodatée de l'installation actuelle dans `C:\Program Files\ChatServeur\Backups` (conservez les `BackupRetention` dernières, 5 par défaut) ;
- copie les nouveaux fichiers ;
- redémarre le service.

Pour revenir à la version précédente, utilisez l'action `Rollback` sans paramètre (restauration de la dernière sauvegarde) ou en spécifiant un chemin de sauvegarde particulier :

```powershell
# revenir à la dernière sauvegarde
./ChatServeurService.ps1 -Action Rollback

# revenir à une sauvegarde précise
./ChatServeurService.ps1 -Action Rollback -SourcePath "C:\\Program Files\\ChatServeur\\Backups\\20240508-235959"
```

## 5. Désinstaller le service

Pour supprimer complètement le service et les fichiers :

```powershell
./ChatServeurService.ps1 -Action Uninstall
```

## 6. Vérifier l'état du service

- Obtenir l'état courant :

  ```powershell
  Get-Service ChatServeur
  ```

- Consulter les journaux (visibles dans l'observateur d'événements sous **Journal des applications**). Pour une supervision plus poussée, envisagez d'ajouter une solution de logging centralisée (Serilog, Elastic Stack, etc.).

## 7. Mettre en place les mises à jour automatiques via GitHub

Le script peut vérifier une publication GitHub chaque nuit à minuit et mettre à jour automatiquement le service si une nouvelle version est disponible.

1. Préparez une release GitHub contenant une archive ZIP de la publication Windows (le fichier doit correspondre au motif `ChatServeur-win-x64.zip` par défaut).
2. Configurez la tâche planifiée automatique :

   ```powershell
   ./ChatServeurService.ps1 -Action ConfigureAutoUpdate -GitHubRepo "MonOrganisation/MonDepot"
   ```

   - `-GitHubRepo` doit utiliser le format `Organisation/Depot`.
   - Si l'archive porte un autre nom, ajustez `-AssetPattern` (expression régulière PowerShell).
   - Pour un dépôt privé, fournissez un jeton personnel via `-GitHubToken`.
   - Modifiez `-BackupRetention` pour définir le nombre de sauvegardes conservées.

3. Chaque nuit à 00h00, le Planificateur de tâches :

   - contacte GitHub pour récupérer la dernière release ;
   - compare le tag à la version actuellement installée ;
   - télécharge et déploie automatiquement la nouvelle archive si besoin ;
   - sauvegarde l'ancienne installation avant de redémarrer le service (restaurable via `Rollback`).

Pour tester immédiatement la logique automatique sans attendre minuit, exécutez :

```powershell
./ChatServeurService.ps1 -Action CheckForUpdates -GitHubRepo "MonOrganisation/MonDepot"
```

Pour désactiver la tâche planifiée :

```powershell
./ChatServeurService.ps1 -Action DisableAutoUpdate
```

## 8. Conseils supplémentaires

- Vérifiez que les ports 5000 (clients) et 5443 (administration HTTPS) sont ouverts sur le réseau local.
- Pour changer le nom ou l'emplacement d'installation, utilisez les paramètres `-ServiceName`, `-DisplayName` et `-InstallPath` du script.
- Les fichiers SQLite sont créés dans un sous-dossier `data` du répertoire d'installation. Sauvegardez ce dossier avant les mises à jour si vous ne disposez pas d'une sauvegarde automatique.
