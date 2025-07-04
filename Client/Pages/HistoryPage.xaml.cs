using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Models;
using Client.Services;
using Client.Helpers;
using Microsoft.UI.Xaml;
using System;

namespace Client.Pages
{
    public sealed partial class HistoryPage : Page
    {
        public ObservableCollection<Patient> ArchivedPatients { get; } = new();
        public ObservableCollection<Patient> NonArchivedPatients { get; } = new();
        public ObservableCollection<string> Rooms { get; } = RoomList.Load();
        private ObservableCollection<string> RoomsWithAll { get; } = new();
        private readonly SignalRService _service;

        public HistoryPage()
        {
            this.InitializeComponent();
            _service = App.ChatService;
            DataContext = this;
            Loaded += HistoryPage_Loaded;
            Rooms.CollectionChanged += Rooms_CollectionChanged;
        }

        private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPatientsAsync();
        }

        private async Task LoadPatientsAsync()
        {
            var notArchived = await _service.GetPatientsAsync();
            NonArchivedPatients.Clear();
            foreach (var p in notArchived.OrderBy(p => p.HoldTime))
                NonArchivedPatients.Add(p);

            var archived = await _service.GetArchivedPatientsAsync();
            ArchivedPatients.Clear();
            foreach (var p in archived.OrderBy(p => p.HoldTime))
                ArchivedPatients.Add(p);

            BuildRooms();
        }

        private void Rooms_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            BuildRooms();
        }

        private void BuildRooms()
        {
            RoomsWithAll.Clear();
            foreach (var r in Rooms)
                RoomsWithAll.Add(r);
            RoomsWithAll.Add("Toutes");

            NotArchivedPivot.Items.Clear();
            ArchivedPivot.Items.Clear();
            foreach (var room in RoomsWithAll)
            {
                var list1 = new ListView { SelectionMode = ListViewSelectionMode.None };
                if (Resources["PatientTemplateSelector"] is DataTemplateSelector selector1)
                    list1.ItemTemplateSelector = selector1;
                list1.ItemsSource = GetPatientsForRoom(NonArchivedPatients, room).ToList();
                var item1 = new PivotItem { Header = room, Content = list1 };
                NotArchivedPivot.Items.Add(item1);

                var list2 = new ListView { SelectionMode = ListViewSelectionMode.None };
                if (Resources["ArchivedPatientTemplateSelector"] is DataTemplateSelector selector2)
                    list2.ItemTemplateSelector = selector2;
                list2.ItemsSource = GetPatientsForRoom(ArchivedPatients, room).ToList();
                var item2 = new PivotItem { Header = room, Content = list2 };
                ArchivedPivot.Items.Add(item2);
            }
        }

        private IEnumerable<Patient> GetPatientsForRoom(IEnumerable<Patient> source, string room)
        {
            IEnumerable<Patient> query = room == "Toutes" ? source : source.Where(p => p.Position == room);
            return query.OrderByDescending(p => p.IsTaken).ThenBy(p => p.HoldTime);
        }

        private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender == NotArchivedPivot && ArchivedPivot.SelectedIndex != NotArchivedPivot.SelectedIndex)
                ArchivedPivot.SelectedIndex = NotArchivedPivot.SelectedIndex;
            else if (sender == ArchivedPivot && NotArchivedPivot.SelectedIndex != ArchivedPivot.SelectedIndex)
                NotArchivedPivot.SelectedIndex = ArchivedPivot.SelectedIndex;
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadPatientsAsync();
        }
        private async void UnarchivePatient_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                patient.IsArchived = false;
                ArchivedPatients.Remove(patient);
                NonArchivedPatients.Add(patient);
                BuildRooms();
                await _service.UnarchivePatientAsync(patient.Id);
            }
        }

        private async void DeletePatient_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuFlyoutItem)?.Tag is Patient patient)
            {
                var dialog = new ContentDialog
                {
                    Title = "Confirmation",
                    Content = "Supprimer ce patient ?",
                    PrimaryButtonText = "Oui",
                    CloseButtonText = "Non",
                    XamlRoot = (this.Content as FrameworkElement)?.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ArchivedPatients.Remove(patient);
                    NonArchivedPatients.Remove(patient);
                    BuildRooms();
                    await _service.RemovePatientAsync(patient.Id);
                }
            }
        }
    }
}
