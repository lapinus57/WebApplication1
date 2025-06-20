using System.Collections.ObjectModel;

namespace Client.Models
{
    public class ExamOption
    {
        public string Name { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;

        public static ObservableCollection<ExamOption> Load()
        {
            return new ObservableCollection<ExamOption>();
        }
    }
}
