using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.Models
{
    public class AppointmentSlotInfo
    {
        public DateTime SlotStart { get; set; }
        public int ExistingAppointments { get; set; }
        public IReadOnlyDictionary<int, int> ColorCounts { get; set; } = new Dictionary<int, int>();
        public bool UsesOverloadCapacity { get; set; }
        public bool RedRelaxationApplied { get; set; }

        public string DisplayDate => SlotStart.ToString("dddd dd/MM/yyyy HH:mm");

        public string Summary
        {
            get
            {
                if (ColorCounts.Count == 0)
                {
                    return "Aucun rendez-vous";
                }

                var parts = ColorCounts
                    .OrderBy(p => p.Key)
                    .Select(p => $"{GetColorName(p.Key)}: {p.Value}");
                return string.Join(", ", parts);
            }
        }

        private static string GetColorName(int value) => value switch
        {
            0 => "Noir",
            255 => "Rouge",
            33023 => "Orange",
            16711935 => "Rose",
            65280 => "Vert",
            _ => $"Couleur {value}"
        };
    }
}
