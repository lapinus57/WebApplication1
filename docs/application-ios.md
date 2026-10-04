# EyeChat sur iPhone

EyeChat Mobile est une application web installable (PWA) intégrée au serveur. Elle permet de consulter la file des patients actifs et les messages du jour depuis Safari, sans publier une application dans l’App Store.

## Installation

1. Connecter l’iPhone au même réseau sécurisé que le serveur EyeChat.
2. Dans Safari, ouvrir `https://ADRESSE_DU_SERVEUR:5443/Mobile`.
3. Se connecter avec le compte d’administration EyeChat.
4. Toucher **Partager**, puis **Sur l’écran d’accueil** et **Ajouter**.

L’icône **EyeChat** ouvre ensuite l’interface en plein écran. Les données sont actualisées toutes les 10 secondes et dès que l’application revient au premier plan.

## Notifications

Le bouton en forme de losange dans l’en-tête demande l’autorisation d’afficher les nouveaux messages. Sur iOS, les notifications web nécessitent une version récente d’iOS et l’installation préalable de l’application sur l’écran d’accueil. La relève automatique fonctionne pendant que l’application est ouverte ou suspendue brièvement ; ce mécanisme ne remplace pas encore un service de notifications push APNs.

## Sécurité et données de santé

- La page et son flux JSON utilisent l’authentification existante du serveur.
- En production, l’accès doit impérativement passer par le port HTTPS `5443` avec un certificat approuvé par l’iPhone.
- Les réponses contenant les patients et les messages portent la directive `no-store` et ne sont pas ajoutées au cache hors ligne. Seuls les fichiers statiques de l’interface sont mis en cache.
- Il est recommandé de limiter l’accès au réseau interne ou à un VPN et d’activer le verrouillage biométrique de l’iPhone.
- La version mobile est volontairement en lecture seule afin d’éviter une modification accidentelle des dossiers depuis un téléphone.

Pour fermer la session, toucher l’icône de sortie en haut à droite.
