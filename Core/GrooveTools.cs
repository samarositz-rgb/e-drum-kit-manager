using System.Globalization;

namespace TD12KitManager;

/// <summary>A drum the matching and play-style tools care about.</summary>
public enum DrumClass { None, Kick, Snare, HiHat, Ride, Other }

/// <summary>
/// Groove tools that EZdrummer only offers on screen: find grooves by playing them (like Tap2Find),
/// string a song's parts together (like Song Creator) and change how a groove is played.
/// </summary>
public static class GrooveTools
{
    // EZdrummer's own note layout in its groove files (counted from the installed library).
    static DrumClass GrooveClass(int note) => note switch
    {
        35 or 36 => DrumClass.Kick,
        37 or 38 or 39 or 40 => DrumClass.Snare,
        >= 8 and <= 26 or >= 60 and <= 64 or 42 or 44 or 46 => DrumClass.HiHat,
        51 or 53 or 59 => DrumClass.Ride,
        _ => DrumClass.Other,
    };

    const int HatPedal = 21;
    const int RideBow = 51;
    const int HatClosed = 62;

    /// <summary>The drum a note from the player's module plays, from the module profile.</summary>
    public static DrumClass PadClass(ModuleProfile profile, int note)
    {
        foreach (var (zone, n) in profile.Notes)
            if (n == note)
                return zone switch
                {
                    "kick" => DrumClass.Kick,
                    "snare" or "snareRim" or "snareXstick" => DrumClass.Snare,
                    _ when zone.StartsWith("hh") => DrumClass.HiHat,
                    _ when zone.StartsWith("ride") => DrumClass.Ride,
                    _ => DrumClass.Other,
                };
        return note == ModuleProfile.CrossStickNote ? DrumClass.Snare : DrumClass.None;
    }

    // ---------- Find by playing ----------

    /// <summary>A groove's rhythm folded into one bar: which steps (16ths) usually have a kick, snare, hi-hat or ride.</summary>
    sealed record Rhythm(GrooveNode Node, string Title, double Bpm, int Steps, HashSet<int> Kick, HashSet<int> Snare, HashSet<int> Hats);

    static List<Rhythm>? rhythms;

    static List<Rhythm> Rhythms(List<GrooveNode> library)
    {
        if (rhythms != null) return rhythms;
        var list = new List<Rhythm>();
        void Walk(GrooveNode n, List<string> path)
        {
            if (n.File != null)
            {
                try
                {
                    var g = Grooves.ReadMidi(n.File);
                    int steps = g.Beats * 16 / g.BeatUnit;
                    if (steps < 4) return;
                    var pos = g.Notes.Select(x => ((int)Math.Round(x.Qn * 4), GrooveClass(x.Pitch))).ToList();
                    list.Add(new Rhythm(n, string.Join(", ", path.TakeLast(2).Append(n.Name.ToLowerInvariant())), g.Bpm, steps,
                        Fold(pos, DrumClass.Kick, steps), Fold(pos, DrumClass.Snare, steps),
                        Fold(pos, DrumClass.HiHat, steps).Union(Fold(pos, DrumClass.Ride, steps)).ToHashSet()));
                }
                catch (Exception) { }
                return;
            }
            path.Add(n.Name);
            foreach (var c in n.Children) Walk(c, path);
            path.RemoveAt(path.Count - 1);
        }
        foreach (var n in library) Walk(n, []);
        return rhythms = list;
    }

    /// <summary>Steps that have this drum in at least half of the bars.</summary>
    static HashSet<int> Fold(List<(int Step, DrumClass Class)> hits, DrumClass cls, int steps)
    {
        if (hits.Count == 0) return [];
        int bars = hits.Max(h => h.Step) / steps + 1;
        var counts = new int[steps];
        foreach (var bar in hits.Where(h => h.Class == cls).GroupBy(h => h.Step / steps))
            foreach (int s in bar.Select(h => h.Step % steps).Distinct()) counts[s]++;
        return Enumerable.Range(0, steps).Where(s => counts[s] * 2 >= Math.Max(1, bars)).ToHashSet();
    }

    static double Similar(HashSet<int> a, HashSet<int> b)
    {
        int union = a.Union(b).Count();
        return union == 0 ? 1 : a.Intersect(b).Count() / (double)union;
    }

    public sealed record Match(GrooveNode Node, string Title, double Bpm, int Percent);

    /// <summary>
    /// The grooves closest to what was played (time in seconds, drum). Each groove is compared at its own tempo, so a
    /// groove much faster or slower than the playing doesn't line up and scores low.
    /// </summary>
    public static List<Match> FindByPlaying(List<GrooveNode> library, List<(double Time, DrumClass Class)> played, int count = 12)
    {
        var hits = played.Where(h => h.Class is DrumClass.Kick or DrumClass.Snare or DrumClass.HiHat or DrumClass.Ride).OrderBy(h => h.Time).ToList();
        if (hits.Count < 4) return [];
        double t0 = hits[0].Time;
        bool cymbals = hits.Any(h => h.Class is DrumClass.HiHat or DrumClass.Ride);
        var scored = new List<(Rhythm R, double Score)>();
        foreach (var r in Rhythms(library))
        {
            double step = 60.0 / r.Bpm / 4;
            double err = 0;
            var pos = new List<(int, DrumClass)>();
            foreach (var h in hits)
            {
                double q = (h.Time - t0) / step;
                int idx = (int)Math.Round(q);
                err += Math.Abs(q - idx);
                pos.Add((idx, h.Class == DrumClass.Ride ? DrumClass.HiHat : h.Class));
            }
            err /= hits.Count; // 0 = every hit on a 16th, 0.5 = all between
            if (err > 0.3) continue;
            double score = 0.4 * Similar(Fold(pos, DrumClass.Kick, r.Steps), r.Kick)
                         + 0.4 * Similar(Fold(pos, DrumClass.Snare, r.Steps), r.Snare);
            score = cymbals ? score + 0.2 * Similar(Fold(pos, DrumClass.HiHat, r.Steps), r.Hats) : score / 0.8;
            score -= err;
            scored.Add((r, score));
        }
        return scored.OrderByDescending(s => s.Score)
            .GroupBy(s => s.R.Title[..s.R.Title.LastIndexOf(',')]) // best variation of each part
            .Select(g => g.First()).Take(count)
            .Select(s => new Match(s.R.Node, s.R.Title, s.R.Bpm, (int)Math.Round(Math.Clamp(s.Score, 0, 1) * 100)))
            .ToList();
    }

    // ---------- Play style ----------

    public enum Feel { Normal, HiHatToRide, RideToHiHat, HalfTime, DoubleTime }

    /// <summary>The groove played differently: cymbal moved, half or double time, softer or harder (percent).</summary>
    public static GrooveMidi Restyle(GrooveMidi g, Feel feel, int dynamics)
    {
        var notes = g.Notes.Select(n =>
        {
            int pitch = n.Pitch;
            if (feel == Feel.HiHatToRide && GrooveClass(pitch) == DrumClass.HiHat && pitch != HatPedal) pitch = RideBow;
            if (feel == Feel.RideToHiHat && GrooveClass(pitch) == DrumClass.Ride) pitch = HatClosed;
            int vel = Math.Clamp((int)Math.Round(n.Velocity * dynamics / 100.0), 1, 127);
            return (n.Qn, n.LenQn, pitch, vel);
        }).ToList();
        double length = g.LengthQn;
        if (feel == Feel.HalfTime)
        {
            notes = notes.Select(n => (n.Qn * 2, n.LenQn * 2, n.pitch, n.vel)).ToList();
            length *= 2;
        }
        else if (feel == Feel.DoubleTime)
        {
            var once = notes.Select(n => (n.Qn / 2, n.LenQn / 2, n.pitch, n.vel)).ToList();
            notes = once.Concat(once.Select(n => (n.Item1 + length / 2, n.Item2, n.pitch, n.vel))).ToList();
        }
        return g with { LengthQn = length, Notes = notes.Select(n => (n.Item1, n.Item2, n.pitch, n.vel)).ToList() };
    }

    // ---------- Whole song ----------

    public sealed record SongSection(string Name, double StartQn);

    /// <summary>
    /// A song's parts strung together like a real arrangement: intro, verse, pre-chorus, chorus, second verse,
    /// pre-chorus, chorus, bridge, last chorus, ending. Before each chorus the last bar is replaced by one of the song's fills.
    /// </summary>
    public static (GrooveMidi Midi, List<SongSection> Sections)? BuildSong(GrooveNode song)
    {
        GrooveNode? Part(params string[] names) => song.Children.FirstOrDefault(p =>
            names.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) && p.Children.Any(v => v.File != null));
        var intro = Part("Intro");
        var verse = Part("Verse");
        var verse2 = Part("Verse 2") ?? verse;
        var pre = Part("Pre Chorus", "Pre-Chorus");
        var chorus = Part("Chorus");
        var bridge = Part("Bridge");
        var ending = Part("Endings", "Ending");
        var fills = Part("Fills", "Fill");
        if (verse == null && chorus == null) return null;

        var order = new List<(GrooveNode? Part, int Variation, string Label, bool FillAfter)>
        {
            (intro, 0, "Intro", false), (verse, 0, "Verse", pre == null), (pre, 0, "Pre chorus", true), (chorus, 0, "Chorus", false),
            (verse2, 1, "Second verse", pre == null), (pre, 1, "Pre chorus", true), (chorus, 1, "Chorus", true),
            (bridge, 0, "Bridge", true), (chorus, 2, "Last chorus", false), (ending, 0, "Ending", false),
        };
        var notes = new List<(double, double, int, int)>();
        var sections = new List<SongSection>();
        double at = 0, bpm = 0;
        int beats = 4, unit = 4, fillIndex = 0;
        foreach (var (part, variation, label, fillAfter) in order)
        {
            if (part == null) continue;
            var files = part.Children.Where(v => v.File != null).ToList();
            var g = Grooves.ReadMidi(files[variation % files.Count].File!);
            if (bpm == 0) { bpm = g.Bpm; beats = g.Beats; unit = g.BeatUnit; }
            double bar = g.Beats * 4.0 / g.BeatUnit, length = g.LengthQn;
            var body = g.Notes;
            var fillFiles = fills?.Children.Where(v => v.File != null).ToList();
            GrooveMidi? fill = fillAfter && fillFiles is { Count: > 0 } ? Grooves.ReadMidi(fillFiles[fillIndex++ % fillFiles.Count].File!) : null;
            if (fill != null && fill.LengthQn < length)
            {
                double cut = length - fill.LengthQn;
                body = body.Where(n => n.Qn < cut - 0.01).ToList();
                body.AddRange(fill.Notes.Select(n => (n.Qn + cut, n.LenQn, n.Pitch, n.Velocity)));
            }
            sections.Add(new SongSection(label, at));
            notes.AddRange(body.Select(n => (n.Qn + at, n.LenQn, n.Pitch, n.Velocity)));
            at += Math.Ceiling(length / bar - 0.01) * bar;
        }
        return (new GrooveMidi(bpm, beats, unit, at, notes.OrderBy(n => n.Item1).ToList()), sections);
    }

    /// <summary>Reads the hits REAPER recorded: "seconds timestamp note velocity" per line.</summary>
    public static List<(double Time, int Note)> ReadTaps(string path)
    {
        var rows = File.ReadAllLines(path).Select(l => l.Split(' ')).Where(p => p.Length >= 4)
            .Select(p => (Time: double.Parse(p[0], CultureInfo.InvariantCulture), Ts: double.Parse(p[1], CultureInfo.InvariantCulture), Note: int.Parse(p[2])))
            .ToList();
        // REAPER only polls every few hundredths of a second; its event timestamps are finer, so use them when they track the clock.
        if (rows.Count >= 2)
        {
            double dt = rows[^1].Time - rows[0].Time, dts = rows[^1].Ts - rows[0].Ts;
            if (dt > 0.2 && dts > 0)
            {
                double scale = dts / dt;
                return rows.Select(r => (rows[0].Time + (r.Ts - rows[0].Ts) / scale, r.Note)).ToList();
            }
        }
        return rows.Select(r => (r.Time, r.Note)).ToList();
    }
}
