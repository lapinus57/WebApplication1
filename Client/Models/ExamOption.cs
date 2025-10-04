using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
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
                    return JsonConvert.DeserializeObject<ObservableCollection<ExamOption>>(json)
                           ?? new ObservableCollection<ExamOption>();
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
                var json = JsonConvert.SerializeObject(options, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
            }
        }
    }
}
