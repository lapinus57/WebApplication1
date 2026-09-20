# EyeChat ne se lance pas après l'installation

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

EyeChat écrit son journal dans le fichier suivant :

```text
%LOCALAPPDATA%\EyeChat\app.log
```

Collez ce chemin dans la barre d'adresse de l'Explorateur de fichiers, puis transmettez `app.log` au support. Depuis cette version, une erreur survenant pendant la création de la fenêtre affiche également ce chemin et le code `CLI26`. Une impossibilité d'activer les raccourcis clavier globaux est enregistrée avec le code `CLI25`, mais elle ne bloque plus l'ouverture de l'application.

## Si aucun journal n'est créé

L'échec se produit probablement avant l'exécution d'EyeChat. Vérifiez alors :

- que le certificat utilisé pour signer le paquet est approuvé et encore valide ;
- que l'architecture du paquet correspond au PC ;
- dans **Observateur d'événements > Journaux Windows > Application**, les erreurs `AppModel-Runtime`, `.NET Runtime` ou `Application Error` enregistrées à l'heure du lancement.

Joignez le détail de cette erreur, la version de Windows (`winver`) et l'architecture du PC à la demande de support.
