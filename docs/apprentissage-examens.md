# Apprentissage local des examens

Depuis **Préparation FO → Apprentissage local**, une page WinUI permet de préparer séparément **FO, OCT, V3F et Skiacol** pour les prochains rendez-vous. Elle contient les patients de la période, y compris ceux qui ne déclenchent aucune alerte FO dans le système existant. Un modèle activé classe les dossiers en Oui/Non lorsque ses critères techniques sont satisfaits ; les autres restent **À confirmer** dans la liste, sans ouvrir de fenêtres en boucle.

Cette première version fournit un entraînement supervisé local, pas un modèle clinique déjà entraîné. Les règles FO existantes restent disponibles dans leur écran habituel. Le classement appris est accessible dans la nouvelle page. Il ne prescrit ni n’administre de traitement : « Skiacol : Oui » est une préparation à valider dans l’organisation du cabinet, sans posologie ni ordre d’administration.

## Constituer les exemples sur le poste d’apprentissage

1. Effectuer d’abord une recherche réussie dans Préparation FO afin d’enregistrer la correspondance des champs Access.
2. Ouvrir **Apprentissage local**, choisir une période future de 1 à 93 jours puis **Charger les patients**. L’ouverture de la page seule ne connecte pas Access. Le fournisseur déjà configuré, notamment Jet 4.0 sur un client x86, est conservé.
3. Sélectionner un patient et relire ses trois dernières consultations, affichées par date décroissante, en texte simple issu d’Observation et REFRACTION. Les consultations futures sont exclues.
4. Renseigner **Oui** ou **Non** pour chaque examen relu. **Non renseigné** signifie que cet examen ne sera pas appris pour ce patient ; cela ne devient jamais un Non implicite. Les réponses ne sont pas préremplies par le modèle.
5. Pour expliquer une réponse, sélectionner une phrase dans le texte, choisir l’examen puis **Ajouter la sélection comme explication**. Plusieurs extraits sont possibles, y compris pour une réponse Non. Inclure la négation et le contexte utile, plutôt qu’un mot isolé ou une identité. Ces passages alimentent la recherche de critères textuels appris. Pour changer le sens d’une réponse, effacer les anciens extraits incompatibles puis les refaire.
6. **Enregistrer mes réponses et extraits**. Un contrôle relit la source avant enregistrement. Les corrections restent locales et ne modifient aucune table Access.

Un dossier correspond à un patient dans une base et reste toujours dans le même groupe d’apprentissage ou de test. Une nouvelle validation remplace son exemple précédent ; elle ne crée pas un deuxième exemplaire qui pourrait contaminer le groupe de test. Les réponses manuelles s’affichent « relu » uniquement si la date du rendez-vous, les textes et les signaux sont toujours identiques. Le jeu local accepte 1 000 patients au maximum, 24 extraits de 1 200 caractères par patient et 100 000 caractères de consultation par dossier. Les dépassements sont signalés sans tronquer silencieusement un compte rendu.

## Entraîner et vérifier

**Entraîner et tester** construit un arbre binaire indépendant pour chaque examen. Les seules cibles d’apprentissage sont vos réponses humaines. Les règles existantes fournissent des indices, jamais des réponses d’entraînement automatiques.

Les indices combinent les consignes et la chronologie FO de l’historique, le diabète/HTO détectés, les actes CCAM dont la convention locale BZQK001 = OCT-FO, les demandes des dernières consultations et des séquences de 1 à 4 mots issues de vos annotations. Les nombres sont conservés dans ces séquences. Seuls les critères présents chez au moins cinq patients d’entraînement peuvent entrer dans le vocabulaire ; celui-ci est limité à 128 critères textuels par examen. Les arbres ont au plus quatre niveaux de séparation et cinq exemples par feuille. Les textes des trois consultations sont réunis pour les indices textuels ; leur ordre n’est pas interprété comme le ferait un lecteur médical. L’historique antérieur contribue aux signaux structurés FO, mais pas aux nouveaux critères textuels appris.

Environ 20 % des patients sont réservés aux tests par une répartition stable. Leurs textes, annotations et réponses ne servent ni à choisir le vocabulaire ni à construire les arbres. Le rapport affiche par examen les effectifs, les Oui/Non corrects, les **faux Oui**, les **faux Non** et le nombre de **questions** qui seraient demandées sur les tests. Les erreurs sont comptées sur la suggestion brute, y compris lorsqu’elle finit À confirmer : les abstentions ne masquent donc pas les erreurs.

Le classement direct exige, pour chaque examen :

- au moins cinq Oui et cinq Non dans les tests réservés ; sensibilité et spécificité observées d’au moins 90 % ;
- une feuille contenant au moins dix exemples, dont au moins 90 % portent la même réponse ;
- une consultation disponible et un indice appris effectivement présent, au-delà de la simple absence de mots ;
- pour FO, aucune contradiction signalée par l’analyse chronologique existante.

Sinon le dossier reste **À confirmer**, et vous pouvez lui donner une réponse et des extraits. **Pourquoi ce classement ?** montre les critères parcourus, les passages retrouvés et l’effectif de la feuille. L’accord des exemples n’est pas une probabilité clinique. Les seuils servent à limiter le classement automatique ; ils ne certifient pas la qualité médicale. Les formulations inconnues, négations complexes et relations entre anciennes consignes et nouvelles consultations peuvent encore être mal interprétées. Tester avec des dossiers représentatifs, notamment des vrais Non et des cas contradictoires, et relire les erreurs avant l’usage en production. Un test supplémentaire sur de nouveaux patients est nécessaire après plusieurs cycles de correction pour éviter de s’adapter uniquement au jeu de test initial.

## Exporter puis utiliser le modèle en production

1. Vérifier le rapport du modèle candidat puis **Exporter le modèle** pour obtenir son fichier JSON.
2. Sur le poste de production : **Importer un modèle**, consulter ses résultats puis **Activer le classement**.
3. **Charger les patients** pour afficher Oui/Non ou À confirmer avec ce modèle. Un import seul ne remplace pas le modèle actif. Une activation vide la liste précédente pour imposer un nouveau calcul. **Désactiver** arrête le classement appris et conserve les exemples locaux.

L’entraînement et l’inférence ne contactent aucun service d’IA. Aucun paquet Python, moteur génératif ou exécutable téléchargé n’est nécessaire. Le modèle est un objet JSON strictement validé : version et format des critères, taille, profondeur, effectifs et nombre de nœuds. Il ne contient ni code à exécuter, ni comptes rendus, ni extraits en clair, ni identifiants de patients, ni dates de rendez-vous. Il conserve les empreintes des critères textuels, les critères structurés, les arbres et les résultats agrégés. Les empreintes ne constituent pas une garantie d’anonymisation mathématique : un vocabulaire supposé peut être testé contre une empreinte. Ne pas annoter d’informations d’identité comme critère d’examen et conserver le modèle dans le circuit du cabinet.

Les dossiers et extraits d’entraînement restent dans `%LOCALAPPDATA%/EyeChat/Fundus/learning.dat`, chiffrés avec DPAPI pour le compte Windows courant. Ce fichier contient bien des données cliniques locales ; il ne doit pas être joint à un ticket ou publié dans GitHub. La sauvegarde remplace le fichier de façon atomique. Une erreur de lecture ne remet pas à zéro les exemples. Ce jeu ne se partage pas automatiquement entre postes et n’est pas l’historique partagé des envois SMS/mail. Seul le modèle JSON est conçu pour être transféré ; chaque poste l’active explicitement. Une même collection d’exemples ne mélange pas deux bases Access.

## Vérification du logiciel

`dotnet run --project tests/Fundus.Tests` couvre l’apprentissage indépendant, les exemples négatifs, l’isolation des patients et des annotations de test, les abstentions, les erreurs de validation, l’export sans dossiers, les imports invalides et cycliques, l’annulation, la chronologie des sources et les limites de taille. Tous les exemples de ces tests sont synthétiques. Les builds Windows x86/x64 vérifient le code et le XAML ; la sélection de texte, DPAPI, les sélecteurs de fichiers et la lecture réelle de votre base Jet restent à essayer sur le poste Windows.
