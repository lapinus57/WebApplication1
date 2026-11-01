# Heure de venue et exports des rappels

## Une heure annoncée dans la remarque

Dans **SMS et mail → Envois**, charger et préparer les rendez-vous comme auparavant.
Une consigne explicite telle que **« j’ai dit de venir à 16h00 »** prend priorité
sur les règles générales d’horaire. Pour un rendez-vous à 16h45 :

- la colonne **Heure RDV** reste à **16:45** ;
- le message, SMS comme mail, annonce **16:00** ;
- l’invitation calendrier utilise aussi l’heure annoncée ;
- le tableau et **Voir les horaires ajustés** expliquent le changement.

La remarque est prise en compte même si les règles générales sont désactivées.
Elle est interprétée avant le rendu des messages, après le regroupement familial.
Les règles ne se cumulent pas : une heure de venue explicite n’est pas décalée
une seconde fois. La base Access et l’horaire du rendez-vous restent inchangés.
Le texte intégral de la remarque n’est pas ajouté au message.

Formulations reconnues : « venir à 16h00 », « arriver à 16:00 »,
« arrivée pour 16 heures », « convoqué à 16h », « se présenter à 16h00 ».
« Venir à 16h00 au lieu de 16h45 » est accepté si 16h45 est bien l’heure réelle.
Il s’agit d’une reconnaissance de consignes explicites, pas d’une analyse IA de
toute formulation libre. Un simple « appel à 16h00 » ne change pas l’heure.

Une consigne avec plusieurs horaires possibles, une négation, une question, une
autre date, une heure invalide ou une formulation incertaine est signalée
**À vérifier — remarque horaire ambiguë**. Le message concerné ne peut pas être
envoyé, y compris s’il contient plusieurs membres d’une famille. Vérifier la
consigne dans l’agenda, la préciser si nécessaire puis **Charger et préparer**
à nouveau. La fonction ne corrige jamais Access elle-même.

## Exporter la liste des envois (Excel)

1. Préparer les messages et appliquer les filtres canal/recherche souhaités.
2. Cliquer **Exporter la liste (Excel)** et choisir où enregistrer le fichier `.xlsx`.

Le classeur contient deux feuilles :

- **Messages** : une ligne par mail ou SMS visible à l’écran, triée par heure de
  rendez-vous. Colonnes : RDV, venue annoncée, canal, personnes, destinataire,
  statut, sélection (coché, non coché, bloqué), texte du SMS ou du mail
  (converti en texte lisible) et horaires ajustés. **Toutes les lignes visibles
  sont exportées**, même décochées ou bloquées.
- **Familles** : chaque famille, ses membres, téléphones, e-mails et la façon
  dont elle a été regroupée.

L’en-tête reste visible en défilant et chaque colonne a un filtre. Toutes les
cellules sont du texte : un nom ou une remarque ne peut pas devenir une formule.
Pour imprimer, ouvrir le fichier dans Excel ou LibreOffice.

L’export n’envoie aucun message et ne réserve aucun destinataire.

## Exporter l’historique d’un jour (Excel)

1. Ouvrir **Historique**.
2. Choisir **Jour d’envoi (heure locale)**. Il s’agit du jour où l’envoi a été
   enregistré, pas du jour du rendez-vous. Le fuseau utilisé est celui du poste.
3. Filtrer éventuellement par canal ou destinataire.
4. Cliquer **Exporter ce jour (Excel)**.

L’historique est rechargé avant l’export. Le fichier reprend les heures d’envoi,
les destinataires, les statuts enregistrés et le texte des messages (mails
convertis en texte, sans balises HTML).

## Exporter un diagnostic chiffré

Pour faire analyser un problème de regroupement ou d’envoi :

1. Charger et préparer les rendez-vous du jour concerné.
2. Menu **…** des envois → **Exporter un diagnostic chiffré**.
3. Choisir un mot de passe (10 caractères minimum), décrire le problème si
   besoin, puis enregistrer le fichier `.eyediag`.
4. Transmettre le fichier, et **le mot de passe par un autre moyen**.

Le fichier contient les rendez-vous du jour (y compris annulés), les familles
formées, la liste des mails et SMS préparés avec leur statut, l’historique des
venues s’il a été chargé, et les options de préparation. Les clés OVH et le mot
de passe de messagerie ne sont **jamais** inclus.

L’option **pseudonymes** (cochée par défaut) remplace les noms, prénoms,
téléphones, e-mails, adresses et villes par des valeurs inventées, y compris
dans les remarques et les textes des messages. Une même valeur donne toujours
le même pseudonyme dans un fichier : les doublons et les liens entre patients
restent visibles. Restent en clair : les heures, les numéros de rue, les
numéros bidons (`0000000000`) et le fournisseur d’e-mail courant
(`orange.fr`, `gmail.com`…). Une faute de frappe n’est en revanche plus
reconnaissable : décocher l’option seulement si le problème vient de
l’orthographe exacte d’un nom ou d’une adresse.

Chiffrement : AES-256-GCM, clé dérivée du mot de passe (PBKDF2-SHA256,
600 000 itérations). Sans le mot de passe, le fichier est illisible ; toute
modification du fichier est détectée. Le script `tools/lire_diagnostic.py`
permet de le déchiffrer.

## Vérification sur le poste

Avec des données de test, vérifier le cas RDV 16h45 / venue 16h00 dans le SMS,
le mail et le calendrier ; vérifier également qu’une consigne ambiguë bloque
l’envoi. Exporter une liste et un historique d’une date connue (notamment les
envois proches de minuit) et ouvrir les fichiers dans Excel. Exporter un
diagnostic, puis vérifier qu’un mauvais mot de passe est refusé.
