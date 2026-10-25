using System.Diagnostics;
using System.Security.Cryptography;

namespace XdcConverter;

public sealed class PlayerForm : Form
{
    private readonly string[] files;
    private readonly Button monitorButton = new() { Text = "NTSC…", Dock = DockStyle.Fill, Enabled = false };
    private readonly ComboBox parts = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly PictureBox picture = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly TrackBar seek = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None, AutoSize = false, Height = 28, Enabled = false };
    private readonly Label time = new() { AutoSize = true, Anchor = AnchorStyles.Right, Text = "00:00:00.00" };
    private readonly Label note = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    private readonly TransportButton play = new(TransportIcon.Play) { Enabled = false };
    private readonly TransportButton pause = new(TransportIcon.Pause) { Enabled = false };
    private readonly TransportButton stop = new(TransportIcon.Stop) { Enabled = false };
    private readonly ToolTip tips = new();
    private readonly CheckBox mute = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private static int rememberedVolume = 75;
    private readonly TrackBar volume = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Value = rememberedVolume, TickStyle = TickStyle.None, AutoSize = false, Height = 27, SmallChange = 1, LargeChange = 10 };
    private readonly Label volumePercent = new() { AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = new();
    private readonly TaskCompletionSource ready = new();
    private XdvReader? reader;
    private WaveAudio? audio;
    private CancellationTokenSource? seeking;
    private int generation, baseFrame, nextAudio;
    private bool playing, updating, closed, loading, dragging;
    public int FrameNumber { get; private set; } = -1;
    public int RenderedFrames { get; private set; }
    public string LastFrameHash { get; private set; } = "";
    public bool IsPlaying => playing;
    public bool AudioActive => audio != null;
    public Task Ready => ready.Task;
    public bool ReferencesFiles(IEnumerable<string> paths) => files.Select(Path.GetFullPath).Intersect(paths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase).Any();

    public PlayerForm(IEnumerable<string> paths, string? smokeReport = null)
    {
        files = paths.ToArray();
        if (files.Length == 0) throw new ArgumentException("No video files.");
        Icon = AppBrand.Icon; Font = new Font("Segoe UI", 9);
        BackColor = MainForm.WindowColor;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 510); MinimumSize = new Size(450, 400);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parts.Items.AddRange(files.Select(Path.GetFileName).ToArray()!); parts.SelectedIndex = 0;
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        header.Controls.Add(parts, 0, 0); header.Controls.Add(monitorButton, 1, 0);
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(picture, 0, 1); layout.Controls.Add(seek, 0, 2);
        var actions = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 5, Margin = new Padding(0, 2, 0, 2) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 35));
        var transport = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
        transport.Controls.AddRange([play, pause, stop]);
        actions.Controls.Add(transport, 0, 0); actions.Controls.Add(mute, 2, 0); actions.Controls.Add(volume, 3, 0); actions.Controls.Add(volumePercent, 4, 0);
        layout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(time, 0, 4); layout.Controls.Add(note, 0, 5); Controls.Add(layout);
        monitorButton.Click += (_, _) => MonitorDialog.Show(this);
        MonitorSettings.Changed += MonitorChanged;
        play.Click += (_, _) => StartPlayback();
        pause.Click += (_, _) => PausePlayback();
        stop.Click += async (_, _) => await StopPlaybackAsync();
        mute.CheckedChanged += (_, _) => { if (playing) { PausePlayback(); StartPlayback(); } };
        volume.ValueChanged += (_, _) =>
        {
            rememberedVolume = volume.Value; volumePercent.Text = volume.Value + "%";
            try { if (audio != null) audio.Volume = volume.Value; }
            catch (Exception ex) { note.Text = ex.Message; }
        };
        seek.MouseDown += (_, _) => { dragging = true; PausePlayback(); };
        seek.MouseUp += async (_, _) => { dragging = false; await SeekToAsync(seek.Value); };
        seek.ValueChanged += async (_, _) => { if (!updating) { PausePlayback(); await SeekToAsync(seek.Value, dragging ? 80 : 0); } };
        parts.SelectedIndexChanged += async (_, _) => await LoadPartAsync();
        timer.Tick += (_, _) => TickPlayback();
        KeyPreview = true;
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Space && !mute.Focused && !parts.Focused && !play.Focused && !pause.Focused && !stop.Focused) { e.Handled = e.SuppressKeyPress = true; if (playing) PausePlayback(); else StartPlayback(); }
            else if (e.KeyCode is Keys.Left or Keys.Right && reader != null && !volume.Focused && !parts.Focused)
            {
                e.Handled = e.SuppressKeyPress = true; PausePlayback();
                await SeekToAsync(Math.Clamp(FrameNumber + (e.KeyCode == Keys.Left ? -1 : 1), 0, reader.Info.Packets - 1));
            }
        };
        Shown += async (_, _) =>
        {
            try { await LoadPartAsync(); } finally { ready.TrySetResult(); }
            if (smokeReport != null) await SmokeTest(smokeReport);
        };
        FormClosed += (_, _) =>
        {
            closed = true; generation++; seeking?.Cancel(); PausePlayback(); timer.Dispose();
            reader?.Dispose(); picture.Image?.Dispose(); tips.Dispose(); L.Changed -= Translate; MonitorSettings.Changed -= MonitorChanged;
        };
        L.Changed += Translate; Translate();
    }

    private void Translate()
    {
        Text = L.T("Просмотр XDV", "XDV preview") + " · " + (parts.SelectedItem?.ToString() ?? "");
        play.AccessibleName = L.T("Воспроизвести", "Play"); pause.AccessibleName = L.T("Пауза", "Pause"); stop.AccessibleName = L.T("Остановить", "Stop");
        tips.SetToolTip(play, play.AccessibleName); tips.SetToolTip(pause, pause.AccessibleName); tips.SetToolTip(stop, stop.AccessibleName);
        mute.Text = L.T("Без звука", "Mute");
        volume.AccessibleName = L.T("Громкость", "Volume"); tips.SetToolTip(volume, volume.AccessibleName);
        volumePercent.Text = volume.Value + "%";
        seek.AccessibleName = L.T("Позиция XDV", "XDV position");
        monitorButton.Enabled = reader?.Info.Mode == 1;
        monitorButton.Text = MonitorSettings.Current.Enabled ? "NTSC…" : L.T("Палитра…", "Palette…");
        note.Text = reader?.Info.Mode == 1 && MonitorSettings.Current.Enabled
            ? L.T("Кадры из XDV · 4:3 · CGA/NTSC. Реальный монитор может отличаться.", "Decoded XDV · 4:3 · CGA/NTSC. Real monitors can differ.")
            : L.T("Кадры из XDV · 4:3", "Decoded XDV frames · 4:3");
    }

    private void MonitorChanged()
    {
        if (closed) return; Translate();
        if (reader != null && FrameNumber >= 0)
        {
            try { ShowFrame(FrameNumber, reader.ReadFrame(FrameNumber)); }
            catch (Exception ex) { note.Text = ex.Message; }
        }
    }
    private async Task LoadPartAsync()
    {
        if (closed || parts.SelectedIndex < 0) return;
        PausePlayback(); seeking?.Cancel(); int version = ++generation; Translate();
        loading = true; seek.Enabled = false; UpdateTransport();
        var old = reader; reader = null; old?.Dispose();
        FrameNumber = -1;
        string path = files[parts.SelectedIndex];
        try
        {
            var opened = await Task.Run(() => new XdvReader(path));
            if (closed || generation != version) { opened.Dispose(); return; }
            reader = opened; loading = false; Translate();
            seek.Maximum = opened.Info.Packets - 1;
            seek.SmallChange = 1; seek.LargeChange = Math.Max(1, (int)opened.Info.Fps);
            await SeekToAsync(0);
            if (!closed && ReferenceEquals(reader, opened) && !loading) { seek.Enabled = true; UpdateTransport(); }
        }
        catch (Exception ex) { if (!closed && generation == version) { loading = false; note.Text = ex.Message; } }
    }

    public async Task SeekToAsync(int frame, int delay = 0)
    {
        if (closed || loading || reader == null) return;
        PausePlayback(); seeking?.Cancel();
        var cancellation = new CancellationTokenSource(); seeking = cancellation;
        UpdateTransport();
        int version = ++generation; var currentReader = reader;
        try
        {
            if (delay > 0) await Task.Delay(delay, cancellation.Token);
            var decoded = await Task.Run(() => currentReader.ReadFrame(frame, cancellation.Token), cancellation.Token);
            if (!closed && version == generation) ShowFrame(frame, decoded);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed && version == generation) note.Text = ex.Message; }
        finally
        {
            if (ReferenceEquals(seeking, cancellation)) { seeking = null; if (!closed) UpdateTransport(); }
            cancellation.Dispose();
        }
    }

    private void ShowFrame(int frame, byte[] decoded)
    {
        if (reader == null) return;
        var image = FrameImage.Create(decoded, reader.Info.Mode);
        picture.Image?.Dispose(); picture.Image = image;
        FrameNumber = frame; RenderedFrames++;
        LastFrameHash = Convert.ToHexString(SHA256.HashData(decoded));
        updating = true; seek.Value = frame; updating = false;
        time.Text = $"{L.Time(frame / reader.Info.Fps)} / {L.Time(reader.Info.Duration)}";
    }

    public void StartPlayback()
    {
        if (closed || loading || seeking != null || reader == null || playing) return;
        try
        {
            if (FrameNumber >= reader.Info.Packets - 1) ShowFrame(0, reader.ReadFrame(0));
            baseFrame = Math.Max(0, FrameNumber); nextAudio = baseFrame;
            if (!mute.Checked)
            {
                try { audio = new WaveAudio(reader.Info.SampleRate) { Volume = volume.Value }; FillAudio(); }
                catch (Exception ex) { audio?.Dispose(); audio = null; note.Text = ex.Message; }
            }
            clock.Restart(); playing = true; timer.Start(); UpdateTransport();
        }
        catch (Exception ex) { PausePlayback(); note.Text = ex.Message; }
    }

    private void FillAudio()
    {
        if (reader == null || audio == null) return;
        int capacity = Math.Clamp((int)Math.Ceiling(reader.Info.Fps / 2), 3, 32);
        while (nextAudio < reader.Info.Packets && audio.Pending < capacity) audio.Enqueue(reader.ReadAudio(nextAudio++));
    }
    private void TickPlayback()
    {
        if (!playing || reader == null) return;
        try
        {
            FillAudio();
            double elapsedFrames = audio == null ? clock.Elapsed.TotalSeconds * reader.Info.Fps : (double)audio.SamplesPlayed / reader.Info.AudioChunk;
            int target = baseFrame + (int)elapsedFrames;
            if (target >= reader.Info.Packets)
            {
                ShowFrame(reader.Info.Packets - 1, reader.ReadFrame(reader.Info.Packets - 1)); PausePlayback(); return;
            }
            if (target != FrameNumber) ShowFrame(target, reader.ReadFrame(target));
        }
        catch (Exception ex) { PausePlayback(); note.Text = ex.Message; }
    }
    public void PausePlayback()
    {
        timer.Stop(); clock.Stop(); playing = false;
        try { audio?.Dispose(); } finally { audio = null; }
        UpdateTransport();
    }

    private void UpdateTransport()
    {
        play.Enabled = pause.Enabled = stop.Enabled = !closed && !loading && seeking == null && reader != null;
        play.BackColor = playing ? Color.FromArgb(206, 229, 250) : Color.White;
        pause.BackColor = !playing && FrameNumber > 0 ? Color.FromArgb(206, 229, 250) : Color.White;
    }
    public async Task StopPlaybackAsync() { PausePlayback(); await SeekToAsync(0); }

    private async Task SmokeTest(string reportPath)
    {
        try
        {
            if (reader == null || FrameNumber != 0) throw new Exception("Player failed to load.");
            StartPlayback(); await Task.Delay(1100);
            bool clockAdvanced = FrameNumber >= (int)(reader.Info.Fps / 2);
            bool sound = AudioActive;
            volume.Value = 30;
            bool liveVolume = audio != null && Math.Abs(audio.EffectiveVolume - 30) <= 1;
            PausePlayback(); int pausedAt = FrameNumber; await Task.Delay(100);
            bool paused = FrameNumber == pausedAt;
            play.PerformClick(); await Task.Delay(120);
            stop.PerformClick(); await Task.Delay(180);
            bool stoppedAtStart = !IsPlaying && FrameNumber == 0 && seek.Value == 0 && !AudioActive;
            play.PerformClick(); await Task.Delay(150); pause.PerformClick();
            bool pauseButtonWorked = !IsPlaying && FrameNumber > 0;
            for (int i = 0; i < 8; i++)
            {
                await SeekToAsync((i * 173) % reader.Info.Packets);
                StartPlayback();
                liveVolume &= volume.Value == 30 && audio != null && Math.Abs(audio.EffectiveVolume - 30) <= 1;
                await Task.Delay(65); PausePlayback();
            }
            mute.Checked = true;
            await SeekToAsync(Math.Max(0, reader.Info.Packets - 5));
            StartPlayback(); await Task.Delay(650);
            bool ended = !IsPlaying && FrameNumber == reader.Info.Packets - 1;
            var report = new { Success = clockAdvanced && paused && ended && liveVolume && stoppedAtStart && pauseButtonWorked, ClockAdvanced = clockAdvanced, AudioActive = sound,
                LiveVolumeAndSeekPersistence = liveVolume,
                StopResetsToBeginning = stoppedAtStart, PauseButton = pauseButtonWorked, VolumeSliderWidth = volume.Width, TransportOnLeft = play.PointToScreen(Point.Empty).X < volume.PointToScreen(Point.Empty).X,
                PauseHeldFrame = paused, SeekAndResumeCycles = 8, ReachedEnd = ended, reader.Info.Mode, reader.Info.Packets, RenderedFrames };
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report, Data.Json));
        }
        catch (Exception ex) { File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }, Data.Json)); }
        finally { Close(); }
    }
}
