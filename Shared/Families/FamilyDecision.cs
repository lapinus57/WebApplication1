using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EyeChat.Families
{
    public enum FamilyDecisionKind { Separate = 1, Join = 2, Validate = 3 }

    /// <summary>
    /// Décision prise à la main sur la page d'administration, conservée d'un recalcul à l'autre.
    /// Séparer / regrouper : deux codes patient. Valider : la liste exacte des membres (<see cref="Members"/>).
    /// </summary>
    public sealed record FamilyDecision(FamilyDecisionKind Kind, int A, int B, string Members = "")
    {
        public static string Signature(IEnumerable<int> members)
            => string.Join(",", members.Distinct().OrderBy(x => x).Select(x => x.ToString(CultureInfo.InvariantCulture)));
    }
}
