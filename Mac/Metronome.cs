using System.Diagnostics;
using System.Text.Json;
using AppKit;
using CoreGraphics;
using Foundation;

namespace TD12KitManager.Mac;

/// <summary>Tempo, time signature and volume for REAPER's click, with start/stop and tap tempo (same settings file as Windows).</summary>
sealed class MetronomeControl
{
    sealed class Settings
    {
        public int Bpm { get; set; } = 100;
        public string TimeSignature { get; set; } = "4/4";
        public int Volume { get; set; } = 40;
    }

    static readonly string SettingsFile = Path.Combine(KitStore.AppDir, "metronome.json");
    static readonly string[] TimeSignatures = ["2/4", "3/4", "4/4", "5/4", "6/8", "7/8", "9/8", "12/8"];

    readonly NumberField bpm;
    readonly ChoiceBox<string> timeSig = new("Time signature", 80);
    readonly NumberField volume;
    readonly NSButton startStop = new(new CGRect(0, 0, 190, 28)) { Title = "Start metronome (F5)", BezelStyle = NSBezelStyle.Rounded };
    readonly NSButton tap = new(new CGRect(0, 0, 110, 28)) { Title = "Tap tempo", BezelStyle = NSBezelStyle.Rounded };
    readonly Func<string, string[], Task<bool>> send;
    readonly List<long> taps = [];
    NSTimer? liveUpdate;
    bool running;

    public NSView View { get; }
    public NSButton StartStopButton => startStop;

    /// <param name="send">Sends a request to REAPER; returns true when REAPER accepted it.</param>
    public MetronomeControl(Func<string, string[], Task<bool>> send)
    {
        this.send = send;
        var s = Load();
        bpm = new NumberField("Metronome tempo, beats per minute", 30, 300, 1, s.Bpm);
        volume = new NumberField("Click volume, percent", 0, 100, 5, s.Volume, 60);
        foreach (var t in TimeSignatures) timeSig.Add(t, t);
        int sigIndex = Array.IndexOf(TimeSignatures, s.TimeSignature);
        timeSig.SelectItem(sigIndex >= 0 ? sigIndex : Array.IndexOf(TimeSignatures, "4/4"));

        Announcer.Label(startStop, "Start metronome");
        Announcer.Label(tap, "Tap tempo");
        View = Line(Caption("Metronome tempo:"), bpm, Caption("Time signature:"), timeSig,
            Caption("Click volume (%):"), volume, startStop, tap);

        startStop.Activated += async (_, _) => await Toggle();
        tap.Activated += (_, _) => Tap();
        // While the click is running, tempo/signature/volume changes are sent to REAPER after a short pause.
        bpm.ValueChanged += Changed;
        volume.ValueChanged += Changed;
        timeSig.Activated += (_, _) => Changed();
    }

    static NSTextField Caption(string text) => new(new CGRect(0, 0, 10, 18))
    {
        StringValue = text, Editable = false, Bordered = false, DrawsBackground = false, Selectable = false,
    };

    static NSStackView Line(params NSView[] views)
    {
        var line = NSStackView.FromViews(views);
        line.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        line.Spacing = 8;
        return line;
    }

    public async Task Toggle()
    {
        if (await Send(!running))
        {
            running = !running;
            startStop.Title = running ? "Stop metronome (F5)" : "Start metronome (F5)";
            Announcer.Label(startStop, running ? "Stop metronome" : "Start metronome");
        }
    }

    void Changed()
    {
        Save();
        if (!running) return;
        liveUpdate?.Invalidate();
        liveUpdate = NSTimer.CreateScheduledTimer(0.4, timer => { if (running) _ = Send(true); });
    }

    /// <summary>Averages the gaps between recent presses; a pause over 2 seconds starts a new count.</summary>
    public void Tap()
    {
        long now = Stopwatch.GetTimestamp();
        if (taps.Count > 0 && Stopwatch.GetElapsedTime(taps[^1], now).TotalSeconds > 2) taps.Clear();
        taps.Add(now);
        if (taps.Count > 8) taps.RemoveAt(0);
        if (taps.Count < 2)
        {
            Announcer.Say(tap, "Keep tapping");
            return;
        }
        double seconds = Stopwatch.GetElapsedTime(taps[0], now).TotalSeconds / (taps.Count - 1);
        int value = (int)Math.Round(Math.Clamp(60 / seconds, 30, 300));
        bpm.Value = value;
        Announcer.Say(tap, value.ToString());
    }

    Task<bool> Send(bool on)
    {
        var sig = (timeSig.Chosen ?? "4/4").Split('/');
        return send(on ? "Starting metronome..." : "Stopping metronome...",
            ["METRO", on ? "on" : "off", bpm.Value.ToString(), sig[0], sig[1], volume.Value.ToString()]);
    }

    static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new(); }
        catch (Exception) { return new(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(KitStore.AppDir);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Settings
            {
                Bpm = bpm.Value,
                TimeSignature = timeSig.Chosen ?? "4/4",
                Volume = volume.Value,
            }));
        }
        catch (IOException) { }
    }
}
