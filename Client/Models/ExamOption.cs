using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Client.Helpers;
using Newtonsoft.Json;

namespace Client.Models
{
    public class ExamOption : INotifyPropertyChanged
    {
        private int _index;
        private string _id = Guid.NewGuid().ToString();
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

        public string Id
        {
            get => _id;
            set
            {
                var sanitized = value?.Trim();
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    sanitized = Guid.NewGuid().ToString();
                }

                if (_id != sanitized)
                {
                    _id = sanitized;
                    OnPropertyChanged(nameof(Id));
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                var sanitized = value?.Trim() ?? string.Empty;
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
                var sanitized = value?.Trim() ?? string.Empty;
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
                var sanitized = value?.Trim() ?? string.Empty;

                if (_color != sanitized)
                {
                    _color = sanitized;
                    OnPropertyChanged(nameof(Color));
                    OnPropertyChanged(nameof(ForegroundColor));
                }
            }
        }

        public string ForegroundColor
        {
            get
            {
                var background = ColorUtils.FromHex(_color);
                var contrasting = ColorUtils.GetContrastingTextColor(background);
                return ColorUtils.ToHex(contrasting);
            }
        }


        public string CodeMSG
        {
            get => _codeMSG;
            set
            {
                var sanitized = value?.Trim() ?? string.Empty;
                if (_codeMSG != sanitized)
                {
                    _codeMSG = sanitized;
                    OnPropertyChanged(nameof(CodeMSG));
                }
            }
        }

        public string Annotation
        {
            get => _annotation;
            set
            {
                var sanitized = value ?? string.Empty;
                if (_annotation != sanitized)
                {
                    _annotation = sanitized;
                    OnPropertyChanged(nameof(Annotation));
                }
            }
        }

        public string EndAnnotation
        {
            get => _endAnnotation;
            set
            {
                var sanitized = value ?? string.Empty;
                if (_endAnnotation != sanitized)
                {
                    _endAnnotation = sanitized;
                    OnPropertyChanged(nameof(EndAnnotation));
                }
            }
        }

        public string Floor
        {
            get => _floor;
            set
            {
                var sanitized = value?.Trim() ?? string.Empty;
                if (_floor != sanitized)
                {
                    _floor = sanitized;
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

                    foreach (var option in items)
                    {
                        option?.Normalize();
                    }

                    return items;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("ExamOption.Load failed", ex);
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

                foreach (var option in sanitized)
                {
                    option.Normalize();
                }
                var json = JsonConvert.SerializeObject(sanitized, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogException("ExamOption.Save failed", ex);
            }
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                _id = Guid.NewGuid().ToString();
                OnPropertyChanged(nameof(Id));
            }

            Id = Id;
            Name = Name;
            Description = Description;
            CodeMSG = CodeMSG;
            Annotation = Annotation;
            EndAnnotation = EndAnnotation;
            Floor = Floor;
            Color = Color;

            if (string.IsNullOrWhiteSpace(Name))
            {
                // Certains environnements ne renseignent ni le nom ni la description. On essaye alors
                // de dériver un identifiant stable à partir des autres champs disponibles pour que les
                // ComboBox puissent toujours fournir un SelectedValue exploitable.
                var fallback = new[]
                {
                    Description,
                    CodeMSG,
                    Annotation,
                    EndAnnotation,
                    Floor
                }.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

                if (string.IsNullOrWhiteSpace(fallback))
                {
                    fallback = Index != 0 ? $"Examen {Index}" : "Examen";
                }

                Name = fallback;
            }

            if (string.IsNullOrWhiteSpace(Description))
            {
                Description = Name;
            }
        }

        public static ExamOption? FindByIdentifier(IEnumerable<ExamOption>? options, string? identifier)
        {
            if (options is null)
            {
                return null;
            }

            var normalized = NormalizeIdentifier(identifier);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            return options.FirstOrDefault(option =>
                       string.Equals(NormalizeIdentifier(option.Id), normalized, StringComparison.OrdinalIgnoreCase))
                   ?? options.FirstOrDefault(option =>
                       string.Equals(NormalizeIdentifier(option.Name), normalized, StringComparison.OrdinalIgnoreCase))
                   ?? options.FirstOrDefault(option =>
                       string.Equals(NormalizeIdentifier(option.Description), normalized, StringComparison.OrdinalIgnoreCase))
                   ?? options.FirstOrDefault(option =>
                       string.Equals(NormalizeIdentifier(option.CodeMSG), normalized, StringComparison.OrdinalIgnoreCase))
                   ?? options.FirstOrDefault(option =>
                       string.Equals(NormalizeIdentifier(option.Annotation), normalized, StringComparison.OrdinalIgnoreCase))
                   ?? options.FirstOrDefault(option =>
                       string.Equals(NormalizeIdentifier(option.EndAnnotation), normalized, StringComparison.OrdinalIgnoreCase))
                   ?? options.FirstOrDefault(option =>
                       string.Equals(NormalizeIdentifier(option.Floor), normalized, StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeIdentifier(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
