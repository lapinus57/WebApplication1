# Démarrage du serveur et certificat HTTPS

Sous Windows, le PFX est maintenant chargé avec `UserKeySet` : la clé privée est
importée dans le profil du compte qui lance le serveur, et non dans le stockage
de clés de la machine. Le certificat reste ouvert pendant toute l’exécution de
Kestrel ; sa libération nettoie la clé importée (sans `PersistKeySet`). Le
chargement entièrement en mémoire n’est pas retenu sur Windows car la connexion
HTTPS avec Schannel échouait dans le test d’intégration.

Le compte du serveur doit pouvoir lire le PFX, écrire dans son propre profil
et dans le dossier `data`. Aucun droit administrateur n’est demandé par ce
chargement, mais les droits du compte réel restent à vérifier sur le poste.
La protection HTTPS et le compte administrateur restent obligatoires en production.

## Installer la correction

1. Arrêter l’ancienne instance depuis son icône ou le gestionnaire de services.
2. Sauvegarder le dossier `data` et la configuration effective du serveur.
3. Publier le projet Serveur. Le profil `FolderProfile` conserve Windows x86
   autoporté, et cible maintenant `net8.0-windows10.0.17763.0` comme le projet.
   Copier **tout le dossier de publication** dans un dossier où le compte
   serveur dispose des droits nécessaires. Préserver les données et les secrets
   existants : ne pas les remplacer par le fichier de configuration vide du dépôt.
4. Conserver le nouveau certificat déjà validé et son mot de passe. Ne pas
   réutiliser un PFX ou un mot de passe partagé dans une conversation.
5. Lancer `Serveur.exe` normalement, sans choisir « Exécuter en tant
   qu’administrateur ». Vérifier `http://localhost:5000/api/discovery`, puis
   l’administration `https://localhost:5443`.

La configuration JSON est recherchée dans le dossier de l’exécutable, même si
le lancement vient d’un raccourci ou d’un autre répertoire. Les variables
d’environnement et arguments de lancement gardent leur priorité normale.

Une erreur de démarrage affiche désormais une boîte de dialogue pour un lancement
interactif. Un diagnostic sans secrets est aussi écrit, si possible, dans
`%LOCALAPPDATA%\EyeChat\Serveur\Logs\dernier-demarrage-erreur.txt`, sous le compte
qui lance le processus. Le processus retourne le code 1 en cas d’échec.
En service Windows, aucune boîte de dialogue ni icône de notification n’est créée.

Si « accès refusé » persiste, contrôler les droits de **lecture du PFX** et les
droits sur **data** du compte réel du processus. Ne pas accorder des droits à
Tout le monde, ni rendre le certificat public. Le correctif ne modifie aucun ACL.

La CI teste un certificat fictif et une vraie connexion HTTPS locale ; le
démarrage sous le compte Windows du cabinet reste à vérifier sur le poste.
