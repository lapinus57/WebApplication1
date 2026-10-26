# IA locale : comprendre une consigne et proposer une règle

Cette version utilise réellement un modèle de langage via **Ollama**, distinct de l’arbre d’apprentissage. Le modèle interprète une consigne en français et propose une règle structurée. Il ne l’active jamais seul : les valeurs affichées doivent être relues, éventuellement corrigées, puis validées dans EyeChat.

## Installation sur le poste de test

1. Installer [Ollama pour Windows](https://ollama.com/download/windows).
2. Dans PowerShell, télécharger le modèle :

```powershell
ollama pull qwen2.5:7b
ollama list
```

3. Laisser Ollama ouvert en arrière-plan. Si l’application n’est pas démarrée, la lancer ; `ollama serve` est aussi possible depuis un terminal, mais ne pas démarrer un deuxième serveur si le port est déjà utilisé.
4. Ouvrir **Préparation FO → Apprentissage local → IA locale et règles** dans EyeChat.

Le Ryzen 7, les 16 Go de RAM disponibles et une RTX 4070 constituent une configuration adaptée à un premier essai du modèle 7B quantifié proposé. L’usage réel du GPU et les temps de réponse restent à vérifier sur le poste : la CI ne dispose pas de cette carte ni du modèle. `ollama ps` permet de voir le modèle chargé et son placement après une requête.

Ollama fonctionne dans un processus séparé. **EyeChat reste en x86 pour Jet/Access 32 bits** ; l’IA locale peut utiliser le GPU sans changer le fournisseur Access. Le téléchargement initial nécessite Internet. EyeChat appelle ensuite uniquement `http://127.0.0.1:11434/api/chat`, avec le modèle fixe `qwen2.5:7b`, sans proxy ni redirection HTTP. L’ouverture des écrans ne lance aucune requête d’IA.

## Premier essai sans dossier patient

Saisir :

> Pour une myopie plus forte que −5, préparer un FO tous les cinq ans.

Cliquer **Comprendre ma consigne avec l’IA locale**. La proposition attendue est : critère Myopie, sphère strictement inférieure à −5, examen FO/OCT et délai **60 mois depuis le dernier examen documenté**. Vérifier ces quatre valeurs. Le bouton **Valider ces valeurs et activer la règle** constitue l’activation humaine explicite.

Essayer ensuite une consigne incomplète :

> Pour une myopie plus forte que −5, prévoir un FO.

L’IA doit demander le délai au lieu de le fabriquer. Compléter la consigne puis relancer. Une formulation ambiguë comme « myopie > −5 » doit aussi susciter une précision sur le sens du seuil. Toute modification du texte invalide la proposition précédente ; il faut relancer l’interprétation. Une erreur de l’IA peut être corrigée dans le formulaire avant activation. Les réponses ne sont pas garanties correctes : cette relecture fait partie du fonctionnement.

## Critères gérés par cette première intégration

- Diabète documenté.
- HTO documentée, distincte de l’hypertension artérielle.
- Myopie avec un seuil de sphère strictement inférieur à une valeur négative.

Une règle contient un critère, un examen (FO/OCT, V3F ou Skiacol) et un délai entier de 1 à 120 mois depuis le dernier examen documenté. Le modèle doit demander une précision pour un critère non pris en charge, une consigne négative, plusieurs règles dans une phrase, un délai absent ou une demande de prescription/posologie. Ce périmètre limité évite de transformer arbitrairement un texte médical en code exécutable.

Le modèle ne consulte pas automatiquement Access et aucun dossier patient n’est joint à la demande. **Seul le texte saisi dans « Ma consigne en français » est envoyé à Ollama.** Les explications d’un dossier peuvent être reformulées ici comme une règle générale. L’analyse générative des consultations avec citations de preuves n’est pas encore intégrée à cet écran.

## Application aux patients

Après activation, revenir à l’apprentissage et **recharger les patients**. Le logiciel applique les règles validées sans rappeler Ollama. L’explication mentionne la règle et son identifiant court.

Les règles ajoutent des indications de préparation ; elles ne suppriment pas une consigne existante et ne retardent pas un suivi déjà plus rapproché, notamment les règles annuelles ou de myopie présentes dans le cabinet. Modifier une règle ajoutée ne modifie pas ces règles de base.

Pour FO/OCT, le logiciel recherche le dernier examen documenté à partir des mentions de réalisation et des actes reconnus. Une date inconnue, une annulation ou une contradiction impose une confirmation ; la simple absence d’indication ne produit pas un Non automatique. La myopie doit être explicitement documentée avec une valeur : un cylindre négatif isolé n’est pas assimilé à une myopie.

Pour **V3F et Skiacol**, cette version conserve la règle mais exige une vérification de la date du dernier examen : elle ne réutilise jamais la date du FO à sa place. Ces règles ne produisent donc pas de Oui automatique sur leur seule échéance. Aucune administration de médicament ni posologie n’est déclenchée.

## Corriger, désactiver et transférer

Sélectionner une règle enregistrée pour afficher ses valeurs. Corriger puis valider pour remplacer cette règle. **Désactiver la sélection** conserve la règle inactive ; **Supprimer la sélection** la retire de ce poste. Recharger ensuite les patients pour actualiser les résultats.

**Exporter les règles actives** crée un JSON contenant seulement les critères, seuils, délais, examens et métadonnées de validation. Il ne contient ni les consignes libres envoyées au modèle, ni les dossiers. Sur un autre poste équipé de cette version, **Importer pour relecture** ajoute des règles inactives : sélectionner et valider chacune avant usage. L’import n’active rien et ne remplace pas les règles actives existantes. La collection est limitée à 50 règles.

Le fichier de règles est **distinct du modèle d’arbre d’apprentissage** et du modèle Qwen. Le poste de production n’a pas besoin d’Ollama pour appliquer des règles déjà validées. Ollama est nécessaire uniquement pour interpréter de nouvelles consignes. Corriger une proposition ne réentraîne pas les poids de Qwen : cela enrichit les règles explicites du cabinet.

Les règles du poste sont enregistrées dans `%LOCALAPPDATA%/EyeChat/Fundus/rules.dat`, chiffrées avec DPAPI pour le compte Windows courant. Les écritures sont atomiques. Un fichier illisible n’est pas remis à zéro. Le JSON importé est validé et ne peut contenir ni script, ni SQL, ni expression régulière exécutable. Les réponses d’IA sont limitées en taille et en durée ; les messages d’erreur ne reproduisent pas les consignes.

## Vérifications

Les tests automatiques utilisent des dossiers synthétiques et un service HTTP simulé : propositions valides et incomplètes, seuils signés, négations, dates manquantes, absence d’activation à l’import, refus de propriétés inconnues, endpoint local, erreurs HTTP, réponses tronquées/trop volumineuses et annulation. Ils vérifient l’intégration et les garde-fous, **pas la qualité clinique ni la qualité linguistique du modèle réel**. Les premiers essais Qwen doivent être effectués sur le poste de test avec des consignes synthétiques avant de valider des règles de production.
