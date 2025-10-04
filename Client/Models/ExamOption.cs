using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Client.Models
{
    public class ExamOption : INotifyPropertyChanged
    {
        private int _index;
        private string _name = string.Empty;
        private string _color = string.Empty;
        private string _codeMSG = string.Empty;
        private string _annotation = string.Empty;
        private string _endAnnotation = string.Empty;
        private string _floor = string.Empty;
        private string _description = string.Empty;

        public int Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    _index = value;
                    OnPropertyChanged(nameof(Index));
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                var sanitized = value ?? string.Empty;
                if (_name != sanitized)
                {
                    _name = sanitized;
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(DisplayLabel));
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                var sanitized = value ?? string.Empty;
                if (_description != sanitized)
                {
                    _description = sanitized;
                    OnPropertyChanged(nameof(Description));
                    OnPropertyChanged(nameof(DisplayLabel));
                }
            }
        }

        public string Color
        {
            get => _color;
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged(nameof(Color));
                    OnPropertyChanged(nameof(ForegroundColor));
                }
            }
        }
        public string ForegroundColor =>
            ColorUtils.ToHex(
                ColorUtils.GetContrastingTextColor(
                    ColorUtils.FromHex(_color)));


        public string CodeMSG
        {
            get => _codeMSG;
            set
            {
                if (_codeMSG != value)
                {
                    _codeMSG = value;
                    OnPropertyChanged(nameof(CodeMSG));
                }
            }
        }

        public string Annotation
        {
            get => _annotation;
            set
            {
                if (_annotation != value)
                {
                    _annotation = value;
                    OnPropertyChanged(nameof(Annotation));
                }
            }
        }

        public string EndAnnotation
        {
            get => _endAnnotation;
            set
            {
                if (_endAnnotation != value)
                {
                    _endAnnotation = value;
                    OnPropertyChanged(nameof(EndAnnotation));
                }
            }
        }

        public string Floor
        {
            get => _floor;
            set
            {
                if (_floor != value)
                {
                    _floor = value;
                    OnPropertyChanged(nameof(Floor));
                }
            }
        }

        [JsonIgnore]
        public string DisplayLabel => string.IsNullOrWhiteSpace(Description) ? Name : Description;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EyeChat",
            "exam_options.json");
        public static ObservableCollection<ExamOption> Load()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var items = JsonConvert.DeserializeObject<ObservableCollection<ExamOption>>(json)
                                ?? new ObservableCollection<ExamOption>();

                    // Les entrées nulles provoquent un plantage lors de l'évaluation des liaisons XAML.
                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        if (items[i] is null)
                        {
                            items.RemoveAt(i);
                        }
                    }

                    return items;
                }
            }
            catch
            {
            }
            return new ObservableCollection<ExamOption>();
        }

        public static void Save(ObservableCollection<ExamOption> options)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                var sanitized = new ObservableCollection<ExamOption>(
                    options
                        .Where(o => o is not null)
                        .Cast<ExamOption>());
                var json = JsonConvert.SerializeObject(sanitized, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
            }
        }
    }
}
