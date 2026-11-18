namespace TD12KitManager;

/// <summary>Keeps a copy of the e-drum map in EZdrummer's own preset folder, so the plugin recognises the map a kit names.</summary>
static class EdrumPresetFile
{
    public static void Ensure(string id, string body)
    {
        try
        {
            // EZdrummer keeps user presets in Application Support on a Mac and in Documents on Windows.
            string dir = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "Toontrack", "EZdrummer 3", "EdrumPresets")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toontrack", "EZdrummer 3", "EdrumPresets");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, id);
            string text = $"\"{id}\" {{\n{body}}}\n";
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
