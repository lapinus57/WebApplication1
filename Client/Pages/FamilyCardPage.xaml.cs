using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.SmsMail.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Pages
{
    /// <summary>
    /// Fiche des rendez-vous à venir de toute une famille : le serveur cherche le patient (nom, prénom, date de naissance),
    /// assemble la fiche (familles + agenda) et l'envoie par mail ; le poste l'affiche et l'imprime.
    /// </summary>
    public sealed partial class FamilyCardPage : Page
    {
        public FamilyCardPage()
        {
            InitializeComponent();
        }

        private static HubConnection Server() => App.ChatService.GetSmsMailHistoryConnection();


        private CancellationTokenSource? _familyCardSearch;
        private FamilyCard? _familyCard;
        private readonly HashSet<int> _familyCardExcluded = new();

        /// <summary>La fiche telle qu'elle sera imprimée ou envoyée : sans les personnes décochées.</summary>
        private FamilyCard? VisibleFamilyCard => _familyCard == null ? null : FamilyCardDocument.Without(_familyCard, _familyCardExcluded);

        private void ShowFamilyCardMembers()
        {
            FamilyCardMembersPanel.Children.Clear();
            var members = _familyCard?.Members ?? new List<FamilyCardMember>();
            FamilyCardMembersHeader.Visibility = members.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (members.Count < 2) return;
            foreach (var member in members)
            {
                var count = _familyCard!.Appointments.Count(a => a.PatientId == member.PatientId);
                var box = new CheckBox
                {
                    Content = $"{FamilyCardDocument.Addressee(member)} ({(count == 0 ? "aucun RDV à venir" : count == 1 ? "1 RDV" : count + " RDV")})",
                    IsChecked = !_familyCardExcluded.Contains(member.PatientId), Tag = member.PatientId
                };
                box.Checked += FamilyCardMember_Toggled;
                box.Unchecked += FamilyCardMember_Toggled;
                FamilyCardMembersPanel.Children.Add(box);
            }
        }

        private void FamilyCardMember_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox { Tag: int id } box) return;
            if (box.IsChecked == true) _familyCardExcluded.Remove(id); else _familyCardExcluded.Add(id);
            RefreshFamilyCardPreview();
        }

        private void RefreshFamilyCardPreview()
        {
            var card = VisibleFamilyCard;
            if (card == null) return;
            FamilyCardPreview.NavigateToString(FamilyCardDocument.Html(card));
            var any = card.Members.Count > 0;
            FamilyCardPrintButton.IsEnabled = FamilyCardMailButton.IsEnabled = any;
            FamilyCardStatus.Text = any ? $"{card.Members.Count} personne(s), {card.Appointments.Count} rendez-vous à venir."
                : "Toutes les personnes sont décochées : cochez au moins une personne.";
        }

        private async void FamilyCardSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            // Petite pause pendant la frappe : une seule recherche au serveur quand on s'arrête de taper.
            _familyCardSearch?.Cancel();
            var pause = _familyCardSearch = new CancellationTokenSource();
            try { await Task.Delay(350, pause.Token); } catch (TaskCanceledException) { return; }
            await SearchFamilyCardAsync(sender.Text);
        }

        private async void FamilyCardSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            _familyCardSearch?.Cancel();
            await SearchFamilyCardAsync(args.QueryText);
        }

        private async Task SearchFamilyCardAsync(string text)
        {
            var query = (text ?? "").Trim();
            if (query.Length < 2) { FamilyCardResults.ItemsSource = null; FamilyCardStatus.Text = ""; return; }
            try
            {
                var found = await Server().InvokeAsync<List<FamilyCardCandidate>>("FamilyCardSearch", query);
                if (FamilyCardSearchBox.Text.Trim() != query) return; // une frappe plus récente a pris le relais
                FamilyCardResults.ItemsSource = found;
                FamilyCardStatus.Text = found.Count == 0 ? "Personne trouvée. (La recherche utilise la base patients du serveur : pensez à l'importer dans l'administration → Base patients.)"
                    : found.Count == 1 ? "Choisissez la personne pour créer la fiche." : $"{found.Count} personnes trouvées : choisissez la bonne.";
                if (found.Count == 1) FamilyCardResults.SelectedIndex = 0;
            }
            catch (Exception ex) { FamilyCardStatus.Text = "Recherche impossible : " + ex.Message; }
        }

        private async void FamilyCardResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FamilyCardResults.SelectedItem is not FamilyCardCandidate chosen) return;
            FamilyCardPrintButton.IsEnabled = FamilyCardMailButton.IsEnabled = false;
            FamilyCardStatus.Text = "Création de la fiche…";
            try
            {
                _familyCard = await Server().InvokeAsync<FamilyCard>("FamilyCardGet", chosen.PatientId);
                _familyCardExcluded.Clear();
                await FamilyCardPreview.EnsureCoreWebView2Async();
                FamilyCardEmailBox.ItemsSource = _familyCard.Emails;
                FamilyCardEmailBox.Text = _familyCard.Emails.FirstOrDefault() ?? "";
                ShowFamilyCardMembers();
                RefreshFamilyCardPreview();
            }
            catch (Exception ex) { FamilyCardStatus.Text = "Fiche impossible : " + ex.Message; }
        }

        private async void FamilyCardPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_familyCard == null || FamilyCardPreview.CoreWebView2 == null) return;
            try { FamilyCardPreview.CoreWebView2.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser); }
            catch (Exception) { await FamilyCardPreview.CoreWebView2.ExecuteScriptAsync("window.print()"); }
        }

        private async void FamilyCardMail_Click(object sender, RoutedEventArgs e)
        {
            try { await SendFamilyCardAsync(); }
            catch (Exception ex) { FamilyCardStatus.Text = "Envoi impossible : " + ex.Message; }
        }

        private async Task SendFamilyCardAsync()
        {
            var card = VisibleFamilyCard;
            if (_familyCard == null || card == null || card.Members.Count == 0) return;
            var email = (FamilyCardEmailBox.Text ?? "").Trim();
            if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new InvalidOperationException("Indiquez une adresse mail valide.");
            var dialog = new ContentDialog
            {
                Title = "Envoyer la fiche par mail ?", XamlRoot = XamlRoot, PrimaryButtonText = "Envoyer", CloseButtonText = "Annuler", DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"Fiche de {string.Join(", ", card.Members.Select(m => m.Name))} ({card.Appointments.Count} rendez-vous) à {email}." }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            FamilyCardStatus.Text = "Envoi par le serveur…";
            var result = await Server().InvokeAsync<string>("FamilyCardSendMail", _familyCard.PatientId, email, _familyCardExcluded.ToList());
            FamilyCardStatus.Text = result + " (visible dans l'historique SMS et mail).";
        }

    }
}
