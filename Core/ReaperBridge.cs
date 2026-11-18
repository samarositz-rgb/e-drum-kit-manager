using System.Reflection;

namespace TD12KitManager;

/// <summary>File-based link to the "EDrum Kit Bridge.lua" script running inside REAPER.</summary>
public static class ReaperBridge
{
    const string ScriptName = "EDrum Kit Bridge.lua";
    const string OldScriptName = "TD12 Kit Bridge.lua";
    const string StartupLine = "dofile(reaper.GetResourcePath() .. \"/Scripts/EDrum Kit Bridge.lua\") -- E-Drum Kit Manager";

    static readonly string RequestFile = Path.Combine(KitStore.BridgeDir, "request.txt");
    static readonly string StatusFile = Path.Combine(KitStore.BridgeDir, "status.txt");
    public static readonly string CaptureFile = Path.Combine(KitStore.BridgeDir, "capture.txt");

    /// <summary>Copies the bridge script into REAPER and makes it start with REAPER. Returns true if REAPER was not set up yet.</summary>
    public static bool Install()
    {
        string scripts = Path.Combine(KitStore.ReaperDir, "Scripts");
        if (!Directory.Exists(KitStore.ReaperDir)) return false;
        Directory.CreateDirectory(scripts);

        CopyResource("Bridge.lua", Path.Combine(scripts, ScriptName));
        CopyResource("snare_rim_split.jsfx", Path.Combine(KitStore.ReaperDir, "Effects", "EDrumKitManager", "snare_rim_split.jsfx"));
        CopyResource("pad_levels.jsfx", Path.Combine(KitStore.ReaperDir, "Effects", "EDrumKitManager", "pad_levels.jsfx"));
        // Older TD-12 Kit Manager effects stay in place for projects that use them; keep them current too.
        string oldFx = Path.Combine(KitStore.ReaperDir, "Effects", "TD12");
        if (Directory.Exists(oldFx))
        {
            CopyResource("snare_rim_split.jsfx", Path.Combine(oldFx, "snare_rim_split.jsfx"));
            CopyResource("pad_levels.jsfx", Path.Combine(oldFx, "td12_pad_levels.jsfx"));
        }

        string startup = Path.Combine(scripts, "__startup.lua");
        string existing = File.Exists(startup) ? File.ReadAllText(startup) : "";
        // Replace the TD-12 Kit Manager bridge (renamed), so only one bridge runs.
        if (existing.Contains(OldScriptName))
        {
            var kept = existing.Split('\n').Where(l => !l.Contains(OldScriptName)).ToList();
            File.WriteAllText(startup, string.Join("\n", kept).TrimEnd() + (kept.Any(l => l.Trim().Length > 0) ? "\n" : ""));
            existing = File.ReadAllText(startup);
            try { File.Delete(Path.Combine(scripts, OldScriptName)); } catch (IOException) { }
        }
        if (existing.Contains(ScriptName)) return false;
        File.AppendAllText(startup, (existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "") + StartupLine + "\n");
        return true;
    }

    /// <summary>
    /// Run by the uninstaller: stops the bridge starting with REAPER and removes the script. The two effects stay,
    /// because saved REAPER projects may use them.
    /// </summary>
    public static void Uninstall(string? reaperDir = null)
    {
        string scripts = Path.Combine(reaperDir ?? KitStore.ReaperDir, "Scripts");
        string startup = Path.Combine(scripts, "__startup.lua");
        if (File.Exists(startup))
        {
            var kept = File.ReadAllText(startup).Split('\n').Where(l => !l.Contains(ScriptName) && !l.Contains(OldScriptName)).ToList();
            string text = string.Join("\n", kept).TrimEnd();
            if (text.Trim().Length == 0) File.Delete(startup);
            else File.WriteAllText(startup, text + "\n");
        }
        foreach (var name in new[] { ScriptName, OldScriptName })
            try { File.Delete(Path.Combine(scripts, name)); } catch (IOException) { }
    }

    static void CopyResource(string resource, string target)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        using var r = new StreamReader(s);
        string text = r.ReadToEnd();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target) || File.ReadAllText(target) != text)
            File.WriteAllText(target, text);
    }

    public static string Send(params string[] lines)
    {
        Directory.CreateDirectory(KitStore.BridgeDir);
        string id = Guid.NewGuid().ToString("N")[..8];
        string tmp = RequestFile + ".tmp";
        File.WriteAllLines(tmp, [id, .. lines]);
        File.Move(tmp, RequestFile, true);
        return id;
    }

    public static (string Id, bool Ok, string Message)? ReadStatus()
    {
        try
        {
            if (!File.Exists(StatusFile)) return null;
            var parts = File.ReadAllText(StatusFile).Split('\t', 3);
            return parts.Length == 3 ? (parts[0], parts[1] == "OK", parts[2].Trim()) : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>Waits for REAPER's reply to request <paramref name="id"/>.</summary>
    public static async Task<(bool Ok, string Message)?> WaitForReply(string id, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(150);
            try
            {
                if (!File.Exists(StatusFile)) continue;
                var parts = File.ReadAllText(StatusFile).Split('\t', 3);
                if (parts.Length == 3 && parts[0] == id) return (parts[1] == "OK", parts[2].Trim());
            }
            catch (IOException) { }
        }
        // REAPER never picked it up; withdraw the request so it doesn't fire later by surprise.
        try { if (File.Exists(RequestFile) && File.ReadLines(RequestFile).FirstOrDefault() == id) File.Delete(RequestFile); }
        catch (IOException) { }
        return null;
    }
}
