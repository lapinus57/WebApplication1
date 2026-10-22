# SMS et mail

L'onglet reprend les modèles `RdvInfo`, les requêtes et signatures OVH et les
notifications SMTP de `lapinus57/smsmailv2Winui` (révision
`e9a71754a6dcbd0fde67e6cf612a804d7cedfa06`). Le projet de référence contient
encore des données de démonstration et des boutons non reliés : cette intégration
les remplace par un parcours de chargement, préparation, sélection et envoi.

## Première utilisation

1. Configurer la base Access dans les paramètres RDV et les identifiants/fournisseur
   Access dans les paramètres du poste, comme pour la recherche RDV.
2. Dans **SMS et mail > Paramètres**, renseigner le cabinet, le compte OVH
   (application key, application secret, consumer key, service SMS, expéditeur
   autorisé) et le compte SMTP. Le port 587 utilise STARTTLS ; le SMTP implicite
   sur le port 465 n'est pas pris en charge par `System.Net.Mail.SmtpClient`.
3. Enregistrer les paramètres et adapter les modèles SMS et HTML.
4. Choisir le jour puis charger les rendez-vous. La lecture utilise les tables
   EyeSoft `ag_rdv` et `patients`, avec les mêmes colonnes que l'application SMS
   d'origine. Le nom de table et les colonnes de date/heure viennent des paramètres
   RDV existants. La base Access n'est pas modifiée.
5. Vérifier les contacts et messages, cocher les envois souhaités puis confirmer.

L'import JSON accepte également un tableau de `RdvInfo` :

```json
[
  {
    "Nom": "Exemple",
    "Prenom": "Camille",
    "DateRDV": "2026-10-23T00:00:00",
    "HeureRDV": "2026-10-23T09:30:00",
    "Tel1": "",
    "Email": ""
  }
]
```

Seuls les rendez-vous du jour sélectionné sont préparés. Les rendez-vous annulés
et les lignes ABSENCE sont exclus lors du chargement Access. Les coordonnées
partagées sont regroupées en un message par contact et par canal ; le premier
horaire est utilisé et la liste HTML précise les horaires individuels. Aucune
fusion n'est faite sur la seule ressemblance des noms ou adresses. Vérifier la
sélection avant chaque envoi. Les numéros français 06/07 sont normalisés en +33 ;
les numéros internationaux sont attendus au format +/00. Les mails multiples
dans un champ sont reconnus. L'option « SMS uniquement en l'absence de mail »
utilise la validité syntaxique des adresses, pas leur délivrabilité réelle.

Les variables disponibles sont `[PRENOMS]`, `[CABINET]`, `[DATE]`, `[DATE_LONGUE]`,
`[HEURE]` et `[LISTE_PERSONNES]`. Le nombre de segments SMS est estimé sur le
message final, en tenant compte du GSM étendu et de l'Unicode. La facturation
réelle reste celle d'OVH.

## Historique et données du poste

Les paramètres sont chiffrés par DPAPI pour l'utilisateur Windows dans
`%LOCALAPPDATA%/EyeChat/SmsMail/settings.dat`. Aucune clé ou configuration privée
du dépôt de référence n'est importée. Le journal SQLite `history.db`, au même
endroit, contient les destinataires et messages ; il est local au poste et
exportable en CSV. Les postes distincts ne partagent pas la protection contre
les doublons.

Une réservation durable par jour/contact/canal précède chaque appel fournisseur.
Un nouvel envoi est bloqué même après rechargement ou changement de modèle.
Si l'application s'arrête ou si le réseau échoue pendant un envoi, l'historique
demande de vérifier le résultat chez le fournisseur : aucun nouvel essai
automatique ne risque de répéter le message. Un échec de journalisation après
acceptation ne relance pas l'envoi. Le journal n'est pas effacé par l'interface.

« Accepté par SMTP/OVH » ne signifie pas reçu. Le bouton d'actualisation récupère
les statuts SMS OVH. Les notifications mail sont demandées à l'adresse d'envoi,
mais leur disponibilité dépend du serveur destinataire ; elles se consultent
dans la boîte mail. L'intégration ne supprime ni ne traite automatiquement les
messages IMAP. Les anciens journaux SQL de SMSMail ne sont pas importés.

## Validation

```sh
dotnet run --project tests/SmsMail.Tests
```

Ce programme de vérification teste préparation, coordonnées, regroupement,
annulations, modèles HTML, segments SMS, réservation persistante et API OVH
simulée (signature, crédits décimaux, soumission, erreurs, réception).
Aucun message réel n'est envoyé. Le workflow Windows compile aussi le client
WinUI pour les pull requests vers `master2`. La connexion à la base Access et
les comptes réels doivent être vérifiés sur le poste Windows de déploiement.

## Modèle du cabinet et invitation calendrier

Le modèle HTML par défaut reprend le mail fourni : titre vert, rappel du cabinet,
jour et heure, adresse et personnes concernées. La date, les personnes, l'heure,
le cabinet et l'adresse sont remplacés par les données de l'envoi. La variable
`[ADRESSE_CABINET]` est disponible. Le modèle standard précédent est migré à la
lecture des paramètres ; les modèles personnalisés sont conservés. Le bouton
« Appliquer le modèle de rappel du cabinet » permet de remplacer volontairement
le modèle en cours, puis de l'enregistrer.

Chaque mail comprend une invitation `text/calendar; method=REQUEST` et une pièce
jointe `rendez-vous.ics`. Les dates UTC sont calculées depuis `Europe/Paris`, avec
les heures d'été/hiver. La durée par défaut est de 30 minutes, modifiable dans les
paramètres. Pour un contact partageant plusieurs RDV, l'événement couvre du
premier horaire au dernier, augmenté de cette durée. L'organisateur est l'adresse
d'envoi ; seul le destinataire courant est invité. Le UID reste identique pour
le même jour/contact afin d'éviter les événements dupliqués lors de l'import.
Aucune réponse de participation n'est demandée ni traitée. L'affichage de
l'invitation dépend du logiciel mail ; la pièce jointe reste importable.
