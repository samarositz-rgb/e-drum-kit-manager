using AppKit;
using Foundation;

namespace TD12KitManager.Mac;

static class Program
{
    static void Main(string[] args)
    {
        // Diagnostic: EDrumKitManager --dump-catalog out.txt lists every drum found in the installed libraries.
        if (args.Length == 2 && args[0] == "--dump-catalog")
        {
            var expansions = Catalog.LoadInstalledLibraries();
            var lines = new List<string> { "Expansions: " + string.Join(", ", expansions) };
            lines.AddRange(Catalog.Rooms.Select(r => "Room: " + r.Lib + " = " + r.Name));
            foreach (var slot in Catalog.Slots)
                lines.Add($"{slot.Label}: {Catalog.ForSlot(slot).Count()} drums");
            lines.AddRange(Catalog.Drums.Select(d => $"{d.Lib}\t{d.Pos}\t{d.Id}\t{d.Type}\t{d.DisplayName}"));
            File.WriteAllLines(args[1], lines);
            return;
        }
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new AppDelegate();
        NSApplication.Main(args);
    }
}

sealed class AppDelegate : NSApplicationDelegate
{
    MainWindowController? main;

    public override void DidFinishLaunching(NSNotification notification)
    {
        main = new MainWindowController();
        main.Show();
        NSApplication.SharedApplication.Activate();
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;
}
