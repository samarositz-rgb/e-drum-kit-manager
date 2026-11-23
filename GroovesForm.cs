namespace TD12KitManager;

/// <summary>
/// Browse EZdrummer's grooves (style, feel, song, part, variation) and play one in REAPER with the loaded kit.
/// Enter plays the selected variation, Space stops, the tempo field changes the speed while it plays.
/// </summary>
sealed class GroovesForm : Form
{
    readonly TextBox findBox = new() { Width = 360, AccessibleName = "Find grooves, for example funk or shuffle 90" };
    readonly TreeView tree = new() { Width = 520, Height = 380, HideSelection = false, AccessibleName = "Grooves" };
    readonly NumberBox tempo = new() { Minimum = 30, Maximum = 300, Value = 120, Width = 70, AccessibleName = "Tempo, beats per minute" };
    readonly CheckBox loop = new() { Text = "&Loop", Checked = true, AutoSize = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    readonly System.Windows.Forms.Timer findDelay = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer tempoDelay = new() { Interval = 400 };
    readonly ComboBox feel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Play style" };
    readonly ComboBox dynamics = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, AccessibleName = "Dynamics" };
    readonly System.Windows.Forms.Timer sectionTimer = new() { Interval = 100 };
    readonly Func<string, string[], Task<(bool Ok, string Message)?>> askReaper;
    readonly ModuleProfile profile;
    List<GrooveTools.SongSection> sections = [];
    int nextSection;
    double songQn, songLengthQn;
    DateTime lastTick;
    List<GrooveNode> library = [];
    Button playButton;
    bool playing;

    /// <summary>One button: Play while stopped, Stop while a groove plays (Alt+P either way).</summary>
    void SetPlaying(bool on)
    {
        playing = on;
        playButton.Text = on ? "Sto&p" : "&Play";
    }
    int songBpm;

    public GroovesForm(Func<string, string[], Task<(bool Ok, string Message)?>> askReaper, ModuleProfile profile)
    {
        this.askReaper = askReaper;
        this.profile = profile;
        Text = "Grooves";
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var grid = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(10), Dock = DockStyle.Fill };
        grid.Controls.Add(new Label { Text = "&Find:", AutoSize = true });
        grid.Controls.Add(findBox);
        grid.Controls.Add(new Label { Text = "&Grooves (Enter plays, Space plays or stops):", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        grid.Controls.Add(tree);

        var controls = new RowPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        playButton = new Button { Text = "&Play", AutoSize = true };
        var songButton = new Button { Text = "Play &whole song", AutoSize = true };
        var findPlaying = new Button { Text = "Find &by playing...", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        controls.Controls.AddRange([
            new Label { Text = "&Tempo:", AutoSize = true, Margin = new Padding(3, 7, 2, 3) }, tempo, loop, playButton, songButton]);
        grid.Controls.Add(controls);
        feel.Items.AddRange(["Normal", "Hi-hat moved to ride", "Ride moved to hi-hat", "Half time", "Double time"]);
        feel.SelectedIndex = 0;
        dynamics.Items.AddRange(["Soft", "Normal", "Hard"]);
        dynamics.SelectedIndex = 1;
        var style = new RowPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        style.Controls.AddRange([
            new Label { Text = "St&yle:", AutoSize = true, Margin = new Padding(3, 7, 2, 3) }, feel,
            new Label { Text = "&Dynamics:", AutoSize = true, Margin = new Padding(8, 7, 2, 3) }, dynamics, findPlaying, close]);
        grid.Controls.Add(style);
        songButton.Click += async (_, _) => await PlaySong();
        findPlaying.Click += async (_, _) => await FindByPlaying();
        sectionTimer.Tick += (_, _) => SectionTick();
        grid.Controls.Add(status);
        Controls.Add(grid);
        CancelButton = close;
        close.Click += (_, _) => Close();

        playButton.Click += async (_, _) => await (playing ? Stop() : Play());
        tree.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Play(); }
            else if (e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; await (playing ? Stop() : Play()); }
        };
        tree.AfterSelect += (_, e) => Selected(e.Node);
        findBox.TextChanged += (_, _) => { findDelay.Stop(); findDelay.Start(); };
        findBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; findDelay.Stop(); Filter(); tree.Focus(); } };
        findDelay.Tick += (_, _) => { findDelay.Stop(); Filter(); };
        tempo.ValueChanged += (_, _) => { if (playing) { tempoDelay.Stop(); tempoDelay.Start(); } };
        tempoDelay.Tick += async (_, _) => { tempoDelay.Stop(); await askReaper("", ["GROOVETEMPO", ((int)tempo.Value).ToString()]); };
        Shown += (_, _) => Load();
        FormClosed += async (_, _) => { if (playing) await Stop(); };
    }

    new void Load()
    {
        try { library = Grooves.Load(); }
        catch (Exception ex) { Say("Could not read EZdrummer's grooves: " + ex.Message); }
        Fill(library, expand: false);
        int count = Count(library);
        Say(count == 0
            ? "No grooves found. EZdrummer's MIDI library may not be installed."
            : $"{count} grooves. Use the arrow keys to browse: Right opens a style, song or part. Enter plays a variation.");
        tree.Focus();
    }

    static int Count(IEnumerable<GrooveNode> nodes) => nodes.Sum(n => (n.File != null ? 1 : 0) + Count(n.Children));

    void Fill(List<GrooveNode> nodes, bool expand)
    {
        tree.BeginUpdate();
        tree.Nodes.Clear();
        foreach (var n in nodes) tree.Nodes.Add(Make(n, expand));
        tree.EndUpdate();
        if (tree.Nodes.Count > 0) tree.SelectedNode = tree.Nodes[0];
    }

    static TreeNode Make(GrooveNode n, bool expand)
    {
        var t = new TreeNode(n.Name) { Tag = n };
        foreach (var c in n.Children) t.Nodes.Add(Make(c, expand && c.File == null && c.Children.Any(x => x.Children.Count > 0)));
        if (expand) t.Expand();
        return t;
    }

    /// <summary>Keeps what matches every word typed; a number matches tempos within 5 BPM.</summary>
    void Filter()
    {
        var words = findBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) { Fill(library, expand: false); Say("Showing all grooves."); return; }
        var result = new List<GrooveNode>();
        foreach (var n in library) if (Match(n, "", words) is { } m) result.Add(m);
        Fill(result, expand: true);
        int count = Count(result);
        Say(count == 0 ? "No grooves match." : $"{count} grooves match.");
    }

    static GrooveNode? Match(GrooveNode n, string path, string[] words)
    {
        string here = path + " " + n.Name;
        bool all = words.All(w => int.TryParse(w, out int bpm)
            ? n.Bpm > 0 && Math.Abs(n.Bpm - bpm) <= 5
            : here.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (all) return n; // the whole branch
        var copy = new GrooveNode { Name = n.Name, File = n.File, Bpm = n.Bpm };
        foreach (var c in n.Children) if (Match(c, here, words) is { } m) copy.Children.Add(m);
        return copy.Children.Count > 0 ? copy : null;
    }

    void Selected(TreeNode? node)
    {
        if (node?.Tag is not GrooveNode g || g.Bpm <= 0 || g.Bpm == songBpm) return;
        songBpm = g.Bpm;
        if (!playing) tempo.Value = g.Bpm; // a new song starts at its own tempo
    }

    /// <summary>"Diva Ballad, Verse, variation 1".</summary>
    static string Title(TreeNode node)
    {
        if (!string.IsNullOrEmpty(node.ToolTipText)) return node.ToolTipText; // a "find by playing" result
        var parts = new List<string>();
        for (var n = node; n != null && parts.Count < 3; n = n.Parent) parts.Insert(0, n.Text);
        return string.Join(", ", parts).Replace(", Variation", ", variation");
    }

    GrooveMidi Styled(GrooveMidi midi) => GrooveTools.Restyle(midi, (GrooveTools.Feel)Math.Max(0, feel.SelectedIndex),
        dynamics.SelectedIndex switch { 0 => 75, 2 => 125, _ => 100 });

    string StyleWords => (feel.SelectedIndex > 0 ? ", " + feel.Text.ToLowerInvariant() : "")
        + (dynamics.SelectedIndex != 1 ? ", " + dynamics.Text.ToLowerInvariant() : "");

    async Task<bool> Send(GrooveMidi midi, bool repeat, string name)
    {
        var reply = await askReaper("", ["GROOVE", Grooves.WriteNotes(midi),
            midi.LengthQn.ToString(System.Globalization.CultureInfo.InvariantCulture), ((int)tempo.Value).ToString(),
            repeat ? "1" : "0", name]);
        if (reply is { Ok: true }) { SetPlaying(true); return true; }
        Say(reply == null ? "REAPER did not answer. Make sure REAPER is open." : "REAPER: " + reply.Value.Message);
        return false;
    }

    async Task Play()
    {
        var node = tree.SelectedNode;
        while (node != null && node.Tag is GrooveNode { File: null } && node.Nodes.Count > 0) node = node.Nodes[0]; // first variation
        if (node?.Tag is not GrooveNode { File: { } file })
        {
            Say("Choose a groove variation to play.");
            return;
        }
        GrooveMidi midi;
        try { midi = Styled(Grooves.ReadMidi(file)); }
        catch (Exception ex) { Say("Could not read that groove: " + ex.Message); return; }
        if (songBpm == 0) tempo.Value = (decimal)Math.Round(midi.Bpm);
        sectionTimer.Stop();
        string name = Title(node);
        if (await Send(midi, loop.Checked, name))
            Say($"Playing {name}{StyleWords}. {midi.Beats}/{midi.BeatUnit}, {midi.Bars} bar{(midi.Bars == 1 ? "" : "s")}, {(int)tempo.Value} BPM.");
    }

    /// <summary>The song the selection belongs to: the item whose children are parts that hold variations.</summary>
    static TreeNode? SongOf(TreeNode? node)
    {
        for (var n = node; n != null; n = n.Parent)
            if (n.Tag is GrooveNode g && g.Children.Count > 0 && g.Children.All(c => c.File == null)
                && g.Children.Any(c => c.Children.Any(v => v.File != null)))
                return n;
        return null;
    }

    /// <summary>The song's parts in order, like a real arrangement, with each section's name spoken as it starts.</summary>
    async Task PlaySong()
    {
        if (SongOf(tree.SelectedNode) is not { Tag: GrooveNode song } songNode)
        {
            Say("Choose a song, or a part of one, first. Songs have their tempo in the name.");
            return;
        }
        (GrooveMidi Midi, List<GrooveTools.SongSection> Sections)? built;
        try { built = GrooveTools.BuildSong(song); }
        catch (Exception ex) { Say("Could not build that song: " + ex.Message); return; }
        if (built is not { } b) { Say("That song has no verse or chorus to build from."); return; }
        if (song.Bpm > 0) tempo.Value = song.Bpm;
        var midi = Styled(b.Midi);
        double factor = feel.SelectedIndex == (int)GrooveTools.Feel.HalfTime ? 2 : feel.SelectedIndex == (int)GrooveTools.Feel.DoubleTime ? 0.5 : 1;
        sections = b.Sections.Select(x => x with { StartQn = x.StartQn * factor }).ToList();
        if (factor == 0.5) midi = midi with { LengthQn = b.Midi.LengthQn / 2, Notes = midi.Notes.Where(n => n.Qn < b.Midi.LengthQn / 2).ToList() };
        sectionTimer.Stop();
        string name = songNode.Text;
        if (!await Send(midi, false, name)) return;
        double total = midi.LengthQn * 60 / (double)tempo.Value;
        int minutes = (int)(total / 60), seconds = (int)(total % 60);
        Say($"Playing the whole song {name}{StyleWords}, {minutes} minute{(minutes == 1 ? "" : "s")} {seconds} seconds. {sections[0].Name}.");
        songQn = 0;
        songLengthQn = midi.LengthQn;
        nextSection = 1;
        lastTick = DateTime.Now;
        sectionTimer.Start();
    }

    /// <summary>Follows the song's position from the tempo and says each section as it begins.</summary>
    void SectionTick()
    {
        var now = DateTime.Now;
        songQn += (now - lastTick).TotalSeconds * (double)tempo.Value / 60;
        lastTick = now;
        if (nextSection < sections.Count && songQn >= sections[nextSection].StartQn)
            Say(sections[nextSection++].Name);
        if (songQn >= songLengthQn)
        {
            sectionTimer.Stop();
            _ = FinishSong();
        }
    }

    async Task FinishSong()
    {
        await Task.Delay(1500);
        if (!playing) return;
        SetPlaying(false);
        await askReaper("", ["GROOVESTOP"]);
        Say("End of the song.");
    }

    /// <summary>Records what the player plays on the pads and lists the grooves that match it best.</summary>
    async Task FindByPlaying()
    {
        if (playing) await Stop();
        if (library.Count == 0) { Say("No grooves to search."); return; }
        string path = Path.Combine(KitStore.BridgeDir, "tap.txt");
        try { File.Delete(path); } catch (IOException) { }
        string id = ReaperBridge.Send("TAPREC", path, "30");
        if (await ReaperBridge.WaitForReply(id + ":started", TimeSpan.FromSeconds(5)) == null)
        {
            Say("REAPER did not answer. Make sure REAPER is open, and restart it once after updating Kit Manager.");
            return;
        }
        Say("Play a groove on your pads now, two bars or more, then stop. Searching starts two seconds after your last hit.");
        var reply = await ReaperBridge.WaitForReply(id, TimeSpan.FromSeconds(40));
        if (reply is not { Ok: true }) { Say(reply?.Message ?? "Nothing was heard from your pads."); return; }
        Say("Searching.");
        var hits = GrooveTools.ReadTaps(path).Select(h => (h.Time, GrooveTools.PadClass(profile, h.Note))).ToList();
        var matches = await Task.Run(() => GrooveTools.FindByPlaying(library, hits));
        if (matches.Count == 0) { Say("No grooves matched. Try a steady pattern for a few bars, with kick and snare."); return; }
        tree.BeginUpdate();
        tree.Nodes.Clear();
        foreach (var m in matches)
            tree.Nodes.Add(new TreeNode($"{m.Title}, {Math.Round(m.Bpm)} BPM, {m.Percent} percent") { Tag = m.Node, ToolTipText = m.Title });
        tree.EndUpdate();
        tree.SelectedNode = tree.Nodes[0];
        tree.Focus();
        Say($"{matches.Count} matches. Best: {matches[0].Title}, {matches[0].Percent} percent. Press Enter to play. Clear the Find box to see all grooves again.");
    }

    async Task Stop()
    {
        sectionTimer.Stop();
        if (!playing) { Say("Nothing is playing."); return; }
        SetPlaying(false);
        var reply = await askReaper("", ["GROOVESTOP"]);
        Say(reply is { Ok: true } ? "Stopped." : "REAPER did not answer.");
    }

    void Say(string text)
    {
        status.Text = text;
        Speech.Say(status, text);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { _ = playing ? Stop() : Play(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
