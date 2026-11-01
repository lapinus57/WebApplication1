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

## Colonne « Regroupés par »

Dans le tableau des envois, cette colonne dit ce qui relie les personnes
réunies dans un même mail ou SMS : **Même téléphone**, **Même e-mail**,
**Même adresse**, **Adresse proche** (faute de frappe probable),
**Remarque de … : « avec … »**, **Famille EyeEmail**, ou un regroupement
confirmé à la main. Une personne seule affiche **Personne seule**.

## Exporter la liste des envois (Excel)

1. Préparer les messages et appliquer les filtres canal/recherche souhaités.
2. Cliquer **Exporter la liste (Excel)** et choisir où enregistrer le fichier `.xlsx`.

Le classeur contient deux feuilles :

- **Messages** : une ligne par mail ou SMS visible à l’écran, triée par heure de
  rendez-vous. Colonnes : RDV, venue annoncée, canal, personnes, destinataire,
  regroupés par, statut, sélection (coché, non coché, bloqué), texte du SMS ou du mail
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

## Extraire les post-it de consultation (onglet Post-it)

Onglet temporaire, servant à étudier la façon dont les post-it de fin de
consultation sont écrits. Il ne modifie ni la base Access, ni le regroupement
des familles.

1. Ouvrir **SMS et mail → Post-it**.
2. Choisir la période (par défaut les 6 derniers mois).
3. Choisir un mot de passe (10 caractères minimum), puis
   **Extraire et enregistrer le fichier chiffré…**.
4. Transmettre le fichier `.eyediag`, et le mot de passe par un autre moyen.

Source : table `tPostIT` (champs `CodePat`, `DatePI`, `MessageText`), reliée à
la table `Patients` pour le nom et le prénom du patient. Seuls les post-it de
consultation sont gardés : ceux qui contiennent une cotation (`CS`, `OR`,
`V3F`) ou un suivi du type `1 ok`, `4-8-12 ok`. Les autres sont comptés mais
pas exportés.

Avec les pseudonymes (option cochée par défaut), les noms et prénoms sont
remplacés partout, y compris dans le texte des post-it, grâce à la liste des
patients (cette liste n’est pas exportée). Les codes (`CS`, `OR`, `LUN`, `GOU`,
`LEN`, `DL`, `TOPO`, `1 ok`…) et les mots comme « mari », « fille » restent
lisibles. Un prénom absent de la table Patients ou mal orthographié reste
visible.

### Vérifier les liens familiaux trouvés dans les post-it

Le bouton **Analyser les liens et enregistrer le rapport chiffré…** applique la
règle prévue, **sans modifier les rappels** :

1. Dans chaque post-it de consultation, une personne est citée par une ligne
   `+Prénom`, une ligne `Prénom CS…` / `Prénom OR 1ok`, ou une ligne ne
   contenant qu’un prénom. Les codes, suivis, montants (`de 20`), dates,
   `cf` et phrases libres sont ignorés, ainsi que la ligne du patient lui-même.
2. La personne citée doit avoir un rendez-vous **le même jour**, avec ce prénom.
3. Même nom de famille : lien. Nom différent : lien seulement si les deux
   rendez-vous sont à **moins d’1 heure** d’écart.
4. Plusieurs personnes possibles : la plus proche ; en cas d’égalité, pas de lien.
   Aucune personne : pas de lien.

Choisir une période de **3 ans** (durée d’historique retenue). Le rapport
chiffré liste les liens, les familles formées et les cas non résolus avec leur
motif, pour vérification avant toute utilisation dans les rappels.

Pour masquer l’onglet : **Paramètres → Préparation des rappels et familles →
Afficher l’onglet Post-it**, puis **Enregistrer**.

## Vérification sur le poste

Avec des données de test, vérifier le cas RDV 16h45 / venue 16h00 dans le SMS,
le mail et le calendrier ; vérifier également qu’une consigne ambiguë bloque
l’envoi. Exporter une liste et un historique d’une date connue (notamment les
envois proches de minuit) et ouvrir les fichiers dans Excel. Exporter un
diagnostic, puis vérifier qu’un mauvais mot de passe est refusé.
