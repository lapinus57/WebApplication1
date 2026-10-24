# Retrouver la composition d’un mail

Chaque nouveau mail de rappel ou de test porte une **référence aléatoire de 20 chiffres**, présentée par groupes de quatre au bas du message. Le destinataire peut communiquer ce numéro au cabinet en cas d’erreur dans les personnes indiquées. Il ne contient ni code patient ni information médicale. Les SMS restent inchangés.

Dans **SMS et Mail → Historique**, rechercher cette référence (avec ou sans espaces/tirets). Le résultat est accessible depuis tous les postes connectés au même serveur. **Lire le message envoyé** affiche son contenu ; **Pourquoi ces personnes ?** affiche la composition enregistrée :

- nom et code patient des personnes incluses ;
- rendez-vous réel et heure annoncée dans le rappel ;
- groupe de cette préparation et éventuelle famille EyeEmail ;
- adresse présente dans les coordonnées de la personne, ou transmise au groupe par un autre membre identifié ;
- étapes effectives du regroupement : téléphone ou mail commun, adresse/ville, nom et proximité temporelle, remarque « avec », famille EyeEmail, ou confirmation de l’utilisateur.

Ces raisons expliquent le fonctionnement du regroupement ; elles ne prouvent pas un lien familial. En cas d’erreur, vérifier les coordonnées et règles concernées avant une prochaine préparation. La trace n’invente pas les correspondances non enregistrées, ne recopie pas la remarque complète, et n’est pas envoyée au destinataire. Les textes sont affichés comme du texte, sans interprétation HTML.

La composition est figée à la préparation, puis enregistrée avec le mail, son objet, sa référence et le poste d’origine **avant l’appel SMTP**. Elle n’est pas recalculée à partir des données actuelles. Modifier les familles, les coordonnées ou les statuts de livraison ne réécrit pas une trace déjà réservée. Plusieurs groupes partageant une adresse restent réunis dans le même mail, avec l’origine de chaque inclusion. Une nouvelle préparation peut générer un nouveau numéro, mais la protection contre un second envoi au même contact reste indépendante du numéro.

Le serveur conserve les données dans `data/chat.db` (`SharedMessageHistory` et `SharedMessageTrace`). Une contrainte unique sur la référence refuse toute collision et annule la réservation correspondante avant l’envoi. Le client vérifie qu’il peut relire exactement la trace enregistrée ; une erreur de connexion ou un serveur incompatible bloque l’envoi. Une réservation dont le résultat est incertain reste à vérifier dans l’historique. La copie locale conserve aussi la trace pour la reprise des archives vers le serveur.

Les listes ne chargent que les références et résumés ; les détails sont lus à la demande via les contrôles d’accès du chat. La référence n’ouvre aucun accès public aux informations. L’export CSV contient la référence et le message, pas le détail des règles internes.

Les anciens envois n’avaient pas ces données : ils conservent leur contenu et statut, sans référence rétroactive ni raison supposée. La nouvelle fonction s’applique aux mails préparés avec la version mise à jour. Installer le serveur et tous les clients mis à jour avant de reprendre les envois. Aucun changement ni écriture dans Access.

Validation : tests sur données fictives pour les règles de regroupement, heures annoncées, références et collisions, conservation des traces, migration, lecture depuis un autre poste, accès du hub et blocage en cas de serveur incompatible. Aucun envoi réel pendant les tests.
