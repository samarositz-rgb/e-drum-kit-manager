using System.Reflection;

namespace TD12KitManager;

/// <summary>The Logic Pro Scripter version of the REAPER rim split and pad levels effects, filled in for one module.</summary>
public static class LogicScripter
{
    public static string Build(ModuleProfile p)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logic_scripter.js")!;
        string js = new StreamReader(s).ReadToEnd();
        int rim = p.UsesRimSplit && p.Notes.TryGetValue("snareRim", out int r) ? r : -1;
        int head = p.Notes.TryGetValue("snare", out int h) ? h : -1;
        var global = PadLevels.Load();
        var levels = p.Pads()
            .Where(pad => global.GetValueOrDefault(pad.Name, 100) != 100)
            .SelectMany(pad => pad.Notes.Where(n => n is >= 0 and < 128).Select(n => $"{n}: {global[pad.Name]}"))
            .Distinct();
        return js
            .Replace("var RIM = 40, HEAD = 38, CROSS = 37;", $"var RIM = {rim}, HEAD = {head}, CROSS = {ModuleProfile.CrossStickNote};")
            .Replace("var PAD_LEVELS = {};", "var PAD_LEVELS = {" + string.Join(", ", levels) + "};");
    }
}
