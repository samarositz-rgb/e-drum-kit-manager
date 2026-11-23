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
    readonly Func<string, string[], Task<(bool Ok, string Message)?>> askReaper;
    List<GrooveNode> library = [];
    bool playing;
    int songBpm;

    public GroovesForm(Func<string, string[], Task<(bool Ok, string Message)?>> askReaper)
    {
        this.askReaper = askReaper;
        Text = "Grooves";
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var grid = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(10), Dock = DockStyle.Fill };
        grid.Controls.Add(new Label { Text = "&Find:", AutoSize = true });
        grid.Controls.Add(findBox);
        grid.Controls.Add(new Label { Text = "&Grooves (Enter plays, Space stops):", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        grid.Controls.Add(tree);

        var controls = new RowPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        var play = new Button { Text = "&Play", AutoSize = true };
        var stop = new Button { Text = "St&op", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        controls.Controls.AddRange([
            new Label { Text = "&Tempo:", AutoSize = true, Margin = new Padding(3, 7, 2, 3) }, tempo, loop, play, stop, close]);
        grid.Controls.Add(controls);
        grid.Controls.Add(status);
        Controls.Add(grid);
        CancelButton = close;
        close.Click += (_, _) => Close();

        play.Click += async (_, _) => await Play();
        stop.Click += async (_, _) => await Stop();
        tree.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Play(); }
            else if (e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; await Stop(); }
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
        var parts = new List<string>();
        for (var n = node; n != null && parts.Count < 3; n = n.Parent) parts.Insert(0, n.Text);
        return string.Join(", ", parts).Replace(", Variation", ", variation");
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
        try { midi = Grooves.ReadMidi(file); }
        catch (Exception ex) { Say("Could not read that groove: " + ex.Message); return; }
        if (songBpm == 0) tempo.Value = (decimal)Math.Round(midi.Bpm);
        string name = Title(node);
        string notes = Grooves.WriteNotes(midi);
        var reply = await askReaper("", ["GROOVE", notes,
            midi.LengthQn.ToString(System.Globalization.CultureInfo.InvariantCulture), ((int)tempo.Value).ToString(),
            loop.Checked ? "1" : "0", name]);
        if (reply is { Ok: true })
        {
            playing = true;
            Say($"Playing {name}. {midi.Beats}/{midi.BeatUnit}, {midi.Bars} bar{(midi.Bars == 1 ? "" : "s")}, {(int)tempo.Value} BPM.");
        }
        else Say(reply == null ? "REAPER did not answer. Make sure REAPER is open." : "REAPER: " + reply.Value.Message);
    }

    async Task Stop()
    {
        if (!playing) { Say("Nothing is playing."); return; }
        playing = false;
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
