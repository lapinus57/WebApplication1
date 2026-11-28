using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EyeChat.SmsMail;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace Client
{
    /// <summary>
    /// Post-it EyeChat de la famille, pour le jour : s'ouvre au-dessus de StudioV quand un dossier est ouvert (sans lui prendre
    /// le clavier), enregistré sur le serveur pendant la frappe, visible dans le dossier de chaque membre et sur tous les postes.
    /// Les post-it de StudioV ne sont jamais touchés.
    /// </summary>
    public sealed partial class DayPostItWindow : Window
    {
        private readonly AppWindow _appWindow;
        private DayPostIt? _note;
        private bool _loading;
        private bool _dirty;
        private CancellationTokenSource? _savePause;
        /// <summary>Membres retirés avant la première saisie (le post-it n'existe pas encore sur le serveur).</summary>
        private readonly HashSet<int> _excluded = new();

        public DayPostItWindow()
        {
            InitializeComponent();
            var id = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
            _appWindow = AppWindow.GetFromWindowId(id);
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
            _appWindow.Resize(new Windows.Graphics.SizeInt32(420, 300));
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary).WorkArea;
            _appWindow.Move(new Windows.Graphics.PointInt32(area.X + area.Width - 440, area.Y + 20));
            // Fermer cache seulement la fenêtre : elle se rouvrira au prochain dossier.
            _appWindow.Closing += async (_, args) => { args.Cancel = true; await SaveNowAsync(); _appWindow.Hide(); };
        }

        public int? PatientId => _note?.PatientId;

        private static HubConnection Server() => App.ChatService.GetSmsMailHistoryConnection();

        /// <summary>
        /// Dossier ouvert dans StudioV : post-it du jour du patient (enregistre d'abord celui en cours). Avec
        /// <paramref name="onlyIfExists"/> (simple ouverture, pas de nouvelle consultation), la fenêtre ne s'ouvre que si la
        /// famille a déjà un post-it aujourd'hui ; sinon elle se cache.
        /// </summary>
        public async Task ShowForAsync(int patientId, bool onlyIfExists = false)
        {
            await SaveNowAsync();
            try
            {
                var note = await Server().InvokeAsync<DayPostIt>("DayPostItGet", patientId);
                if (onlyIfExists && !note.UpdatedAt.HasValue)
                {
                    if (_appWindow.IsVisible && _note?.PatientId != patientId) _appWindow.Hide();
                    return;
                }
                if (_note?.PatientId != patientId) _excluded.Clear();
                Show(note);
                _appWindow.Show(false); // au-dessus de StudioV, sans lui prendre le clavier
            }
            catch (Exception ex)
            {
                StatusText.Text = "Post-it indisponible : " + ex.Message;
            }
        }

        /// <summary>Un autre poste a modifié un post-it : relecture si c'est celui affiché et qu'il n'est pas en cours de saisie.</summary>
        public async Task RefreshIfShownAsync(IReadOnlyCollection<int> patients)
        {
            if (_note == null || _dirty || !patients.Contains(_note.PatientId) || !_appWindow.IsVisible) return;
            try { Show(await Server().InvokeAsync<DayPostIt>("DayPostItGet", _note.PatientId)); }
            catch (Exception) { }
        }

        private void Show(DayPostIt note)
        {
            _loading = true;
            _note = note;
            NoteText.Text = note.Text;
            _dirty = false;
            _loading = false;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var today = note.Members.Where(m => m.AppointmentTime.HasValue).Select(m => $"{m.Name} {m.AppointmentTime:HH:mm}").ToList();
            var others = note.Members.Where(m => !m.AppointmentTime.HasValue).Select(m => m.Name).ToList();
            FamilyText.Text = (note.Members.Count > 1 ? "Famille" : "Patient") + " — aujourd'hui : " + (today.Count > 0 ? string.Join(", ", today) : "aucun RDV")
                + (others.Count > 0 ? " · autres : " + string.Join(", ", others) : "");
            Title = "Post-it du " + note.Day.ToString("dddd d MMMM", fr);
            ShowMembers(note);
            StatusText.Text = note.UpdatedAt.HasValue ? $"Écrit par {note.UpdatedBy} à {note.UpdatedAt:HH:mm}." : "Pas encore de post-it aujourd'hui pour cette famille.";
        }

        /// <summary>Un bouton par membre : « Corinne 9:15 ✕ ». La croix retire la personne de ce post-it (elle aura le sien).</summary>
        private void ShowMembers(DayPostIt note)
        {
            MembersPanel.Children.Clear();
            var visible = note.Members.Where(m => !_excluded.Contains(m.PatientId)).ToList();
            if (visible.Count < 2) return;
            foreach (var member in visible)
            {
                var button = new Button
                {
                    Content = $"{member.Name}{(member.AppointmentTime.HasValue ? " " + member.AppointmentTime.Value.ToString("HH:mm") : "")}  ✕",
                    FontSize = 12, Padding = new Thickness(8, 2, 8, 2), Tag = member.PatientId,
                };
                ToolTipService.SetToolTip(button, "Retirer de ce post-it (pas de la même famille, ou pas concerné aujourd'hui)");
                button.Click += Remove_Click;
                MembersPanel.Children.Add(button);
            }
        }

        private async void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: int id } || _note == null) return;
            if (!_note.UpdatedAt.HasValue)
            {
                // Pas encore enregistré : la personne ne fera simplement pas partie du post-it.
                _excluded.Add(id);
                ShowMembers(_note);
                return;
            }
            try
            {
                await SaveNowAsync();
                await Server().InvokeAsync("DayPostItDetach", id);
                Show(await Server().InvokeAsync<DayPostIt>("DayPostItGet", _note.PatientId));
            }
            catch (Exception ex) { StatusText.Text = "Retrait impossible : " + ex.Message; }
        }

        private async void NoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _note == null) return;
            _dirty = true;
            StatusText.Text = "Modification…";
            _savePause?.Cancel();
            var pause = _savePause = new CancellationTokenSource();
            try { await Task.Delay(1200, pause.Token); } catch (TaskCanceledException) { return; }
            await SaveNowAsync();
        }

        private async void NoteText_LostFocus(object sender, RoutedEventArgs e) => await SaveNowAsync();

        private async Task SaveNowAsync()
        {
            if (!_dirty || _note == null) return;
            _savePause?.Cancel();
            var note = _note;
            var text = NoteText.Text;
            try
            {
                await Server().InvokeAsync("DayPostItSave", note.PatientId, text, note.Color, _excluded.ToList());
                _dirty = false;
                if (!note.UpdatedAt.HasValue && !string.IsNullOrWhiteSpace(text))
                {
                    // Première saisie : le post-it existe maintenant sur le serveur, avec ses membres définitifs.
                    note.UpdatedAt = DateTime.Now;
                    _excluded.Clear();
                    var saved = await Server().InvokeAsync<DayPostIt>("DayPostItGet", note.PatientId);
                    if (ReferenceEquals(_note, note) && !_dirty) { note.Members = saved.Members; ShowMembers(note); }
                }
                StatusText.Text = string.IsNullOrWhiteSpace(text) ? "Post-it effacé." : $"Enregistré à {DateTime.Now:HH:mm}, visible pour toute la famille sur tous les postes.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Non enregistré (serveur) : " + ex.Message;
            }
        }
    }
}
