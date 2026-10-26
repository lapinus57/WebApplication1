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
référence ne sont pas importés. L’historique des destinataires, messages et statuts
est centralisé dans `data/chat.db` sur le serveur. Tous les postes enregistrés
auprès de ce même serveur peuvent le consulter, lire les messages et l’exporter
en CSV, sans configurer leurs propres comptes SMTP/OVH pour cette consultation.
Les mots de passe SMTP/IMAP et clés API OVH restent dans les paramètres locaux et
ne sont pas transmis avec l’historique.

**Mettre à jour le serveur et tous les clients avant de reprendre les envois.**
Une ancienne version du client utilise encore son journal local et ne bénéficie
pas des réservations partagées. Ouvrir SMS et Mail sur chaque poste mis à jour
reprend automatiquement son ancien `history.db`, avec les statuts, dates, contenus
et clés de réservation disponibles. Le fichier local est conservé. L’import est
idempotent et reprend après une interruption ; il n’écrase jamais un statut déjà
présent sur le serveur. Les envois des autres postes deviennent alors visibles.

La carte indique le poste d’origine. Actualiser l’historique lit uniquement le
serveur, sans interroger OVH/IMAP ni envoyer de message. Une notification légère
actualise les pages ouvertes après un changement, avec une vérification toutes
les trente secondes pour reprendre après une reconnexion. La date d’actualisation
est affichée ; une erreur conserve la liste précédente en la signalant comme non
actualisée. Les résumés sont lus par pages de cinquante, les corps à la demande.

Une réservation durable par jour/contact/canal précède chaque appel fournisseur.
Cette réservation est atomique sur le serveur, y compris pour deux postes qui
essaient d’envoyer simultanément. Sans connexion au serveur, aucun nouvel envoi
n’est autorisé. Elle protège des renvois après rechargement ou changement de modèle. Une issue
réseau incertaine demande une vérification chez le fournisseur ; aucun nouvel
essai automatique n'est effectué. Une acceptation SMTP/OVH ne prouve pas la réception.

Si la connexion coupe après l’appel fournisseur, le résultat est conservé dans
une file locale durable. À la prochaine actualisation de l’onglet, il est transmis
au même serveur sans renvoyer le SMS/mail. Chaque mise à jour possède un identifiant
idempotent et vérifie la version du statut : un poste en retard ne remplace pas
un statut déjà modifié. Le conflit est signalé dans l’historique. Les transactions
et réservations subsistent après redémarrage du serveur. Sauvegarder `data/chat.db`
inclut l’historique partagé, ses réservations et les reçus de synchronisation.

L’accès utilise la connexion enregistrée du chat, sans restriction à un seul
utilisateur ou poste. Les appels d’historique d’une connexion non enregistrée sont
refusés ; le signal de changement ne contient aucun message ni destinataire. Ce
contrôle reprend le modèle d’accès du chat sur le réseau du cabinet, sans ajouter
une authentification individuelle. Les données Access ne sont jamais modifiées.

Actualiser les statuts SMS interroge OVH. Retours mail (IMAP) lit les notifications
avec le serveur, port et filtre expéditeur configurés et les identifiants SMTP.
La boîte est ouverte en lecture seule : aucun mail supprimé ou marqué lu.
Les DSN sont rapprochés par identifiant du message, destinataire et compte SMTP ;
les retours non identifiables sont ignorés. Le fournisseur doit proposer IMAP
avec ces identifiants et fournir des notifications exploitables.

Importer historique EyeEmail lit les tables SQLite `MailLog` et `SmsOutgoing`
en lecture seule. L'import est idempotent, étiqueté EyeEmail et conserve la source.
Les archives importées sont également synchronisées vers l’historique partagé.
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
réservations, import SQLite, analyse DSN et API OVH simulée, ainsi que deux postes
avec des bases locales distinctes : reprise des archives, concurrence familiale,
coupures, résultat en attente après redémarrage, accusé de synchronisation perdu,
statut obsolète, pagination et redémarrage du serveur. Les tests serveur de
`tests/Fundus.Tests` vérifient aussi les véritables méthodes du hub, l’enregistrement
et la déconnexion des postes, ainsi que les notifications sans contenu médical. Aucun message réel
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

L’historique partagé dispose d’un filtre par canal et d’une recherche par destinataire,
poste, date affichée ou statut. Chaque carte présente le contact, le canal, la date,
le poste d’origine et le résultat. Lire le message envoyé charge son contenu depuis
le serveur et l’affiche en texte lisible. Les aperçus ne chargent aucun contenu distant.

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
coordonnées ont changé ne peut pas être reconstitué. Après reprise des anciens
journaux, la protection est commune aux postes mis à jour connectés au même serveur,
et dépend toujours des familles/identifiants disponibles.

## Aperçu des mails et horaires de rappel

L’aperçu conserve sa hauteur compacte pour les SMS et s’agrandit pour les mails.
Agrandir le mail ouvre un lecteur plus large adapté à la fenêtre. Les aperçus
restent locaux, sans scripts ni ressources distantes ; le rendu final dépend du
logiciel mail du destinataire.

Paramètres > Horaires affichés dans les rappels permet de remplacer un créneau
par un autre. Les exemples 11:30 → 11:20 et 18:00 → 17:50 sont proposés, désactivés
par défaut. Activer les remplacements, modifier/ajouter/supprimer les règles,
enregistrer les paramètres puis préparer les messages. Un raccourci Adapter les
horaires des rappels figure dans le menu de chargement des envois.

Ces règles s’appliquent à tous les rendez-vous correspondant à l’heure et à la
minute d’origine, pour les SMS, les mails et les ICS. Le regroupement familial
utilise les données d’origine ; aucune écriture n’est effectuée dans Access.
Vérifier les horaires ajustés détaille les personnes et horaires avant/après pour
la préparation courante. Les règles ne se cumulent pas : 11:30 → 11:20 n’applique
pas ensuite une autre règle 11:20 → 11:10. Deux règles pour le même créneau sont
refusées. Les rendez-vous hors des créneaux restent inchangés.

Les clés anti-doublon ne dépendent pas de l’heure : modifier un horaire n’autorise
pas à renvoyer un rappel déjà traité. Toute modification des règles exige une
nouvelle préparation avant l’envoi.
