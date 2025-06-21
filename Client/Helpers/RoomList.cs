using System;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;

namespace Client.Helpers
{
    public static class RoomList
    {
        public static readonly string FilePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "rooms.json");

        public static ObservableCollection<string> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonConvert.DeserializeObject<ObservableCollection<string>>(json)
                           ?? new ObservableCollection<string>();
                }
            }
            catch
            {
            }
            return new ObservableCollection<string>();
        }

        public static void Save(ObservableCollection<string> rooms)
        {
            try
            {
                var json = JsonConvert.SerializeObject(rooms, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
            }
        }
    }
}
