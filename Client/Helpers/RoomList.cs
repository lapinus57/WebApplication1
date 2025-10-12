using System;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;

namespace Client.Helpers
{
    public static class RoomList
    {
        public static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EyeChat", "rooms.json");

        public static ObservableCollection<string> Load()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonConvert.DeserializeObject<ObservableCollection<string>>(json)
                           ?? new ObservableCollection<string>();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("[RoomList] Load failed", ex, "CLI24");
            }
            return new ObservableCollection<string>();
        }

        public static void Save(ObservableCollection<string> rooms)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var json = JsonConvert.SerializeObject(rooms, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogException("[RoomList] Save failed", ex, "CLI25");
            }
        }
    }
}
