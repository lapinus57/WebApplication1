namespace Client.Models
{
    public class SpeedMessageModel
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Destinataire { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Options { get; set; } = string.Empty;
    }
}
