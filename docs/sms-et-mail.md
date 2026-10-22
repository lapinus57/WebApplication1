# SMS et mail — référence EyeEmail

L'onglet est aligné sur [lapinus57/EyeEmail](https://github.com/lapinus57/EyeEmail),
révision `eb49ce0ffe175438150e6baa0fd1f6b719a1e5ae`. Il reprend son mappage des
rendez-vous, la consolidation des doublons, les regroupements de familles,
l'éditeur HTML local et les options calendrier. Le modèle fourni par le cabinet
et le bouton de test sont conservés.

## Configuration et utilisation

1. Configurer Access dans les paramètres RDV et les identifiants du poste.
2. Renseigner le cabinet et les comptes OVH / SMTP dans SMS et mail > Paramètres.
   SMTP utilise STARTTLS, généralement sur 587 ; le TLS implicite 465 n'est pas
   pris en charge par le transport SMTP actuel.
3. Adapter les modèles SMS et HTML, les options calendrier et les alarmes.
   L'éditeur HTML fonctionne localement, sans service externe.
4. Choisir le jour, charger les rendez-vous et préparer les messages.
5. Vérifier les familles, contacts et messages ; sélectionner les envois puis confirmer.

Les lectures Access ne modifient pas les bases. Le mappage accepte les alias
EyeEmail, plusieurs adresses mail et les téléphones annotés. Les rendez-vous
annulés et ABSENCE sont exclus. Les doublons d'un même patient et jour sont
consolidés en conservant le premier horaire et les coordonnées complémentaires.
Un tableau JSON de `RdvInfo` peut aussi être importé.

## Familles EyeEmail

L'option de regroupement est activée par défaut. Les passes reprennent EyeEmail :
téléphone/mail, adresse et ville, adresse, remarques, nom et date de création,
puis historique si demandé. La liste Familles EyeEmail affiche les membres,
contacts et méthodes utilisées. Vérifier les rapprochements avant l'envoi.

Une base familles Access facultative peut être choisie. Elle accepte
`FamilyMembership(FamilyId, PersonId)` ou l'ancien
`Patients(NumeroFamille, CodePatient)`, ainsi que `Person`, `ContactChannel` et
`RefContactType`. Les périodes de validité sont prises en compte. Les familles
connues priment sur les déductions ; une appartenance active contradictoire est
signalée. La base reste en lecture seule.

Le bouton Comparer l'historique RDV lit les huit années précédant l'année
courante. Chaque rapprochement historique proposé requiert une confirmation.
Le regroupement est recalculé à la préparation. Désactiver l'option familles
permet de revenir à la préparation par personne puis contact partagé.

Les messages énumèrent tous les rendez-vous de la famille. L'option SMS seulement
sans mail est évaluée au niveau de la famille, selon la validité syntaxique des
adresses. Les numéros français mobiles sont normalisés en +33. Un contact commun
reçoit un seul message par canal. Le nombre de segments SMS reste une estimation ;
la facturation réelle est celle d'OVH.

## Modèles et calendrier

Variables : `[PRENOMS]`, `[CABINET]`, `[ADRESSE_CABINET]`, `[DATE]`,
`[DATE_LONGUE]`, `[DatejourL]`, `[DatejourC]`, `[HEURE]`, `[LISTE_PERSONNES]`.
La casse des variables est indifférente. DATE est longue dans le HTML et courte
dans le SMS. Les modèles personnalisés existants sont conservés. Le bouton
Appliquer le modèle de rappel du cabinet restaure volontairement le modèle fourni.

L'invitation ICS est facultative. Elle utilise `METHOD:PUBLISH`, comme EyeEmail,
un objet personnalisable, l'état occupé/libre et des alarmes DISPLAY configurables
(en minutes avant le rendez-vous). Les alarmes peuvent être désactivées séparément.
Les heures UTC sont calculées depuis Europe/Paris avec changement été/hiver.
L'événement couvre du premier rendez-vous au dernier, augmenté de la durée
configurée (30 minutes par défaut). Son UID est stable par jour/contact.
Aucune réponse de participation n'est traitée. Le rendu dépend du logiciel mail ;
la pièce jointe rendez-vous.ics reste importable.

## Historique, retours et migration

Les paramètres sont chiffrés par DPAPI pour l'utilisateur Windows dans
`%LOCALAPPDATA%/EyeChat/SmsMail/settings.dat`. Les comptes privés du dépôt de
référence ne sont pas importés. `history.db` contient localement les destinataires
et messages, exportables en CSV. Les postes ne partagent pas ce journal.

Une réservation durable par jour/contact/canal précède chaque appel fournisseur.
Elle protège des renvois après rechargement ou changement de modèle. Une issue
réseau incertaine demande une vérification chez le fournisseur ; aucun nouvel
essai automatique n'est effectué. Une acceptation SMTP/OVH ne prouve pas la réception.

Actualiser les statuts SMS interroge OVH. Retours mail (IMAP) lit les notifications
avec le serveur, port et filtre expéditeur configurés et les identifiants SMTP.
La boîte est ouverte en lecture seule : aucun mail supprimé ou marqué lu.
Les DSN sont rapprochés par identifiant du message, destinataire et compte SMTP ;
les retours non identifiables sont ignorés. Le fournisseur doit proposer IMAP
avec ces identifiants et fournir des notifications exploitables.

Importer historique EyeEmail lit les tables SQLite `MailLog` et `SmsOutgoing`
en lecture seule. L'import est idempotent, étiqueté EyeEmail et conserve la source.
Ces archives restent consultables mais ne bloquent pas les nouveaux rappels :
leur date d'envoi ne permet pas de déduire sûrement le jour du rendez-vous.

L'alignement porte sur l'onglet d'envoi. Les utilitaires autonomes d'import de
patients et de reconstruction destructive de la base familles ne sont pas
intégrés. Les actions de fusion/ajout simulées dans EyeEmail ne sont pas reprises.
Les comptes configurables, le journal durable et la confirmation d'envoi sont
conservés au lieu de copier les constantes et suppressions automatiques de la source.

## Tester

Tester mail / SMS accepte une adresse mail, un numéro ou les deux. Le test utilise
les modèles et paramètres affichés, même non enregistrés, et un rendez-vous fictif
demain à 10 h à Paris pour Utilisateur TEST. Le mail respecte les options ICS et
alarmes. Les deux canaux sont testés même avec SMS seulement sans mail activé.
Les résultats sont journalisés comme TEST ; ils ne bloquent pas les vrais rappels.
Les SMS de test consomment les crédits habituels.

```sh
dotnet run --project tests/SmsMail.Tests
```

Les vérifications couvrent mappage, familles, annulations, modèles, calendrier,
réservations, import SQLite, analyse DSN et API OVH simulée. Aucun message réel
n'est envoyé par les tests. Le workflow Windows compile le client WinUI sur les
pull requests vers master2. Les bases Access réelles, comptes et rendu visuel
restent à vérifier sur le poste Windows de déploiement.

## Affichage et sélection des envois

Dans Envois, choisir Tous les canaux, Mails uniquement ou SMS uniquement, puis
rechercher éventuellement un nom, une famille ou un contact. Le compteur indique
les mails et SMS cochés parmi les résultats visibles. Cocher / décocher les
résultats agit uniquement sur cette liste et ne sélectionne pas les messages déjà
traités. Les choix masqués sont conservés mais ne sont jamais envoyés tant qu'ils
restent masqués.

Envoyer les mails et Envoyer les SMS limitent explicitement l'envoi au canal
indiqué, parmi les messages cochés et visibles. Le menu propose également l'envoi
de toute la sélection visible. La confirmation récapitule les quantités exactes.
Le filtre d'envoi ne change pas la règle de préparation « SMS seulement sans mail » :
pour préparer aussi des SMS aux familles avec mail, désactiver cette option puis
préparer à nouveau.

L'historique dispose d'un filtre par canal et d'une recherche par destinataire,
date affichée ou statut. Chaque carte présente le contact, le canal, la date et
le résultat. Lire le message envoyé ouvre son contenu complet, avec rendu HTML
pour les mails et texte pour les SMS. Les aperçus ne chargent aucun contenu distant.

## Tableau des envois et SMS par famille

La liste Envois est un tableau : sélection, canal, destinataire, personnes,
contenu/longueur et statut. Les colonnes restent alignées, avec défilement horizontal
sur les fenêtres étroites et aperçu du message sous le tableau.

Une famille regroupée produit un seul SMS contenant ses personnes, même avec
plusieurs mobiles. La préparation choisit un Téléphone 1 valide en priorité, puis
Téléphone 2 ; les membres sont ordonnés par identifiant pour stabiliser ce choix.
Le destinataire reste visible avant confirmation. Activer le regroupement des
familles et vérifier les rapprochements pour inclure tous leurs rendez-vous.

Le journal réserve atomiquement, pour le jour du rendez-vous, les numéros connus,
les identifiants patients et l'identifiant de famille EyeEmail disponible. Une
repréparation, un changement de numéro ou le redémarrage du client ne permettent
pas de répéter un envoi déjà réservé pour cette famille identifiable. Un résultat
incertain reste bloqué et doit être vérifié chez OVH. Le lendemain de rendez-vous
est une réservation distincte ; les tests restent indépendants.

Les anciens envois sont reconnus par leurs clés de contact lorsque le numéro
figure encore parmi les coordonnées connues. Les anciens journaux ne contiennent
pas les identifiants familiaux : un lien avec une famille dont toutes les
coordonnées ont changé ne peut pas être reconstitué. La protection reste locale
au journal du poste, et dépend des familles/identifiants disponibles.
