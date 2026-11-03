using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace ChatServeur
{
    public static class PasswordHelper
    {
        // Anciens hachages « sel:hash » créés avec 10 000 itérations : toujours acceptés.
        private const int LegacyIterations = 10_000;
        // Nouveaux hachages « v2:itérations:sel:hash » (recommandation OWASP pour PBKDF2-SHA256).
        internal const int CurrentIterations = 210_000;
        private const int MaxIterations = 5_000_000;
        private const int SaltSize = 128 / 8;
        private const int HashSize = 256 / 8;

        public static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Derive(password, salt, CurrentIterations);
            return string.Create(CultureInfo.InvariantCulture,
                $"v2:{CurrentIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}");
        }

        public static bool VerifyPassword(string password, string hashed)
        {
            if (string.IsNullOrEmpty(hashed))
                return false;

            var parts = hashed.Split(':');
            int iterations;
            string saltText, hashText;
            if (parts.Length == 2)
            {
                iterations = LegacyIterations;
                saltText = parts[0];
                hashText = parts[1];
            }
            else if (parts.Length == 4 && parts[0] == "v2" &&
                     int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations) &&
                     iterations > 0 && iterations <= MaxIterations)
            {
                saltText = parts[2];
                hashText = parts[3];
            }
            else
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(saltText);
                byte[] expected = Convert.FromBase64String(hashText);
                if (salt.Length == 0 || expected.Length != HashSize)
                    return false;
                byte[] actual = Derive(password ?? string.Empty, salt, iterations);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
            => KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterations, HashSize);
    }
}
