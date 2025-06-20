namespace Client.Helpers
{
    public static class PatientStringHelper
    {
        public static void ExtractInfoFromInput(string input, out string title, out string lastName, out string firstName)
        {
            title = string.Empty;
            lastName = string.Empty;
            firstName = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return;

            var parts = input.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                lastName = parts[0];
            if (parts.Length > 1)
                firstName = parts[1];
        }
    }
}
