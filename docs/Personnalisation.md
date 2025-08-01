# Personnalisation de l'application

Cette page décrit les principaux éléments personnalisables de l'application **EyeChat** côté client et côté serveur.

## Couleurs de l'interface

Le client permet de personnaliser les couleurs de la barre de titre, du menu de navigation, ainsi que les couleurs des bulles de messages. Ces valeurs sont sauvegardées localement dans un fichier *settings.json* lié à l'utilisateur courant et sont appliquées via `AppSettings.SetObject("Colors", ...)`. Depuis la version 2.0, ce fichier est synchronisé avec le serveur afin que chaque poste dispose du même `{utilisateur}_settings.json`.

Les propriétés concernées se trouvent dans `AppColorSettings` :

```csharp
public class AppColorSettings
{
    public string TitleBarColor { get; set; } = "#FF0078D7";
    public string TextTitleBarColor { get; set; } = "#FF000000";
    public string NavigationViewColor { get; set; } = "#FFE6F1FF";
    public string TextNavigationViewColor { get; set; } = "#FF000000";
    public string MyMessageColor { get; set; } = "#FFCCE5FF";
    public string TextMyMessageColor { get; set; } = "#FF000000";
    public string OtherMessageColor { get; set; } = "#FFD9F2DC";
}
```
【F:Client/Models/AppColorSettings.cs†L1-L14】


Le thème clair ou sombre est mémorisé séparément via `SettingsViewModel.AppTheme`. Cette

propriété met à jour `Application.Current.RequestedTheme` pour appliquer le thème
à toute l'application :

```csharp
public string AppTheme
{
    get => _appTheme;
    set
    {
        if (_appTheme != value)
        {
            _appTheme = value;
            OnPropertyChanged(nameof(AppTheme));
            AppSettings.Set("AppTheme", value);
            ApplyTheme(value);

        }
    }
}
```
【F:Client/ViewModel/SettingsViewModel.cs†L82-L95】

Ces paramètres sont modifiables dans la page `AppearanceSettingsPage` où des sélecteurs de couleurs permettent de choisir la teinte voulue.

Depuis cette version, la ressource `SystemAccentColorDark1` est fournie sous la forme d'un `Color` afin que l'accentuation fonctionne correctement même lorsque le fond de l'application est sombre.

## Raccourcis clavier

Les touches **F5** à **F8** permettent de coller du texte prédéfini selon le contexte (Réfraction, Lentilles, Pathologies, Orthoptie). Les combinaisons **Ctrl+F9** à **Ctrl+F12** et **Shift+F9** à **Shift+F12** déclenchent l'ouverture d'une fiche patient correspondant à un examen.

Les valeurs sont stockées et chargées par `SettingsViewModel` via `AppSettings`. Exemple :

```csharp
public string ShortcutF5Refraction
{
    get => _shortcutF5Refraction;
    set { if (_shortcutF5Refraction != value) { _shortcutF5Refraction = value; OnPropertyChanged(nameof(ShortcutF5Refraction)); Set("ShortcutF5Refraction", value); } }
}
```
【F:Client/ViewModel/SettingsViewModel.cs†L12-L74】

## Liste des examens et salles

La page `ExamRoomPage` permet de gérer la liste des examens médicaux disponibles et les salles associées. Chaque examen possède un nom, une couleur, un code clavier et une annotation. Les données sont sauvegardées dans `exam_options.json` via `ExamOption.Save` et dans `rooms.json` via `RoomList.Save` :

```csharp
public static readonly string FilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "EyeChat",
    "exam_options.json");
```
【F:Client/Models/ExamOption.cs†L63-L68】

```csharp
public static readonly string FilePath =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EyeChat", "rooms.json");
```
【F:Client/Helpers/RoomList.cs†L9-L13】

La configuration peut être envoyée au serveur ou chargée depuis celui‑ci via `SendExamOptionsAsync`, `SendRoomsAsync`, `GetExamOptionsAsync` et `GetRoomsAsync` du service SignalR.

## Style d'affichage du chat

L'utilisateur peut choisir entre un affichage "old school" (type IRC) ou "moderne". Ce choix est enregistré dans les paramètres (`ChatDisplayStyle`) et déclenche `DisplayStyleChanged` qui applique le style sur la page de chat :

```csharp
public bool IsOldSchoolMode
{
    get => _isOldSchoolMode;
    set
    {
        if (_isOldSchoolMode != value)
        {
            _isOldSchoolMode = value;
            OnPropertyChanged(nameof(IsOldSchoolMode));
            AppSettings.Set("ChatDisplayStyle", value ? "OldSchool" : "Modern");
            DisplayStyleChanged?.Invoke(value ? ChatStyle.OldSchool : ChatStyle.Modern);
        }
    }
}
```
【F:Client/ViewModel/SettingsViewModel.cs†L10-L36】

```csharp
public bool UseSenderColorForBubbles
{
    get => _useSenderColorForBubbles;
    set
    {
        if (_useSenderColorForBubbles != value)
        {
            _useSenderColorForBubbles = value;
            OnPropertyChanged(nameof(UseSenderColorForBubbles));
            AppSettings.Set("UseSenderColorForBubbles", value ? "True" : "False");
            BubbleColorModeChanged?.Invoke(value);
        }
    }
}
```
【F:Client/ViewModel/SettingsViewModel.cs†L132-L145】

## Autres réglages

* Taille de la police des messages (`MessageFontSize`).
* Adresse du serveur SignalR (chargée au démarrage dans `SignalRService`).
* Mémorisation de l'utilisateur sélectionné pour l'envoi des messages (`AppSettings.CurrentSelectedUser`).
* Couleur des bulles basée sur celle de l'expéditeur (`UseSenderColorForBubbles`).
* Initiales affichées dans la barre de titre (`Initials`).
* Avatar de l'utilisateur (`Avatar`).

L'avatar peut provenir d'une image intégrée ou d'un fichier importé. Le chemin
de ce fichier est mémorisé dans le *settings.json* de l'utilisateur et envoyé au
serveur afin de s'afficher de la même façon sur chaque poste.
La sélection s'effectue depuis la page **Utilisateur** via le bouton "Changer..."
qui propose plusieurs icônes intégrées ou l'import d'une image personnelle.

Ces paramètres sont tous stockés localement afin d'être conservés entre les sessions de l'application.
Ils sont également envoyés au serveur pour être partagés entre plusieurs clients.
