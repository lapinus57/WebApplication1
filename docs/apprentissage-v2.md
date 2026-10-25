# Entraînement local v2 — guide de démarrage

Ouvrir **Préparation FO → Apprentissage local → Charger les patients**, puis sélectionner un patient.

## Lire le dossier

L’en-tête affiche l’âge au rendez-vous, la date de la dernière consultation et le dernier FO/OCT documenté, avec sa source (acte CCAM du cabinet ou mention de réalisation). Une information absente reste indiquée comme inconnue. L’âge utilise un champ date de naissance reconnu dans Patients ; s’il est absent, ambigu ou non enregistré comme date, aucun âge n’est inventé.

Le texte principal contient uniquement la dernière consultation : **REFRACTION, puis Observation**. Les dates des consultations restent visibles. Les observations futures sont ignorées. L’historique complet reste utilisé par les règles chronologiques existantes.

## Donner des exemples

1. Répondre Oui ou Non pour FO/OCT, V3F et Skiacol, d’après sa relecture.
2. Sélectionner les passages utiles puis les ajouter comme explications.
3. Compléter **Mon explication pour le prochain rendez-vous** en écriture libre.
4. Enregistrer les réponses et extraits, puis **Entraîner et tester** après plusieurs dossiers variés.

Le moteur reste un arbre local, pas un modèle génératif qui comprend toute phrase médicale. Les explications proposent des expressions candidates à l’apprentissage uniquement lorsqu’elles se retrouvent aussi dans le texte clinique de la dernière consultation. Elles ne sont jamais ajoutées aux données cliniques comme si elles avaient été relevées dans Access. Les réponses humaines Oui/Non restent les seules cibles. Une consigne nouvelle écrite librement n’est pas exécutée automatiquement comme une règle.

## Expliquer les anciens FO

Déplier **Anciens FO : pourquoi cet examen a-t-il été réalisé ?**. L’écran recherche au maximum cinq FO documentés et présente pour chacun la consultation concernée et la consultation précédente. Sélectionner un passage, cliquer sur **Ajouter le passage historique sélectionné**, puis écrire son explication dans le champ historique. Enregistrer avec les réponses du dossier, ou uniquement les explications si les examens du prochain rendez-vous restent non renseignés. Une absence de réponse ne devient pas un Non.

Ces explications restent distinctes des réponses du prochain rendez-vous. Leurs expressions peuvent enrichir les critères appris seulement si elles sont encore présentes dans la consultation actuelle et associées à une réponse humaine actuelle. Un ancien FO ne devient jamais automatiquement un Oui pour un futur rendez-vous. L’outil ne prétend pas reconstituer seul la motivation du médecin.

## Règle myopie du cabinet

La règle demandée est **myopie plus forte que −5,00 → FO/OCT tous les cinq ans à partir du dernier examen documenté**, échéance évaluée au rendez-vous. Elle s’ajoute aux règles existantes diabète/HTO et aux consignes explicites.

Pour limiter les confusions, cette première détection requiert une mention explicite telle que `Myopie -6,00` ou `Myopie OD : -6.00` dans la dernière consultation. Un cylindre négatif seul, une valeur positive, −5,00 exactement, une négation ou un antécédent familial ne suffisent pas. Les prescriptions complexes non reconnues restent à relire. Sans dernier FO fiable, ou avec contradiction/annulation, la règle ne donne pas automatiquement Oui. Avant l’échéance, elle ne donne pas automatiquement Non : d’autres motifs peuvent exister.

## Migrer et exporter

Les anciens exemples et modèles sont conservés dans le fichier local chiffré. Le changement de texte d’entrée impose de relire et réenregistrer les dossiers, puis de réentraîner : les exemples v1 sont exclus du nouvel entraînement et le classement appris des modèles v1 est désactivé. Les règles connues du cabinet continuent à fonctionner.

Après vérification des faux Oui et faux Non, exporter le modèle v2. Sur les autres postes équipés de cette version : importer, activer et recharger les patients. Les commentaires, extraits historiques et dates de naissance ne sont pas exportés dans le modèle. Aucune donnée n’est envoyée à une IA externe et Access reste en lecture seule.
