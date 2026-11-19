# Fonctionnement de l'application

L'application EyeChat se compose d'un **serveur ASP.NET Core** et d'un **client WinUI**. Les deux communiquent via SignalR.

## Serveur

Le projet `Serveur` héberge une application ASP.NET Core qui expose un hub SignalR (`ChatHub`). Celui‑ci gère :

* L'enregistrement et la déconnexion des utilisateurs
* L'envoi des messages texte
* La gestion de groupes protégés par mot de passe
* Le stockage des paramètres communs (examens disponibles et salles)

La base SQLite est initialisée au démarrage dans `Program.cs` :
```csharp
var dbFolder = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dbFolder);
var dbPath = Path.Combine(dbFolder, "chat.db");

builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
```
【F:Serveur/Program.cs†L6-L12】

Les appels clients utilisent principalement les méthodes de `ChatHub`, par exemple `SendMessage`, `JoinProtectedGroup`, `GetExamOptions` ou `SaveRooms`.

## Client

Côté client, le service `SignalRService` maintient la connexion avec le serveur. Il expose des méthodes pour envoyer des messages, synchroniser les examens et les salles et charger l'historique. Une partie du code d'initialisation :
```csharp
Connection = new HubConnectionBuilder()
    .WithUrl($"{ServerAddress}/chatHub")
    .WithAutomaticReconnect()
    .Build();
```
【F:Client/Services/SignalRService.cs†L60-L67】

Le client stocke en local différentes informations : messages du jour, utilisateurs connus, paramètres visuels et raccourcis clavier. Ces fichiers sont créés dans le dossier `LocalApplicationData/EyeChat`.

Les pages XAML offrent l'interface utilisateur :
* `ChatPage` pour la messagerie
* `ExamRoomPage` pour la gestion des examens et des salles
* `AppearanceSettingsPage` et `UserSettingsPage` pour les paramètres

## Persistance des données

Certaines données sont partagées entre les utilisateurs via la base de données du serveur :

* Table `SecureGroups` : stockage des groupes protégés et de leur mot de passe chiffré
* Table `ServerConfig` : contient la configuration des examens et des salles sérialisée en JSON

D'autres données sont uniquement locales au client (par exemple l'historique du jour ou les préférences utilisateur).

## Flux typique

1. Au lancement du client, `SignalRService.InitializeAsync` se charge de récupérer la liste des utilisateurs et les messages du jour auprès du serveur.
2. L'utilisateur saisit un message sur `ChatPage`. Celui‑ci est envoyé via `SendMessage` au hub SignalR, puis distribué aux destinataires.
3. Les examens et salles peuvent être modifiés sur `ExamRoomPage` et synchronisés manuellement avec le serveur.
4. Les paramètres visuels ou de raccourcis sont sauvegardés immédiatement dans les fichiers locaux.
5. Quand un poste déclare un patient (raccourci clavier ou bouton), le serveur le diffuse à tous les postes (`NewPatient`). Chaque autre poste affiche une notification Windows : « Nouveau patient · salle », l'identité, puis l'examen, l'œil et le déclarant. Le poste qui déclare n'en reçoit pas. Chaque utilisateur peut la désactiver dans Paramètres → Apparence → « Notification quand un autre poste déclare un patient ».

Ainsi l'application fournit un système de messagerie interne avec des options personnalisables adaptées au flux de travail médical.
6. Les listes de patients (page Chat et Historique) ont un onglet par salle d'attente, plus « Toutes ». Paramètres → Apparence → « Salle d'attente affichée par défaut » choisit l'onglet sélectionné à l'ouverture (sans choix : la première salle). Quand la liste des salles change, l'onglet en cours reste affiché ; si la salle choisie a été supprimée, la première salle est affichée.
