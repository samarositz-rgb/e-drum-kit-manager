using System.Diagnostics;
using System.Text.Json;

namespace TD12KitManager;

/// <summary>Tempo, time signature and volume for REAPER's click, with start/stop and tap tempo.</summary>
public sealed class MetronomePanel : RowPanel
{
    sealed class Settings
    {
        public int Bpm { get; set; } = 100;
        public string TimeSignature { get; set; } = "4/4";
        public int Volume { get; set; } = 40;
    }

    static readonly string SettingsFile = Path.Combine(KitStore.AppDir, "metronome.json");
    static readonly string[] TimeSignatures = ["2/4", "3/4", "4/4", "5/4", "6/8", "7/8", "9/8", "12/8"];

    readonly NumberBox bpm = new() { Minimum = 30, Maximum = 300, Width = 70, AccessibleName = "Tempo, beats per minute" };
    readonly ComboBox timeSig = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, AccessibleName = "Time signature" };
    readonly NumberBox volume = new() { Minimum = 0, Maximum = 100, Increment = 5, Width = 60, AccessibleName = "Click volume, percent" };
    readonly Button startStop = new() { Text = "Start metronome (F5)", AutoSize = true };
    readonly Button tap = new() { Text = "T&ap tempo", AutoSize = true };
    readonly Func<string, string[], Task<bool>> send;
    readonly List<long> taps = [];
    readonly System.Windows.Forms.Timer liveUpdate = new() { Interval = 400 };
    bool running;

    public Button StartStopButton => startStop;

    /// <param name="send">Sends a request to REAPER; returns true when REAPER accepted it.</param>
    public MetronomePanel(Func<string, string[], Task<bool>> send)
    {
        this.send = send;
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0, 4, 0, 8);

        var s = Load();
        bpm.Value = Math.Clamp(s.Bpm, 30, 300);
        timeSig.Items.AddRange(TimeSignatures);
        timeSig.SelectedItem = TimeSignatures.Contains(s.TimeSignature) ? s.TimeSignature : "4/4";
        volume.Value = Math.Clamp(s.Volume, 0, 100);

        Controls.Add(Caption("Metr&onome tempo:"));
        Controls.Add(bpm);
        Controls.Add(Caption("Time si&gnature:"));
        Controls.Add(timeSig);
        Controls.Add(Caption("Click &volume (%):"));
        Controls.Add(volume);
        Controls.Add(startStop);
        Controls.Add(tap);

        startStop.Click += async (_, _) => await Toggle();
        tap.Click += (_, _) => Tap();
        // While the click is running, tempo/signature/volume changes are sent to REAPER after a short pause.
        bpm.ValueChanged += (_, _) => Changed();
        timeSig.SelectedIndexChanged += (_, _) => Changed();
        volume.ValueChanged += (_, _) => Changed();
        liveUpdate.Tick += async (_, _) => { liveUpdate.Stop(); if (running) await Send(true); };
    }

    static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    public async Task Toggle()
    {
        if (await Send(!running))
        {
            running = !running;
            startStop.Text = running ? "Stop metronome (F5)" : "Start metronome (F5)";
        }
    }

    void Changed()
    {
        Save();
        if (running) { liveUpdate.Stop(); liveUpdate.Start(); }
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
            Announce("Keep tapping");
            return;
        }
        double seconds = Stopwatch.GetElapsedTime(taps[0], now).TotalSeconds / (taps.Count - 1);
        int value = (int)Math.Round(Math.Clamp(60 / seconds, 30, 300));
        bpm.Value = value;
        Announce(value.ToString());
    }

    void Announce(string text) => Speech.Say(tap, text, interrupt: true);

    Task<bool> Send(bool on)
    {
        var sig = ((string)timeSig.SelectedItem!).Split('/');
        return send(on ? "Starting metronome..." : "Stopping metronome...",
            ["METRO", on ? "on" : "off", ((int)bpm.Value).ToString(), sig[0], sig[1], ((int)volume.Value).ToString()]);
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
                Bpm = (int)bpm.Value,
                TimeSignature = (string)timeSig.SelectedItem!,
                Volume = (int)volume.Value,
            }));
        }
        catch (IOException) { }
    }
}
