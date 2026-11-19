using System.Text.RegularExpressions;

namespace TD12KitManager;

/// <summary>
/// Builds ready-to-play kits from an expansion's drums: one kit per drum set (kick maker and model), with the toms
/// and snare of that model where the expansion has them, and one cymbal family per kit. Expansions number their
/// drums differently, so drums are matched by name, not by number.
/// </summary>
public static class ExpansionKits
{
    /// <summary>"16x22" Yamaha Recording Custom" -> "Yamaha Recording Custom"; drops sizes, "(toweled)" and similar.</summary>
    static string Model(string name)
    {
        string s = Regex.Replace(name, @"^[\d.,/x""' ]+(?=[A-Za-z])", "");
        s = Regex.Replace(s, @"\s*\(.*?\)\s*", " ");
        s = Regex.Replace(s, @"\s+-\s+.*$", "");
        return s.Trim();
    }

    // Words that name the cymbal type, not its family ("Paiste 2002 Medium Crash" -> "Paiste 2002").
    static readonly Regex CymbalWords = new(@"\b(thin|medium|heavy|dark|crash|ride|splash|china|hats?|hi-?hat|stack|bell|big beat|sound edge|swish)\b.*$", RegexOptions.IgnoreCase);
    static string Family(string name) => CymbalWords.Replace(Model(name), "").Trim();

    static bool IsCymbal(Drum d) => d.Type is "Hi-Hat" or "Ride" or "Crash" or "Splash" or "China" or "Spock";

    public static List<Kit> Build(string lib, string libName)
    {
        var drums = Catalog.Drums.Where(d => d.Lib == lib).ToList();
        var models = drums.Where(d => d.Type == "Kick").Select(d => Model(d.Name))
            .Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var families = drums.Where(IsCymbal).OrderBy(d => d.Type == "Hi-Hat" ? 0 : 1).Select(d => Family(d.Name))
            .Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string shortName = Regex.Replace(libName, @"\s+EZX$", "");
        var kits = new List<Kit>();
        var snaresUsed = new HashSet<Drum>();
        for (int k = 0; k < models.Count; k++)
        {
            string model = models[k];
            string family = families.Count > 0 ? families[k % families.Count] : "";
            var used = new HashSet<Drum>();
            var slots = new List<KitSlot>();
            foreach (var slot in Catalog.Slots)
            {
                var fits = Catalog.ForSlot(slot).ToList();
                var pick = fits.Where(d => d.Lib == lib && !used.Contains(d))
                    .OrderBy(d => IsCymbal(d)
                        ? (family.Length > 0 && Family(d.Name).Equals(family, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        : (Model(d.Name).Equals(model, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                    .ThenBy(d => d.Pos == Catalog.SlotPosition(slot.Index) ? 0 : 1)
                    .ThenBy(d => slot.Index == 1 && snaresUsed.Contains(d) ? 1 : 0) // spread the extra snares over the kits
                    .ThenBy(d => d.Id, StringComparer.Ordinal)
                    .FirstOrDefault()
                    // The expansion has nothing for this slot: borrow from the core library's Main Room.
                    ?? fits.Where(d => !used.Contains(d)).OrderBy(d => d.Lib == "EZX3_EZdrummer3_MainRoom" ? 0 : 1)
                        .ThenBy(d => d.Pos == Catalog.SlotPosition(slot.Index) ? 0 : 1).FirstOrDefault();
                if (pick == null) { slots.Add(new KitSlot()); continue; }
                used.Add(pick);
                if (slot.Index == 1) snaresUsed.Add(pick);
                slots.Add(new KitSlot { Lib = pick.Lib, Drum = pick.Id });
            }
            kits.Add(new Kit { Name = $"{model} ({shortName})", Source = libName, Slots = slots });
        }
        return kits;
    }
}
