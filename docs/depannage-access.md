# Alerte « Le système d’exploitation n’est pas actuellement configuré pour exécuter cette application »

Si **Préparation FO** et **SMS et Mail** affichent tous les deux cette alerte, vérifier l’accès au moteur Access/Office commun aux deux onglets. L’ouverture de Préparation FO lance une recherche automatique, donc peut déclencher le chargement du pilote immédiatement. Le texte de cette alerte seul ne permet pas de confirmer la cause ; le poste réel reste à vérifier.

## Vérifier le programme réellement exécuté

Dans Préparation FO, le pied de page indique **Client x86 (32 bits)** ou **Client x64 (64 bits)**, selon le processus réellement lancé. Le bouton **Diagnostic Access** consulte les déclarations Windows des fournisseurs, en 32 et 64 bits. Il ne lance pas Office, ne charge pas le pilote et n’ouvre aucune base. Le texte est sélectionnable pour être communiqué au support ; il contient l’architecture et les déclarations des moteurs, sans données patient, identifiants de connexion ni chemins de bases.

Une déclaration Windows et une DLL présente ne garantissent pas le bon fonctionnement du moteur : ses dépendances ou l’installation Office peuvent être endommagées. Le diagnostic ne teste pas les autorisations sur la base. Le fournisseur affiché comme configuré n’est pas forcément celui finalement sélectionné par la connexion, qui conserve ses fournisseurs de repli existants.

## Office/Access 32 bits

1. Dans Visual Studio, choisir **x86** et vérifier dans le Gestionnaire de configurations que le projet **Client** est lui aussi en x86.
2. Fermer les instances d’EyeChat en cours, nettoyer puis reconstruire le projet Client. Relancer la nouvelle compilation x86 et vérifier l’architecture indiquée dans Préparation FO. Pour une publication, utiliser le profil **win-x86**.
3. Avec le moteur ACE 32 bits associé à Office/Access 32 bits, le client doit réellement tourner en x86. Windows 64 bits peut parfaitement exécuter ce client 32 bits. La seule présence d’Office 32 bits ne prouve toutefois pas qu’ACE est installé correctement et accessible aux applications externes.
4. Si le client est déjà x86 et que l’alerte persiste dans les deux onglets, fermer EyeChat et les applications Office. Dans **Paramètres Windows → Applications → Microsoft Office/Access → Modifier**, essayer **Réparation rapide** si elle est proposée. Si le problème demeure, faire réparer l’installation existante d’Access Runtime/ACE 32 bits par l’administrateur du poste. Vérifier ensuite les fournisseurs déclarés et relancer un chargement de rendez-vous.

Le client x64 requiert un moteur compatible x64. Le serveur accédant directement à Access a sa propre architecture et sa propre installation ACE ; une correction du client n’installe aucun moteur sur le serveur.

## Correction des profils de compilation

Le projet fixait `AppxBundlePlatforms` à `x64`, même lorsque Visual Studio sélectionnait x86. De plus, l’évaluation du seul profil `win-x86` pouvait produire `Platform=x86`, `RuntimeIdentifier=win-x86`, mais `PlatformTarget=x64` : le profil ne remplaçait pas la cible par défaut. Ces incohérences de compilation/publication sont corrigées. Elles ne suffisent pas à expliquer une installation Office défaillante lorsque le processus était déjà réellement x86.

Les profils x86, x64 et ARM64 indiquent maintenant explicitement la cible CPU, le runtime et l’architecture du paquet. Hors profil, le runtime suit la cible choisie. Un test MSBuild vérifie douze configurations réelles et la CI compile le client en x86 et x64. Le fonctionnement du moteur ACE/Office et de la base réelle doit être vérifié sur le poste du cabinet.

Les requêtes Access, les règles FO/diabète/HTO, les comptes rendus et les données Access ne sont pas modifiés par ce correctif.
