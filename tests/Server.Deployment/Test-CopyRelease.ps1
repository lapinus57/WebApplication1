# Vérifie qu'une mise à jour du serveur (ChatServeurService.ps1 -Action Update) conserve appsettings.json,
# le certificat et le dossier data de l'installation. Fichiers factices dans un dossier temporaire.
$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("deploy-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$script = (Resolve-Path (Join-Path $PSScriptRoot "../../Serveur/Deployment/ChatServeurService.ps1")).ProviderPath
Push-Location $root
try {
    foreach ($d in 'publish/wwwroot/css', 'publish/data', 'install/data', 'install/wwwroot') { New-Item -ItemType Directory -Path $d -Force | Out-Null }
    Set-Content publish/Serveur.exe 'nouveau'; Set-Content publish/appsettings.json '{"vide":1}'; Set-Content publish/appsettings.Development.json '{"vide":1}'
    Set-Content publish/wwwroot/css/site.css 'css2'; Set-Content publish/data/chat.db 'dev'; Set-Content publish/admin.pfx 'nouveau-cert'; Set-Content publish/wwwroot/appsettings.json '{"vide":1}'
    Set-Content install/Serveur.exe 'ancien'; Set-Content install/appsettings.json '{"mes":"reglages"}'; Set-Content install/data/chat.db 'mabase'; Set-Content install/admin.pfx 'moncert'
    # Charge seulement les fonctions du script (sans exécuter son action).
    $code = Get-Content -Raw -LiteralPath $script
    $start = $code.IndexOf('function Ensure-Directory'); $end = $code.IndexOf('function Backup-CurrentInstall')
    Invoke-Expression ($code.Substring($start, $end - $start))
    Copy-Release -Source ./publish -Destination ./install
    function Check($ok, $msg) { if (-not $ok) { throw "ÉCHEC : $msg" }; Write-Host "ok - $msg" }
    Check ((Get-Content ./install/Serveur.exe) -eq 'nouveau') "programme mis à jour"
    Check ((Get-Content ./install/appsettings.json) -eq '{"mes":"reglages"}') "appsettings.json conservé"
    Check ((Get-Content ./install/data/chat.db) -eq 'mabase') "dossier data non écrasé"
    Check ((Get-Content ./install/admin.pfx) -eq 'moncert') "certificat conservé"
    Check ((Get-Content ./install/appsettings.Development.json) -eq '{"vide":1}') "appsettings absent de l'installation : copié"
    Check ((Get-Content ./install/wwwroot/css/site.css) -eq 'css2') "wwwroot mis à jour"
    Check ((Get-Content ./install/wwwroot/appsettings.json) -eq '{"vide":1}') "seuls les fichiers à la racine sont protégés"
    Remove-Item -Recurse -Force ./neuf -ErrorAction SilentlyContinue
    Copy-Release -Source ./publish -Destination ./neuf
    Check ((Test-Path ./neuf/appsettings.json) -and -not (Test-Path ./neuf/data)) "nouvelle installation : appsettings copié, pas de data de développement"
    Write-Host "Mise à jour du serveur : réglages de l'installation conservés."
}
finally {
    Pop-Location
    Remove-Item -Recurse -Force $root
}
