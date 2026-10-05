# EyeChat sur iPhone

EyeChat Mobile est une application web installable (PWA) intégrée au serveur. Elle permet de consulter uniquement la file des patients du jour et les messages du jour depuis Safari, sans publier une application dans l’App Store.

## Installation

1. Sur le PC qui héberge EyeChat, ouvrir une invite de commandes et exécuter `ipconfig`.
2. Repérer l’**Adresse IPv4** de la carte réseau utilisée, par exemple `192.168.1.25`.
3. Connecter l’iPhone au même réseau Wi-Fi sécurisé que le serveur EyeChat.
4. Dans Safari sur l’iPhone, ouvrir l’adresse correspondant au mode du serveur, en remplaçant l’adresse d’exemple par **la même adresse IPv4 que celle utilisée pour ouvrir EyeChat sur le port 5000** :
   - serveur de production avec certificat : `https://192.168.1.25:5443/Mobile` ;
   - serveur lancé en développement sans certificat : `http://192.168.1.25:5000/Mobile`.
5. Se connecter avec le compte d’administration EyeChat.
6. Pour créer un raccourci, toucher **Partager**, puis **Sur l’écran d’accueil** et **Ajouter**.

Le raccourci **EyeChat** ouvre ensuite l’interface en plein écran. Les données sont actualisées toutes les 10 secondes et dès que l’application revient au premier plan.

## Si la page ne s’ouvre pas

- Vérifier que le service EyeChat est démarré sur le PC serveur.
- Ne pas changer l’adresse IP entre les ports : si EyeChat répond sur `192.168.1.153:5000`, essayer `http://192.168.1.153:5000/Mobile` en développement ou `https://192.168.1.153:5443/Mobile` en production. Une adresse comme `192.168.1.123` désigne un autre appareil.
- Vérifier que l’iPhone et le serveur sont sur le même réseau et qu’aucun réseau invité n’isole les appareils.
- Autoriser le port TCP `5443` dans le pare-feu Windows du serveur, ou le port `5000` pour un lancement en développement.
- Utiliser `https://` et non `http://` en production.
- Le certificat HTTPS configuré sur EyeChat doit être approuvé par l’iPhone et correspondre au nom ou à l’adresse utilisés. Un avertissement de certificat ne doit pas être ignoré pour des données de santé.

## Notifications

Le bouton en forme de losange dans l’en-tête demande l’autorisation d’afficher les nouveaux messages. Sur iOS, les notifications web nécessitent une version récente d’iOS et l’installation préalable de l’application sur l’écran d’accueil. La relève automatique fonctionne pendant que l’application est ouverte ou suspendue brièvement ; ce mécanisme ne remplace pas encore un service de notifications push APNs.

## Sécurité et données de santé

- La page et son flux JSON utilisent l’authentification existante du serveur.
- En production, l’accès doit impérativement passer par le port HTTPS `5443` avec un certificat approuvé par l’iPhone.
- Les réponses contenant les patients et les messages portent la directive `no-store` et ne sont pas ajoutées au cache hors ligne. Seuls les fichiers statiques de l’interface sont mis en cache.
- Il est recommandé de limiter l’accès au réseau interne ou à un VPN et d’activer le verrouillage biométrique de l’iPhone.
- La version mobile est volontairement en lecture seule afin d’éviter une modification accidentelle des dossiers depuis un téléphone.

Pour fermer la session, toucher le bouton de sortie en haut à droite.
