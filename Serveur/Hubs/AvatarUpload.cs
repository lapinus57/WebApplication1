namespace ChatServeur
{
    /// <summary>
    /// Valide un avatar envoyé par un client avant de l’écrire dans wwwroot/avatars.
    /// Le dossier est servi par le serveur web : seules de vraies images sont acceptées,
    /// et l’extension est déduite du contenu, jamais du nom envoyé.
    /// </summary>
    public static class AvatarUpload
    {
        // Le hub accepte des messages de 2 Mo ; une image en base64 est environ 4/3 plus grosse.
        public const int MaxBytes = 1_500_000;

        public static bool TryDecode(string? base64, out byte[] bytes, out string extension, out string error)
        {
            bytes = Array.Empty<byte>();
            extension = string.Empty;

            if (string.IsNullOrWhiteSpace(base64))
            {
                error = "Aucune image reçue.";
                return false;
            }

            var commaIndex = base64.IndexOf(',');
            if (commaIndex >= 0)
                base64 = base64[(commaIndex + 1)..];

            if (base64.Length > (MaxBytes + 2) / 3 * 4 + 4)
            {
                error = "L’image dépasse la taille autorisée (1,5 Mo).";
                return false;
            }

            try
            {
                bytes = Convert.FromBase64String(base64.Trim());
            }
            catch (FormatException)
            {
                error = "Le contenu de l’image est illisible.";
                return false;
            }

            if (bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                error = "L’image est vide ou dépasse la taille autorisée (1,5 Mo).";
                return false;
            }

            var detected = DetectExtension(bytes);
            if (detected is null)
            {
                error = "Format non pris en charge : utilisez une image PNG, JPEG, GIF, WebP ou BMP.";
                return false;
            }

            extension = detected;
            error = string.Empty;
            return true;
        }

        private static ReadOnlySpan<byte> PngSignature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static ReadOnlySpan<byte> JpegSignature => new byte[] { 0xFF, 0xD8, 0xFF };

        public static string? DetectExtension(ReadOnlySpan<byte> data)
        {
            if (data.StartsWith(PngSignature))
                return ".png";
            if (data.StartsWith(JpegSignature))
                return ".jpg";
            if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
                return ".gif";
            if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
                return ".webp";
            if (data.Length >= 26 && data.StartsWith("BM"u8))
                return ".bmp";
            return null;
        }
    }
}
