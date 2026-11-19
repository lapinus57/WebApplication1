using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.Helpers
{
    /// <summary>
    /// Onglet de salle d'attente (RDC, 1er, …, Toutes) sélectionné dans les listes de patients.
    /// Classe sans WinUI pour pouvoir être vérifiée par les tests.
    /// </summary>
    public static class WaitingRoomTab
    {
        public const string SettingKey = "DefaultWaitingRoom";
        public const string AllRooms = "Toutes";

        /// <summary>
        /// Onglet à sélectionner après (re)construction des onglets : on garde celui qui était affiché,
        /// sinon la salle choisie dans les paramètres, sinon la première salle.
        /// </summary>
        public static int SelectIndex(IReadOnlyList<string> tabs, string? current, string? preferred)
        {
            int Find(string? name) => string.IsNullOrWhiteSpace(name) ? -1
                : tabs.ToList().FindIndex(t => string.Equals(t, name.Trim(), StringComparison.OrdinalIgnoreCase));
            var index = Find(current);
            if (index < 0) index = Find(preferred);
            return tabs.Count == 0 ? -1 : Math.Max(0, index);
        }
    }
}
