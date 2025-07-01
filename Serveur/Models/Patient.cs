namespace ChatServeur
{
    public class Patient
    {
        public string Id { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string Exams { get; set; } = string.Empty;
        public string Eye { get; set; } = string.Empty;
        public string Annotation { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public DateTime HoldTime { get; set; }
        public DateTime? PickUpTime { get; set; }
        public TimeSpan TimeOrder { get; set; }
        public string Examinator { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public bool IsTaken { get; set; }

        public string ToggleExamLabel => IsTaken
            ? $"Annuler {Exams} de {FirstName} {LastName}"
            : $"Faire passer {Exams} de {FirstName} {LastName}";
    }
}