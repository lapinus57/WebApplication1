# Registre serveur des patients : diabète et HTO

Le serveur conserve une table **PatientRegistry** dans sa base SQLite existante,
`data/chat.db`. Elle contient les patients de la table Patients d’Access, y compris
ceux sans rendez-vous et sans consultation. Elle est indépendante de la table
`Patients` du chat, utilisée pour la liste d’attente. Access reste en lecture seule.

## Utilisation

1. Mettre à jour le serveur puis ouvrir **Patients · Diabète / HTO** dans
   l’administration HTTPS : `https://serveur:5443/PatientRegistry`.
2. Dans **Réglages Access**, sélectionner les tables et clés Patients/Consultation,
   le nom, le prénom et la date de consultation. Les choix proviennent du schéma
   réel. Aucune correspondance avec l’agenda n’est nécessaire. Le chemin et les
   droits Access sont ceux du serveur : paramètres RDV ou `FoAccess:DatabasePath`,
   avec fournisseur ACE et éventuels identifiants de groupe de travail.
3. Cliquer **Importer / actualiser tous les patients**. La lecture s’exécute en
   arrière-plan sur le serveur ; le compteur montre son avancement. Fermer le
   navigateur ne l’annule pas. **Interrompre l’import** ou l’arrêt du serveur
   annule le travail. Un seul import peut s’exécuter à la fois.
4. Rechercher par nom, prénom ou identifiant Access ; filtrer par diabète, HTO ou
   dossiers à relire. La liste est paginée par 100 patients.
5. **Voir / vérifier** affiche les extraits et dates de consultation retenus.
   Corriger chaque état puis **Enregistrer la vérification**. Le choix
   **Analyse automatique** retire la correction pour ce champ.

Il n’y a pas d’import lors d’un simple GET ou d’une consultation de la liste. Le
registre enregistré reste consultable quand Access est indisponible. L’import
n’est pas programmé : le relancer pour intégrer les nouvelles données. Un import
annulé doit être relancé depuis le début ; aucune liste partielle n’est présentée
comme complète.

## États et relecture

Chaque condition possède un état détecté et une éventuelle correction manuelle :

| État | Signification |
| --- | --- |
| Non renseigné | Aucun élément personnel exploitable ; ne signifie pas Non. |
| Oui | Mention affirmative repérée, ou validation manuelle positive. |
| Non | Négation explicite repérée, ou validation manuelle négative. |
| À vérifier | Suspicion, mention non confirmée, contradiction ou contexte insuffisant. |

La liste distingue **Analyse du dossier** et **Validation manuelle**. Les états
automatiques sont des résultats textuels à vérifier, pas un diagnostic.
Seuls `Consultation.Observation` et `REFRACTION` sont analysés après conversion
RTF/HTML en texte ; `ProchainRDV` n’est jamais utilisé.

Le registre partage le repérage des mots du suivi annuel FO : diabète, diabétique,
DID/DNID, HTO, hypertension/hypertonie oculaire. Un chiffre de tonus, un traitement
isolé ou une HTA ne suffisent pas. Les antécédents familiaux, le prédiabète et les
mentions isolées de diabète insipide/gestationnel ne sont pas transformés en
diabète personnel. Les consultations futures sont ignorées.

Une mention de rétinopathie diabétique, y compris « pas de rétinopathie diabétique »,
propose une relecture sans prouver ni exclure le diabète si aucun énoncé personnel
plus explicite n’est disponible. Les consultations silencieuses ne changent pas
un état documenté ; une correction ou suspicion explicite ultérieure peut le
modifier. Les contradictions de même date restent À vérifier. Une réalisation ou
annulation de FO ne supprime jamais le statut chronique du registre.

Les corrections manuelles ne sont pas écrasées par un import. Si les éléments
retenus changent, **Nouveaux éléments à relire** apparaît. Le détail conserve la
correction, son auteur/date et la détection actuelle. Une page obsolète ne peut
pas remplacer une modification plus récente : il faut recharger.

Le registre ne modifie aucun dossier Access, rendez-vous ou prescription. Ses
validations ne remplacent pas les validations locales du client Préparation FO :
elles sont consultées et corrigées dans l’administration serveur. Le calcul FO
et le registre partagent le repérage textuel, avec des états différents selon
leur objectif (préparation annuelle ou statut du patient).

## Stockage et reprise

- `PatientRegistry` : source Access, identifiant patient, nom/prénom, présence
  au dernier import complet, date d’import et dernière consultation ; pour chaque
  condition, état détecté, extrait borné, date source, empreinte, correction,
  auteur/date et empreinte vérifiée ; révision contre les écrasements.
- `PatientRegistrySettings` : correspondances de champs, clé de source, révision
  de configuration et date/nombre du dernier import complet.

La contrainte unique `(SourceKey, AccessPatientId)` empêche les doublons. La source
repose sur le chemin Access et la table/clé Patients, pas sur le nom du patient.
Un changement de source exige de confirmer les réglages ; les données de deux
sources ne sont pas fusionnées sur la seule égalité d’identifiants. Un patient
absent d’un import réussi devient inactif, sans supprimer ses corrections : filtre
**Absents du dernier import**. Son retour dans Access réactive la même ligne.

La lecture et l’analyse de tous les patients précèdent une transaction SQLite
unique. Une erreur de conversion, de schéma ou de lecture conserve le précédent
registre. Un changement de configuration pendant le travail empêche sa validation.
Les comptes rendus complets ne sont pas recopiés : au maximum 600 caractères
d’éléments retenus par condition avec référence de consultation/champ. Les tables
sont créées de façon additive au démarrage. Sauvegarder `data/chat.db` inclut ce
registre et les validations.

L’écran exige la connexion HTTPS d’administration. Les écritures passent par des
POST protégés contre les CSRF. Les noms recherchés ne sont pas dans les URL ;
résultats et progression sont `no-store`. Aucun statut clinique ni extrait n’est
diffusé par le hub du chat. Les exceptions brutes Access/RTF ne sont pas affichées
ou journalisées.

## Vérifications et limites

`dotnet run --project tests/Fundus.Tests` vérifie aussi le registre : états et
chronologie, absence de mention, reprise SQLite, unicité/source, corrections et
révisions, échecs/annulations sans résultat partiel, import en arrière-plan, accès
authentifié, formulaires réels et CSRF, pagination et encodage HTML. Les données
de test sont fictives. Le workflow Windows compile le serveur et le client.

La véritable base du cabinet et ACE restent à tester sur le serveur Windows.
Limites : 100 000 patients, 10 000 consultations par patient, 100 000 caractères de
texte par consultation et 5 000 000 par patient, plus les limites RTF/HTML
existantes. Un dépassement arrête l’import sans remplacer la liste précédente.
