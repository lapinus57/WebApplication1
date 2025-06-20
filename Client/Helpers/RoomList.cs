using System.Collections.ObjectModel;

namespace Client.Helpers
{
    public static class RoomList
    {
        public static ObservableCollection<string> Load()
        {
            return new ObservableCollection<string> { "Salle 1", "Salle 2" };
        }
    }
}
