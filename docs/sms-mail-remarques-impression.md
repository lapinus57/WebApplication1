# Heure de venue et impression des rappels

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

## Imprimer la liste des envois

1. Préparer les messages et appliquer les filtres canal/recherche souhaités.
2. Cliquer **Imprimer la liste**.
3. Dans l’aperçu, choisir **Imprimer / PDF**, puis l’imprimante ou l’enregistrement PDF.

La liste A4 paysage est triée par heure réelle de rendez-vous. Elle affiche les
personnes, le destinataire, le canal, l’heure annoncée, le résumé du mail ou le
SMS, le statut et la sélection. **Toutes les lignes visibles sont imprimées**,
même les lignes décochées ou bloquées ; leur état est indiqué. Pour consulter
le texte complet d’un mail avant envoi, utiliser **Détails du mail**.

L’impression n’envoie aucun message et ne réserve aucun destinataire.

## Imprimer l’historique d’un jour

1. Ouvrir **Historique**.
2. Choisir **Jour d’envoi (heure locale)**. Il s’agit du jour où l’envoi a été
   enregistré, pas du jour du rendez-vous. Le fuseau utilisé est celui du poste.
3. Filtrer éventuellement par canal ou destinataire.
4. Cliquer **Imprimer ce jour**, puis **Imprimer / PDF**.

L’historique est rechargé avant l’impression. Le document reprend le jour choisi,
les filtres, les heures d’envoi, les destinataires, les statuts enregistrés et le
texte des messages. Les mails sont convertis en texte lisible, sans balises HTML.
Le bouton **Tous les jours** retire le filtre de date de l’écran ; l’impression
de l’historique demande toujours de choisir un jour.

L’aperçu utilise WebView2, déjà utilisé par l’éditeur de mails. Le document est
créé en mémoire, sans fichier temporaire HTML, sans script ni image externe.
Un PDF n’est créé que si vous choisissez de l’enregistrer dans la fenêtre
d’impression. Pour une liste très volumineuse, réduire les résultats avec les filtres.

## Vérification sur le poste

Avec des données de test, vérifier le cas RDV 16h45 / venue 16h00 dans le SMS,
le mail et le calendrier ; vérifier également qu’une consigne ambiguë bloque
l’envoi. Imprimer une liste sur plusieurs pages et un historique d’une date
connue, notamment les envois proches de minuit. Les tests automatisés ne
remplacent pas la vérification du dialogue d’impression Windows et du pilote
de votre imprimante.
