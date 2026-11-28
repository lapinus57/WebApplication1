# Mise à jour des postes

Chaque poste annonce sa version au serveur à la connexion. Le serveur garde la liste (`data\client-versions.json`) et
compare : dès qu'un poste a une version plus récente, les postes plus anciens affichent en haut de la fenêtre le bandeau
**« Nouvelle version d'EyeChat »**.

## Dossier des mises à jour (disque réseau)

1. Dans Visual Studio, créez le paquet (Publier → Créer des packages d'application). Vous obtenez un dossier comme
   `Client_0.1.38.0_Test` (avec le `.msixbundle`, `Install.ps1` et le certificat).
2. Copiez ce dossier tel quel dans le dossier partagé, par exemple `\\SERVEUR\Partage\uptEye\Client_0.1.38.0_Test`.
3. Dans l'administration du serveur, page **Postes**, indiquez le chemin réseau complet du dossier `uptEye`
   (un chemin `\\serveur\partage\…` plutôt qu'une lettre de lecteur, qui change d'un poste à l'autre).

Les postes lisent eux-mêmes ce dossier : ils trouvent la version la plus récente (d'après le nom `Client_x.y.z.w_…`),
même si aucun poste ne l'a encore installée. Le bouton **Mettre à jour** ouvre le `.msixbundle` (installeur Windows) ;
s'il n'y en a pas, il ouvre le dossier. Pour un premier poste, ou si le certificat n'est pas encore installé, lancez
plutôt `Install.ps1` du dossier.

La page **Postes** liste chaque poste, son utilisateur, sa version, sa dernière connexion et « à jour » ou
« à mettre à jour ». Un poste qui ne s'est pas connecté depuis 90 jours ne compte plus pour la version la plus récente.
