<#!
.SYNOPSIS
    Installs, updates or removes the ChatServeur Windows Service.

.DESCRIPTION
    This script copies the published ChatServeur server binaries to a target folder and
    registers them as a Windows Service that starts automatically with Windows. Use the
    Update action whenever you publish a new version to deploy the fresh binaries and
    restart the service without needing to recreate it. It can also download nightly
    updates from GitHub and conserve automatic backups to faciliter les retours arrière.

.EXAMPLE
    ./ChatServeurService.ps1 -Action Install -SourcePath "C:\\temp\\ChatServeur" -InstallPath "C:\\Program Files\\ChatServeur"

.EXAMPLE
    ./ChatServeurService.ps1 -Action Update -SourcePath "C:\\temp\\ChatServeur"

.EXAMPLE
    ./ChatServeurService.ps1 -Action Uninstall

.EXAMPLE
    ./ChatServeurService.ps1 -Action CheckForUpdates -GitHubRepo "Organisation/Depot"

.EXAMPLE
    ./ChatServeurService.ps1 -Action ConfigureAutoUpdate -GitHubRepo "Organisation/Depot"

.EXAMPLE
    ./ChatServeurService.ps1 -Action Rollback
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Install", "Update", "Uninstall", "CheckForUpdates", "ConfigureAutoUpdate", "DisableAutoUpdate", "Rollback")]
    [string]$Action,

    [string]$ServiceName = "ChatServeur",

    [string]$DisplayName = "Chat Serveur",

    [string]$InstallPath = "C:\\Program Files\\ChatServeur",

    [string]$SourcePath,

    [string]$GitHubRepo,

    [string]$AssetPattern = "ChatServeur-win-x64.zip",

    [string]$GitHubToken,

    [string]$BackupRoot,

    [int]$BackupRetention = 5,

    [string]$ScheduledTaskName = "ChatServeur AutoUpdate"
)

function Test-Admin {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

if (-not (Test-Admin)) {
    throw "Cette commande doit être exécutée dans une console PowerShell lancée en tant qu'administrateur."
}

if (-not $BackupRoot) {
    $BackupRoot = Join-Path $InstallPath "Backups"
}

function Get-ServiceSafe {
    param(
        [string]$Name
    )

    try {
        return Get-Service -Name $Name -ErrorAction Stop
    } catch {
        return $null
    }
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Backup-CurrentInstall {
    param([string]$CurrentPath, [string]$DestinationRoot, [int]$Retention)

    if (-not (Test-Path -LiteralPath $CurrentPath)) {
        return $null
    }

    Ensure-Directory -Path $DestinationRoot
    $timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $backupPath = Join-Path $DestinationRoot $timestamp
    Write-Host "Sauvegarde de l'installation actuelle vers '$backupPath'..."
    Copy-Item -Path (Join-Path $CurrentPath '*') -Destination $backupPath -Recurse -Force

    if ($Retention -gt 0) {
        $backups = Get-ChildItem -Directory -Path $DestinationRoot | Sort-Object Name -Descending
        if ($backups.Count -gt $Retention) {
            $toRemove = $backups | Select-Object -Skip $Retention
            foreach ($folder in $toRemove) {
                Write-Host "Suppression de l'ancienne sauvegarde '$($folder.FullName)'..."
                Remove-Item -LiteralPath $folder.FullName -Recurse -Force
            }
        }
    }

    return $backupPath
}

function Restore-Backup {
    param(
        [string]$TargetPath,
        [string]$BackupPath
    )

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw "La sauvegarde '$BackupPath' est introuvable."
    }

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        Ensure-Directory -Path $TargetPath
    }

    Write-Host "Restauration de la sauvegarde '$BackupPath' vers '$TargetPath'..."
    Copy-Item -Path (Join-Path $BackupPath '*') -Destination $TargetPath -Recurse -Force
}

function Get-LatestBackupPath {
    param([string]$DestinationRoot)
    if (-not (Test-Path -LiteralPath $DestinationRoot)) {
        return $null
    }

    $latest = Get-ChildItem -Directory -Path $DestinationRoot | Sort-Object Name -Descending | Select-Object -First 1
    return $latest?.FullName
}

function Invoke-ServiceStop {
    param([System.ServiceProcess.ServiceController]$Service)

    if ($Service -and $Service.Status -ne 'Stopped') {
        Write-Host "Arrêt du service '$($Service.ServiceName)'..."
        Stop-Service -InputObject $Service -Force -ErrorAction Stop
        $Service.WaitForStatus('Stopped', [TimeSpan]::FromMinutes(2))
    }
}

function Invoke-ServiceStart {
    param([System.ServiceProcess.ServiceController]$Service)

    if ($Service) {
        Write-Host "Démarrage du service..."
        Start-Service -InputObject $Service
    }
}

function Get-VersionFilePath {
    param([string]$BasePath)
    return Join-Path $BasePath 'version.txt'
}

function Set-InstalledVersion {
    param([string]$BasePath, [string]$Version)
    $versionFile = Get-VersionFilePath -BasePath $BasePath
    Set-Content -LiteralPath $versionFile -Value $Version -Encoding UTF8
}

function Get-InstalledVersion {
    param([string]$BasePath)
    $versionFile = Get-VersionFilePath -BasePath $BasePath
    if (-not (Test-Path -LiteralPath $versionFile)) {
        return $null
    }

    return (Get-Content -LiteralPath $versionFile -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Get-GitHubRelease {
    param(
        [string]$Repository,
        [string]$AssetRegex,
        [string]$Token
    )

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw "Le paramètre -GitHubRepo est requis pour cette action. Utilisez le format 'Organisation/Depot'."
    }

    $headers = @{ "User-Agent" = "ChatServeur-Updater" }
    if ($Token) {
        $headers["Authorization"] = "token $Token"
    }

    $releaseUrl = "https://api.github.com/repos/$Repository/releases/latest"
    Write-Host "Récupération des informations de publication sur $releaseUrl ..."
    $release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers -ErrorAction Stop

    $asset = $release.assets | Where-Object { $_.name -match $AssetRegex } | Select-Object -First 1
    if (-not $asset) {
        throw "Aucun artefact de publication ne correspond au motif '$AssetRegex'."
    }

    return [pscustomobject]@{
        Tag        = $release.tag_name
        AssetName  = $asset.name
        DownloadUrl = $asset.browser_download_url
    }
}

function Invoke-DownloadAndExtract {
    param(
        [string]$Url,
        [string]$Destination
    )

    $tempFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), [System.IO.Path]::GetRandomFileName() + '.zip')
    $tempFolder = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), [System.IO.Path]::GetRandomFileName())

    try {
        Write-Host "Téléchargement de l'archive '$Url'..."
        Invoke-WebRequest -Uri $Url -OutFile $tempFile -UseBasicParsing

        Ensure-Directory -Path $tempFolder
        Write-Host "Extraction de l'archive vers '$tempFolder'..."
        Expand-Archive -Path $tempFile -DestinationPath $tempFolder -Force

        Write-Host "Copie des fichiers extraits vers '$Destination'..."
        Ensure-Directory -Path $Destination
        Copy-Item -Path (Join-Path $tempFolder '*') -Destination $Destination -Recurse -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempFile) { Remove-Item -LiteralPath $tempFile -Force }
        if (Test-Path -LiteralPath $tempFolder) { Remove-Item -LiteralPath $tempFolder -Recurse -Force }
    }
}

if (($Action -eq "Install" -or $Action -eq "Update") -and [string]::IsNullOrWhiteSpace($SourcePath)) {
    throw "Le paramètre -SourcePath est requis pour les actions Install et Update."
}

if ($SourcePath -and -not (Test-Path -LiteralPath $SourcePath)) {
    throw "Le dossier source '$SourcePath' est introuvable. Publiez d'abord le serveur avec 'dotnet publish'."
}

$service = Get-ServiceSafe -Name $ServiceName

switch ($Action) {
    "Install" {
        if ($service) {
            throw "Le service '$ServiceName' existe déjà. Utilisez l'action Update pour mettre à jour ou Uninstall pour le supprimer."
        }

        if (-not (Test-Path -LiteralPath $InstallPath)) {
            New-Item -ItemType Directory -Path $InstallPath | Out-Null
        }

        Write-Host "Copie des fichiers depuis '$SourcePath' vers '$InstallPath'..."
        Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallPath -Recurse -Force

        $binaryPath = Join-Path $InstallPath 'Serveur.exe'
        if (-not (Test-Path -LiteralPath $binaryPath)) {
            throw "Le fichier '$binaryPath' est introuvable. Vérifiez que le dossier source contient la publication du serveur."
        }

        Write-Host "Création du service Windows '$ServiceName'..."
        New-Service -Name $ServiceName -BinaryPathName "`"$binaryPath`"" -DisplayName $DisplayName -StartupType Automatic
        $service = Get-Service -Name $ServiceName -ErrorAction Stop
        Write-Host "Démarrage du service..."
        Start-Service -InputObject $service

        Write-Host "Installation terminée. Le serveur démarrera désormais automatiquement avec Windows."
    }
    "Update" {
        if (-not $service) {
            throw "Le service '$ServiceName' n'existe pas. Exécutez d'abord l'action Install."
        }

        Invoke-ServiceStop -Service $service

        $backupPath = Backup-CurrentInstall -CurrentPath $InstallPath -DestinationRoot $BackupRoot -Retention $BackupRetention

        Write-Host "Mise à jour des fichiers dans '$InstallPath'..."
        Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallPath -Recurse -Force

        if ($backupPath) {
            Write-Host "Sauvegarde disponible dans '$backupPath'."
        }

        Invoke-ServiceStart -Service $service
        Write-Host "Mise à jour terminée."
    }
    "Uninstall" {
        if ($service) {
            Invoke-ServiceStop -Service $service

            Write-Host "Suppression du service Windows '$ServiceName'..."
            sc.exe delete $ServiceName | Out-Null
        }

        if (Test-Path -LiteralPath $InstallPath) {
            Write-Host "Suppression du dossier '$InstallPath'..."
            Remove-Item -LiteralPath $InstallPath -Recurse -Force
        }

        Write-Host "Désinstallation terminée."
    }
    "CheckForUpdates" {
        $service = Get-ServiceSafe -Name $ServiceName
        $currentVersion = Get-InstalledVersion -BasePath $InstallPath
        $release = Get-GitHubRelease -Repository $GitHubRepo -AssetRegex $AssetPattern -Token $GitHubToken

        if ($currentVersion -and $currentVersion -eq $release.Tag) {
            Write-Host "Le serveur est déjà à jour (version $currentVersion)."
            return
        }

        Write-Host "Nouvelle version détectée : $($release.Tag)."
        Invoke-ServiceStop -Service $service

        $backupPath = Backup-CurrentInstall -CurrentPath $InstallPath -DestinationRoot $BackupRoot -Retention $BackupRetention

        Invoke-DownloadAndExtract -Url $release.DownloadUrl -Destination $InstallPath
        Set-InstalledVersion -BasePath $InstallPath -Version $release.Tag

        if ($backupPath) {
            Write-Host "Sauvegarde de la version précédente disponible dans '$backupPath'."
        }

        Invoke-ServiceStart -Service $service
        Write-Host "Mise à jour automatique terminée (version $($release.Tag))."
    }
    "ConfigureAutoUpdate" {
        if ([string]::IsNullOrWhiteSpace($GitHubRepo)) {
            throw "Le paramètre -GitHubRepo est requis pour configurer la mise à jour automatique."
        }

        $scriptPath = $MyInvocation.MyCommand.Path
        $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $arguments = @(
            "-File `"$scriptPath`"",
            "-Action CheckForUpdates",
            "-ServiceName `"$ServiceName`"",
            "-DisplayName `"$DisplayName`"",
            "-InstallPath `"$InstallPath`"",
            "-GitHubRepo `"$GitHubRepo`"",
            "-AssetPattern `"$AssetPattern`"",
            "-BackupRoot `"$BackupRoot`"",
            "-BackupRetention $BackupRetention"
        )

        if ($GitHubToken) {
            $arguments += "-GitHubToken `"$GitHubToken`""
        }

        $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument ($arguments -join ' ')
        $trigger = New-ScheduledTaskTrigger -Daily -At 00:00
        $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 2)
        $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType S4U -RunLevel Highest

        Register-ScheduledTask -TaskName $ScheduledTaskName -Action $action -Trigger $trigger -Principal $principal -Description "Met à jour automatiquement le service ChatServeur depuis GitHub" -Settings $settings -Force
        Write-Host "Tâche planifiée '$ScheduledTaskName' configurée pour vérifier les mises à jour chaque nuit à minuit."
    }
    "DisableAutoUpdate" {
        $task = Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction SilentlyContinue
        if ($task) {
            Unregister-ScheduledTask -TaskName $ScheduledTaskName -Confirm:$false
            Write-Host "Tâche planifiée '$ScheduledTaskName' supprimée."
        } else {
            Write-Host "Aucune tâche planifiée nommée '$ScheduledTaskName' n'a été trouvée."
        }
    }
    "Rollback" {
        $targetBackup = $SourcePath
        if (-not $targetBackup) {
            $targetBackup = Get-LatestBackupPath -DestinationRoot $BackupRoot
        }

        if (-not $targetBackup) {
            throw "Aucune sauvegarde disponible."
        }

        $service = Get-ServiceSafe -Name $ServiceName
        Invoke-ServiceStop -Service $service

        Restore-Backup -TargetPath $InstallPath -BackupPath $targetBackup

        Invoke-ServiceStart -Service $service
        Write-Host "Restauration terminée depuis '$targetBackup'."
    }
}
