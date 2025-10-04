namespace ChatServeur
{
    public class ExamOption
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string CodeMSG { get; set; } = string.Empty;
        public string ForegroundColor => ColorHelpers.GetForeground(Color);
        public string Annotation { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
    }
}

