using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TD12KitManager;

/// <summary>
/// Finds installed EZdrummer sound libraries (core and expansions) and reads the drums from each
/// library's soundDB, a SQLite database Toontrack ships with every library.
/// </summary>
public static class LibraryScanner
{
    static readonly string[] DefaultHomes = OperatingSystem.IsMacOS()
        ? ["/Library/Application Support/EZDrummer", // where Product Manager puts the libraries on a Mac
           "/Library/Application Support/Toontrack/EZDrummer",
           Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Toontrack/EZDrummer")]
        : [@"C:\Program Files (x86)\Common Files\Toontrack\EZDrummer"];

    public sealed record Library(string Lib, string Name, List<Drum> Drums, HashSet<string> CymbalTypes);

    /// <summary>Folders EZdrummer installs sound libraries into.</summary>
    public static IEnumerable<string> LibraryFolders()
    {
        var homes = new List<string>();
        if (OperatingSystem.IsWindows()) homes.AddRange(RegistryHomes());
        homes.AddRange(DefaultHomes);
        // Extra folders (separated by ;) for testing with libraries installed elsewhere.
        if (Environment.GetEnvironmentVariable("TD12_EXTRA_LIBRARY_PATHS") is { Length: > 0 } extra)
            homes.AddRange(extra.Split(';', StringSplitOptions.RemoveEmptyEntries));
        return homes.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists);
    }

    /// <summary>EZdrummer records its library folder in the registry on Windows.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static IEnumerable<string> RegistryHomes()
    {
        var homes = new List<string>();
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Toontrack\Superior\EZDrummer");
                if (key?.GetValue("HomePath") is string p && p.Length > 0) homes.Add(p);
            }
            catch (Exception) { }
        }
        return homes;
    }

    public static List<Library> Scan()
    {
        var libraries = new List<Library>();
        foreach (var home in LibraryFolders())
        foreach (var dir in Directory.GetDirectories(home))
        {
            string db = Path.Combine(dir, "soundDB");
            if (!File.Exists(db)) continue;
            try
            {
                var lib = ReadLibrary(Path.GetFileName(dir), dir, db);
                if (lib.Drums.Count > 0 && !libraries.Any(l => l.Lib == lib.Lib)) libraries.Add(lib);
            }
            catch (Exception) { /* unreadable library: skip it, the rest still work */ }
        }
        return libraries;
    }

    static Library ReadLibrary(string folderName, string dir, string dbPath)
    {
        const string sql = """
            select l.LibID, e.Name, p.Name, d.Name, k.Name, s.Name
            from SOUNDENTITY e
            join LIBRARY l on l.ID = e.LibraryID
            join SDXPOSITIONS p on p.ID = e.SDXPositionID
            join SDXDRUMS d on d.ID = e.SDXDrumID
            left join KITPIECES k on k.ID = e.KitpieceID
            left join SUBKITPIECES s on s.ID = e.SubKitpieceID
            """;
        var drums = new List<Drum>();
        var cymbals = new HashSet<string>();
        string? libId = null;
        foreach (var row in Sqlite.Query(dbPath, sql, 6))
        {
            libId ??= row[0];
            string kitpiece = row[4] ?? "", sub = row[5] ?? "";
            // Toms and cymbals are told apart by their sub-type (rack/floor, crash/ride/splash...).
            string type = kitpiece is "Toms" or "Cymbal" && sub is not ("" or "Default") ? sub : kitpiece;
            if (kitpiece == "Cymbal") cymbals.Add(type);
            if (row[3] is { Length: > 0 } id && row[2] is { Length: > 0 } pos && type.Length > 0)
                drums.Add(new Drum(row[0] ?? folderName, pos, id, (row[1] ?? id).Replace("dampesd", "damped"), type));
        }
        return new Library(libId ?? folderName, ReadName(dir) ?? folderName, drums, cymbals);
    }

    /// <summary>The Name file holds e.g. "EZdrummer 3 Main Room" "EZdrummer 3 Core Library".</summary>
    static string? ReadName(string dir)
    {
        string file = Path.Combine(dir, "Name");
        if (!File.Exists(file)) return null;
        var m = Regex.Match(File.ReadAllText(file), "\"([^\"]+)\"");
        return m.Success ? Regex.Replace(m.Groups[1].Value, @"^EZdrummer 3 ", "") : null;
    }

    /// <summary>Minimal read-only access through the system's SQLite (winsqlite3.dll on Windows, libsqlite3 on a Mac).</summary>
    static class Sqlite
    {
        const string Lib = "sqlite3native";

        static Sqlite()
        {
            NativeLibrary.SetDllImportResolver(typeof(Sqlite).Assembly, (name, assembly, path) =>
                name != Lib ? IntPtr.Zero
                : NativeLibrary.Load(OperatingSystem.IsWindows() ? "winsqlite3"
                    : OperatingSystem.IsMacOS() ? "/usr/lib/libsqlite3.dylib" : "libsqlite3.so.0"));
        }

        const int OpenReadOnly = 0x1, OpenUri = 0x40, Row = 100;

        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        static extern int sqlite3_close(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = CallingConvention.Winapi)]
        static extern int sqlite3_finalize(IntPtr stmt);

        static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

        public static List<string?[]> Query(string path, string sql, int columns)
        {
            // immutable=1: the library folder is read-only, so skip locking and journal files.
            string uri = "file:" + Uri.EscapeDataString(path.Replace('\\', '/')).Replace("%2F", "/").Replace("%3A", ":") + "?immutable=1";
            if (sqlite3_open_v2(Z(uri), out var db, OpenReadOnly | OpenUri, IntPtr.Zero) != 0)
            {
                sqlite3_close(db);
                throw new IOException("Could not open " + path);
            }
            var rows = new List<string?[]>();
            try
            {
                if (sqlite3_prepare_v2(db, Z(sql), -1, out var stmt, IntPtr.Zero) != 0)
                    throw new IOException("Unexpected soundDB layout in " + path);
                try
                {
                    while (sqlite3_step(stmt) == Row)
                    {
                        var row = new string?[columns];
                        for (int i = 0; i < columns; i++) row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i));
                        rows.Add(row);
                    }
                }
                finally { sqlite3_finalize(stmt); }
            }
            finally { sqlite3_close(db); }
            return rows;
        }
    }
}
