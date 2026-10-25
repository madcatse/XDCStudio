using System.Diagnostics;
using System.Text.Json;

namespace XdcConverter;

public sealed class MainForm : Form
{
    public static readonly Color WindowColor = Color.FromArgb(242, 245, 248);
    private readonly TextBox input = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox output = new() { Dock = DockStyle.Fill };
    private readonly ComboBox profile = Combo(["CGA Composite", "CGA RGB", "Tandy"]);
    private readonly ComboBox resolution = Combo(["160 × 200", "160 × 100"]);
    private readonly ComboBox fps = Combo(["15", "23.976", "24", "25", "29.97", "30", "50", "59.94", "60"], 5);
    private readonly ComboBox dither = Combo(["Bayer 8×8", "Bayer 4×4", "Blue noise", "None"]);
    private readonly ComboBox compositeColor = Combo(["NTSC · early CGA", "NTSC · late CGA", "old mode"]);
    private readonly ComboBox fit = Combo(["Fit", "Crop", "Stretch"]);
    private readonly ComboBox rate = Combo(["11025", "16000", "22050", "32000"], 2);
    private readonly ComboBox audio = Combo(["No audio"]);
    private readonly ComboBox language = Combo(["Русский", "English"]);
    private readonly NumericUpDown disk = Number(30, 500, 150);
    private readonly NumericUpDown integrity = Number(0, 100, 75);
    private readonly NumericUpDown start = Number(0, 1000000, 0, 2);
    private readonly NumericUpDown duration = Number(0, 1000000, 0, 2);
    private readonly NumericUpDown gamma = Number(0.30m, 3.00m, 1.00m, 2);
    private readonly NumericUpDown saturation = Number(0, 200, 100);
    private readonly NumericUpDown ditherStrength = Number(0, 100, 100);
    private readonly NumericUpDown temporalStability = Number(0, 100, 50);
    private readonly CheckBox comparison = new() { AutoSize = true, Checked = true, Dock = DockStyle.Fill };
    private readonly Button resetColor = MakeButton();
    private Control saturationControl = null!, strengthControl = null!, stabilityControl = null!;
    private readonly CheckBox deinterlace = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button monitorButton = MakeButton();
    private readonly Button chooseInput = MakeButton();
    private readonly Button chooseOutput = MakeButton();
    private readonly Button convert = MakeButton(true);
    private readonly Button cancel = MakeButton();
    private readonly Button folder = MakeButton();
    private readonly Button resultPreview = MakeButton();
    private readonly Button help = MakeButton();
    private readonly TrackBar timeline = new() { Dock = DockStyle.Fill, Maximum = 10000, TickStyle = TickStyle.None, AutoSize = false, Height = 27, Enabled = false };
    private readonly Label timeLabel = new() { AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 5, Margin = new Padding(0, 5, 0, 3) };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(60, 77, 92), Margin = new Padding(0, 3, 0, 4) };
    private readonly Label mediaLabel = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    private readonly Label modeLabel = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    private readonly Label previewLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label logLabel = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 3) };
    private readonly Label hint = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    private readonly ComparisonView picture = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 0) };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = WindowColor, ForeColor = Color.FromArgb(35, 48, 62), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9) };
    private readonly List<Control> settings = [];
    private readonly List<(Label Label, string Ru, string En)> labels = [];
    private readonly ToolTip tips = new();
    private MediaInfo? media;
    private CancellationTokenSource? operation, previewCancellation;
    private Task pendingPreview = Task.CompletedTask;
    private List<string> lastFiles = [];
    private string? lastOutput;
    private PlayerForm? player;
    private bool busy, suppress, closing, closed;
    private int previewGeneration, previewsCompleted;
    private double previewTime;
    private string statusRu = "Выберите видео для начала", statusEn = "Choose a video to begin";
    private readonly bool smoke;
    private Func<IReadOnlyList<string>, bool>? overwritePrompt;

    private static ComboBox Combo(string[] items, int selected = 0)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill };
        c.Items.AddRange(items); c.SelectedIndex = selected; return c;
    }
    private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals = 0) => new()
    { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Dock = DockStyle.Fill };
    private static Button MakeButton(bool primary = false) => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 31), Padding = new Padding(9, 3, 9, 3),
        FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(0, 119, 128) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(30, 53, 71), Cursor = Cursors.Hand
    };

    public MainForm(string? initialInput, string? smokeOutput = null)
    {
        smoke = smokeOutput != null;
        Icon = AppBrand.Icon; Text = "XDC Studio " + Converter.Version; Font = new Font("Segoe UI", 9);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = WindowColor; ForeColor = Color.FromArgb(27, 43, 59);
        ClientSize = new Size(990, 752); MinimumSize = new Size(950, 610);
        StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var left = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0, 0, 12, 0) };
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        void Row(string ru, string en, Control control)
        {
            var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 6, 3) };
            labels.Add((label, ru, en));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize)); fields.Controls.Add(label, 0, row);
            control.Margin = new Padding(0, 2, 0, 3); fields.Controls.Add(control, 1, row++); settings.Add(control);
        }
        void Wide(Control control)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Margin = new Padding(0, 3, 0, 4);
            fields.Controls.Add(control, 0, row++); fields.SetColumnSpan(control, 2);
        }
        static Control WithButton(Control field, Button button)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            button.AutoSize = false; button.MinimumSize = new Size(0, 26); button.Size = new Size(86, 26); button.Padding = new Padding(2, 0, 2, 0);
            button.AutoEllipsis = true;
            field.Anchor = AnchorStyles.Left | AnchorStyles.Right; field.Margin = new Padding(0, 1, 5, 0);
            button.Margin = Padding.Empty; panel.Controls.Add(field, 0, 0); panel.Controls.Add(button, 1, 0); return panel;
        }
        Row("Исходное видео", "Source video", WithButton(input, chooseInput));
        Row("Выходной XDV", "Output XDV", WithButton(output, chooseOutput)); Wide(mediaLabel);
        Row("Целевой профиль", "Target profile", profile); Row("Разрешение", "Resolution", resolution);
        Row("Цвет CGA", "CGA colour", compositeColor);
        Row("Кадров в секунду", "Frames per second", fps); Row("Дизеринг", "Dithering", dither);
        static Control Slider(NumericUpDown number, int factor)
        {
            var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            var track = new TrackBar { Minimum = (int)(number.Minimum * factor), Maximum = (int)(number.Maximum * factor),
                Value = (int)(number.Value * factor), TickStyle = TickStyle.None, AutoSize = false, Height = 24, Dock = DockStyle.Fill,
                Margin = Padding.Empty, SmallChange = factor == 100 ? 5 : 1, LargeChange = factor == 100 ? 10 : 10 };
            bool updating = false;
            track.ValueChanged += (_, _) => { if (!updating) number.Value = track.Value / (decimal)factor; };
            number.ValueChanged += (_, _) => { updating = true; track.Value = (int)(number.Value * factor); updating = false; };
            number.Margin = Padding.Empty; number.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(track, 0, 0); panel.Controls.Add(number, 1, 0); return panel;
        }
        gamma.Increment = 0.05m; saturation.Increment = ditherStrength.Increment = 5;
        Row("Гамма", "Gamma", Slider(gamma, 100));
        Row("Насыщенность, %", "Saturation, %", saturationControl = Slider(saturation, 1));
        Row("Сила дизеринга, %", "Dither strength, %", strengthControl = Slider(ditherStrength, 1));
        Row("Стабилизация, %", "Stability, %", stabilityControl = Slider(temporalStability, 1));
        resetColor.MinimumSize = new Size(0, 24); resetColor.Padding = new Padding(4, 0, 4, 0);
        resetColor.Anchor = AnchorStyles.Right; Wide(resetColor); settings.Add(resetColor);
        Row("Пропорции", "Aspect ratio", fit);
        Row("Поток, КиБ/с", "Data rate, KiB/s", disk); Row("Целостность, %", "Frame integrity, %", integrity);
        Row("Звуковая дорожка", "Audio track", audio); Row("Звук, Гц", "Audio rate, Hz", rate);
        Row("Начало, секунд", "Start, seconds", start); Row("Длина, секунд", "Length, seconds", duration);
        Wide(hint); Wide(deinterlace); settings.Add(deinterlace);
        left.Controls.Add(fields); columns.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        var previewHeader = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        monitorButton.MinimumSize = new Size(0, 24); monitorButton.Padding = new Padding(4, 0, 4, 0);
        previewHeader.Controls.Add(previewLabel, 0, 0); previewHeader.Controls.Add(monitorButton, 1, 0); previewHeader.Controls.Add(language, 2, 0);
        right.Controls.Add(previewHeader, 0, 0); right.Controls.Add(comparison, 0, 1); right.Controls.Add(picture, 0, 2); right.Controls.Add(timeline, 0, 3);
        right.Controls.Add(timeLabel, 0, 4); right.Controls.Add(modeLabel, 0, 5); right.Controls.Add(logLabel, 0, 6); right.Controls.Add(log, 0, 7);
        columns.Controls.Add(right, 1, 0); root.Controls.Add(columns, 0, 0);
        var footer = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 10)); footer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.Controls.Add(progress, 0, 0); footer.Controls.Add(status, 0, 1);
        var actions = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty };
        actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int actionColumn = 0;
        foreach (var button in new[] { convert, cancel, folder, resultPreview })
        {
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            button.Margin = new Padding(0, 0, 6, 3); button.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            actions.Controls.Add(button, actionColumn++, 0);
        }
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        help.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; help.Margin = new Padding(8, 0, 0, 3);
        actions.Controls.Add(help, 5, 0);
        footer.Controls.Add(actions, 0, 2); root.Controls.Add(footer, 0, 1); Controls.Add(root);

        help.Click += (_, _) => OpenHelp();
        monitorButton.Click += (_, _) => MonitorDialog.Show(this);
        MonitorSettings.Changed += MonitorChanged;
        chooseInput.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = L.T("Видео", "Video") + "|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.mpg;*.mpeg;*.wmv|" + L.T("Все файлы", "All files") + "|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK) await LoadVideo(dialog.FileName);
        };
        chooseOutput.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "XDC Video|*.XDV", FileName = "MOVIE.XDV", OverwritePrompt = false };
            if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.FileName;
        };
        convert.Click += async (_, _) => await ConvertVideo();
        cancel.Click += (_, _) => { operation?.Cancel(); SetStatus("Останавливаю…", "Stopping…"); };
        folder.Click += (_, _) => { if (lastOutput != null) Process.Start(new ProcessStartInfo("explorer.exe", lastOutput) { UseShellExecute = true }); };
        resultPreview.Click += (_, _) => OpenPlayer();
        language.SelectedIndexChanged += (_, _) => { if (!suppress) L.Set(language.SelectedIndex == 1, !smoke); };
        profile.SelectedIndexChanged += (_, _) => { UpdateProfile(); SchedulePreview(); };
        compositeColor.SelectedIndexChanged += (_, _) =>
        {
            if (suppress) return;
            if (profile.SelectedIndex == 0 && compositeColor.SelectedIndex < 2)
                MonitorSettings.Set(new MonitorOptions(NewCga: compositeColor.SelectedIndex == 1), !smoke);
            SchedulePreview();
        };
        foreach (var control in new[] { resolution, fps, dither, fit, rate, audio }) control.SelectedIndexChanged += (_, _) => SchedulePreview();
        foreach (var control in new[] { disk, integrity, duration, gamma, saturation, ditherStrength, temporalStability }) control.ValueChanged += (_, _) => SchedulePreview();
        dither.SelectedIndexChanged += (_, _) => UpdateProfile();
        compositeColor.SelectedIndexChanged += (_, _) => UpdateProfile();
        temporalStability.ValueChanged += (_, _) => UpdateProfile();
        comparison.CheckedChanged += (_, _) => picture.Compare = comparison.Checked;
        resetColor.Click += (_, _) =>
        {
            suppress = true; gamma.Value = 1; saturation.Value = 100; ditherStrength.Value = 100; suppress = false; SchedulePreview();
        };
        deinterlace.CheckedChanged += (_, _) => SchedulePreview();
        start.ValueChanged += (_, _) =>
        {
            if (!suppress && media != null) SetTimeline((double)start.Value);
            SchedulePreview();
        };
        timeline.ValueChanged += (_, _) => { if (!suppress) { UpdateTime(); SchedulePreview(); } };
        AllowDrop = true;
        DragEnter += (_, e) => { if (!busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += async (_, e) =>
        {
            if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (Path.GetExtension(files[0]).Equals(".xdv", StringComparison.OrdinalIgnoreCase)) new PlayerForm(files).Show(this);
                else await LoadVideo(files[0]);
            }
        };
        FormClosing += (_, e) => { StopPreview(); if (busy) { closing = true; e.Cancel = true; operation?.Cancel(); } };
        FormClosed += (_, _) => { closed = true; StopPreview(); picture.Frames = null; tips.Dispose(); L.Changed -= Translate; MonitorSettings.Changed -= MonitorChanged; };
        Shown += async (_, _) =>
        {
            if (initialInput != null && File.Exists(initialInput)) await LoadVideo(initialInput);
            if (smokeOutput != null) await SmokeTest(smokeOutput);
        };
        L.Changed += Translate; Translate(); SetBusy(false);
    }

    private void Translate()
    {
        suppress = true;
        foreach (var item in labels) item.Label.Text = L.T(item.Ru, item.En);
        void Items(ComboBox combo, params string[] values)
        {
            int selected = combo.SelectedIndex; combo.Items.Clear(); combo.Items.AddRange(values); combo.SelectedIndex = Math.Clamp(selected, 0, values.Length - 1);
        }
        Items(profile, L.T("CGA Composite · 16 цветов", "CGA Composite · 16 colours"), L.T("CGA RGB · чёрно-белое", "CGA RGB · monochrome"), L.T("Tandy · 16 цветов", "Tandy · 16 colours"));
        Items(resolution, L.T("160 × 200 · полное", "160 × 200 · full"), L.T("160 × 100 · облегчённое", "160 × 100 · reduced"));
        Items(compositeColor, L.T("NTSC · ранняя CGA", "NTSC · early CGA"), L.T("NTSC · поздняя CGA", "NTSC · late CGA"), "old mode");
        compositeColor.AccessibleName = L.T("Подбор композитного цвета", "Composite colour selection");
        tips.SetToolTip(compositeColor, L.T("Модель для конвертации: оттенок 0, цветность 100%. Выбор NTSC также настраивает предпросмотр. Кнопка NTSC меняет только просмотр.", "Encoding target: hue 0, chroma 100%. Selecting NTSC also aligns the preview. The NTSC button changes viewing only."));
        Items(dither, "Bayer 8×8", "Bayer 4×4", L.T("Blue noise · неподвижный", "Blue noise · static"), L.T("Без дизеринга", "No dithering"));
        Items(fit, L.T("Вписать с полями", "Fit with borders"), L.T("Заполнить с обрезкой", "Fill and crop"), L.T("Растянуть", "Stretch"));
        if (audio.Items.Count > 0) { int selected = audio.SelectedIndex; audio.Items[0] = L.T("Без звука · тишина", "No audio · silence"); audio.SelectedIndex = selected; }
        language.SelectedIndex = L.English ? 1 : 0;
        language.AccessibleName = L.T("Язык интерфейса", "Interface language"); timeline.AccessibleName = L.T("Позиция исходного видео", "Source video position");
        chooseInput.Text = L.T("Выбрать", "Browse"); chooseOutput.Text = L.T("Сохранить", "Save as");
        convert.Text = L.T("Конвертировать в XDV", "Convert to XDV"); cancel.Text = L.T("Отмена", "Cancel");
        folder.Text = L.T("Открыть результат", "Open output folder"); resultPreview.Text = L.T("Просмотр XDV", "Play XDV");
        help.Text = L.T("Справка", "Help"); help.AccessibleName = help.Text;
        tips.SetToolTip(help, L.T("Открыть справку в браузере · F1 · работает без интернета", "Open help in your browser · F1 · available offline"));
        deinterlace.Text = L.T("Убрать чересстрочность", "Deinterlace"); previewLabel.Text = L.T("Предпросмотр", "Preview");
        comparison.Text = L.T("Сравнить с исходником · двигайте разделитель", "Compare with source · drag the divider");
        resetColor.Text = L.T("Сбросить настройки цвета", "Reset colour adjustments");
        gamma.AccessibleName = L.T("Гамма", "Gamma"); saturation.AccessibleName = L.T("Насыщенность", "Saturation");
        ditherStrength.AccessibleName = L.T("Сила дизеринга", "Dithering strength");
        temporalStability.AccessibleName = L.T("Стабилизация NTSC", "NTSC temporal stability");
        foreach (var number in new[] { gamma, saturation, ditherStrength, temporalStability })
            foreach (Control child in number.Parent!.Controls) if (child is TrackBar) child.AccessibleName = number.AccessibleName;
        picture.AccessibleName = L.T("Сравнение исходника и результата", "Source and result comparison"); picture.Invalidate();
        tips.SetToolTip(gamma, L.T("1,00 — без коррекции; больше 1 — светлее", "1.00 = unchanged; above 1 = brighter"));
        tips.SetToolTip(saturation, L.T("100% — исходная насыщенность; 0% — оттенки серого до палитры", "100% = original saturation; 0% = grayscale before palette mapping"));
        tips.SetToolTip(ditherStrength, L.T("0% — ближайший цвет; 100% — полное смешивание", "0% = nearest colour; 100% = full colour mixing"));
        tips.SetToolTip(temporalStability, L.T("0% — выключена. Удерживает близкие цвета и учитывает стоимость обновления 8088. Эффект виден в готовом XDV; предпросмотр показывает отдельный кадр без истории.", "0% = off. Retains similar colours and considers 8088 update costs. View the effect in the finished XDV; the still preview has no frame history."));
        tips.SetToolTip(picture, L.T("Слева исходник, справа результат. Перетаскивайте линию; стрелки ←/→ также работают.", "Source on the left, result on the right. Drag the line or use Left/Right keys."));
        logLabel.Text = L.T("Лог", "Log"); hint.Text = L.T("Длина 0 — до конца. Имя XDV: до 8 букв/цифр (A–Z, 0–9, _).", "Length 0 = to end. XDV name: up to 8 characters (A–Z, 0–9, _).");
        tips.SetToolTip(timeline, L.T("Выберите момент для предпросмотра", "Choose a time to preview"));
        tips.SetToolTip(resultPreview, L.T("Воспроизвести кадры и звук готового XDV", "Play the decoded frames and audio from the XDV"));
        status.Text = L.T(statusRu, statusEn); UpdateMediaLabel(); UpdateProfile(); UpdateTime();
        suppress = false;
    }
    private static string HelpFile => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "docs", L.English ? "help.en.html" : "help.ru.html"));
    private void OpenHelp()
    {
        try
        {
            if (!File.Exists(HelpFile)) throw new FileNotFoundException(L.T("Файл справки не найден. Распакуйте portable-архив целиком, вместе с папкой docs.", "Help file is missing. Extract the complete portable archive, including the docs folder."), HelpFile);
            Process.Start(new ProcessStartInfo(HelpFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("Не удалось открыть справку.\n", "Could not open help.\n") + ex.Message,
                L.T("Справка", "Help"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F1) { OpenHelp(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void UpdateMediaLabel() => mediaLabel.Text = media == null ? "MP4 · MKV · WebM · MOV · AVI" :
        $"{media.Width} × {media.Height} · {media.Fps:0.###} FPS · {L.Time(media.Duration)} · {L.T("звук", "audio")}: {media.Audio.Count}";
    private void MonitorChanged() { UpdateProfile(); SchedulePreview(); }
    private void UpdateProfile()
    {
        resolution.Enabled = !busy && profile.SelectedIndex == 0;
        compositeColor.Enabled = !busy && profile.SelectedIndex == 0;
        monitorButton.Enabled = profile.SelectedIndex == 0;
        monitorButton.Text = MonitorSettings.Current.Enabled ? "NTSC…" : L.T("Палитра…", "Palette…");
        tips.SetToolTip(monitorButton, L.T("Настройка композитного монитора. Только просмотр, без изменения XDV.", "Composite monitor settings. Preview only; does not change XDV."));
        saturationControl.Enabled = !busy && profile.SelectedIndex != 1;
        strengthControl.Enabled = !busy && dither.SelectedIndex != 3;
        stabilityControl.Enabled = !busy && profile.SelectedIndex == 0 && compositeColor.SelectedIndex != 2;
        modeLabel.Text = profile.SelectedIndex switch
        {
            0 => MonitorSettings.Current.Enabled ? L.T(MonitorSettings.Current.NewCga ? "Просмотр: поздняя CGA · NTSC" : "Просмотр: ранняя CGA · NTSC", MonitorSettings.Current.NewCga ? "Viewing: late CGA · NTSC" : "Viewing: early CGA · NTSC") : L.T("CGA · палитра XDC без NTSC", "CGA · XDC palette without NTSC"),
            1 => L.T("CGA RGB · 640 × 200 · чёрно-белое", "CGA RGB · 640 × 200 · monochrome"),
            _ => L.T("Tandy · 160 × 200 · 16 цветов", "Tandy · 160 × 200 · 16 colours")
        };
        if (profile.SelectedIndex == 0 && compositeColor.SelectedIndex != 2 && temporalStability.Value > 0)
            modeLabel.Text += L.T(" · стабилизация — в XDV", " · stabilization in XDV");
    }
    private void SetStatus(string ru, string en) { statusRu = ru; statusEn = en; status.Text = L.T(ru, en); }
    private void SetTimeline(double seconds)
    {
        if (media == null) return;
        bool previous = suppress; suppress = true;
        timeline.Value = (int)Math.Clamp(seconds / media.Duration * timeline.Maximum, 0, timeline.Maximum);
        suppress = previous; UpdateTime();
    }
    private double SelectedTime => media == null ? 0 : Math.Min(media.Duration * timeline.Value / timeline.Maximum, Math.Max(0, media.Duration - 1 / Math.Max(1, media.Fps)));
    private void UpdateTime() => timeLabel.Text = media == null ? "" : $"{L.Time(SelectedTime)} / {L.Time(media.Duration)}";
    private Job ReadJob() => new()
    {
        Input = input.Text, Output = output.Text, Mode = profile.SelectedIndex + 1, HalfHeight = resolution.SelectedIndex == 1,
        Fps = double.Parse(fps.Text, System.Globalization.CultureInfo.InvariantCulture), SampleRate = int.Parse(rate.Text),
        DiskRate = (int)disk.Value, Integrity = (int)integrity.Value, Fit = new[] { "fit", "crop", "stretch" }[fit.SelectedIndex],
        Gamma = (double)gamma.Value, Saturation = (double)saturation.Value / 100, DitherStrength = (int)ditherStrength.Value,
        CompositeAlgorithm = compositeColor.SelectedIndex == 2 ? "legacy" : "ntsc", CompositeNewCga = compositeColor.SelectedIndex == 1,
        TemporalStability = (int)temporalStability.Value,
        Dither = new[] { "bayer", "bayer4", "blue", "none" }[dither.SelectedIndex], Start = (double)start.Value, Duration = (double)duration.Value,
        Silent = audio.SelectedIndex == 0, AudioStream = audio.SelectedIndex > 0 ? media?.Audio[audio.SelectedIndex - 1].Index : null, Deinterlace = deinterlace.Checked
    };
    private void SetBusy(bool value)
    {
        busy = value;
        foreach (var control in settings) control.Enabled = !value;
        convert.Enabled = !value && media != null; cancel.Enabled = value;
        folder.Enabled = !value && lastOutput != null; resultPreview.Enabled = !value && lastFiles.Count > 0;
        timeline.Enabled = !value && media != null; UpdateProfile();
    }
    private void StopPreview() { previewGeneration++; previewCancellation?.Cancel(); }
    private void SchedulePreview()
    {
        if (suppress || busy || closed || media == null) return;
        StopPreview();
        var cancellation = new CancellationTokenSource(); previewCancellation = cancellation;
        pendingPreview = PreviewAsync(ReadJob(), SelectedTime, media.VideoIndex, previewGeneration, cancellation);
    }
    private async Task PreviewAsync(Job job, double time, int videoIndex, int version, CancellationTokenSource cancellation)
    {
        PreviewFrames? image = null;
        try
        {
            await Task.Delay(180, cancellation.Token);
            SetStatus("Готовлю предпросмотр…", "Preparing preview…");
            image = await Task.Run(() => Converter.Preview(job, time, cancellation.Token, videoIndex), cancellation.Token);
            if (version != previewGeneration || closed || busy) return;
            picture.Frames = image; image = null;
            previewsCompleted++; previewTime = time;
            SetStatus("Предпросмотр готов", "Preview ready");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == previewGeneration && !closed) { SetStatus("Не удалось обновить предпросмотр", "Could not update preview"); AppendLog(ex.Message); }
        }
        finally
        {
            image?.Dispose(); if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null; cancellation.Dispose();
        }
    }
    private async Task LoadVideo(string path)
    {
        if (busy) return;
        StopPreview(); SetBusy(true); operation = new(); SetStatus("Читаю параметры видео…", "Reading video information…");
        media = null; picture.Frames = null;
        try
        {
            media = await Processes.Probe(path, operation.Token);
            suppress = true; input.Text = Path.GetFullPath(path);
            output.Text = Path.Combine(Path.GetDirectoryName(input.Text)!, "MOVIE.XDV");
            start.Value = duration.Value = 0; timeline.Value = 0;
            audio.Items.Clear(); audio.Items.Add(L.T("Без звука · тишина", "No audio · silence"));
            foreach (var track in media.Audio) audio.Items.Add(track.Description);
            audio.SelectedIndex = media.Audio.Count > 0 ? 1 : 0; suppress = false;
            UpdateMediaLabel(); UpdateTime(); SetStatus("Видео готово", "Video ready");
        }
        catch (OperationCanceledException) { SetStatus("Отменено", "Cancelled"); }
        catch (Exception ex) { Error(ex); }
        finally { suppress = false; operation.Dispose(); operation = null; SetBusy(false); }
        if (closing) { Close(); return; }
        SchedulePreview(); await pendingPreview;
    }
    private async Task ConvertVideo()
    {
        if (busy || media == null) return;
        StopPreview(); SetBusy(true); operation = new(); log.Clear(); progress.Value = 0;
        try
        {
            var job = ReadJob();
            AppendLog(L.T("Конвертация началась", "Conversion started"));
            var callback = new Progress<ConversionProgress>(p =>
            {
                if (closed) return;
                progress.Value = p.Percent; status.Text = p.Message;
                if (p.Percent == 100) AppendLog(p.Message);
            });
            var result = await Task.Run(() => new Converter().Run(job, callback, operation.Token, ConfirmOverwriteAsync));
            lastOutput = result.OutputDirectory; lastFiles = result.Files.Select(f => f.File).ToList();
            AppendLog(string.Join(Environment.NewLine, lastFiles));
            SetStatus($"Готово: {result.Files.Count} XDV", $"Done: {result.Files.Count} XDV");
            if (!closing) OpenPlayer();
        }
        catch (OperationCanceledException) { SetStatus("Конвертация отменена", "Conversion cancelled"); AppendLog(status.Text); }
        catch (Exception ex) { Error(ex); }
        finally { operation.Dispose(); operation = null; SetBusy(false); if (closing) Close(); }
    }
    private void OpenPlayer()
    {
        if (lastFiles.Count == 0) return;
        player?.Close(); player = new PlayerForm(lastFiles); player.Show(this);
    }
    private async Task<bool> ConfirmOverwriteAsync(IReadOnlyList<string> paths)
    {
        return await this.InvokeAsync(() =>
        {
            if (closed || closing) return false;
            bool approved;
            if (overwritePrompt != null) approved = overwritePrompt(paths);
            else if (smoke) approved = false;
            else
            {
                string list = string.Join(Environment.NewLine, paths.Take(8));
                if (paths.Count > 8) list += Environment.NewLine + L.T($"…и ещё {paths.Count - 8}", $"…and {paths.Count - 8} more");
                string message = L.T("Файл уже существует. Перезаписать результат и связанные файлы?", "Output already exists. Replace it and its companion files?") +
                    Environment.NewLine + Environment.NewLine + list;
                approved = MessageBox.Show(this, message, L.T("Перезапись результата", "Replace existing output"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            if (approved)
            {
                foreach (var preview in OwnedForms.OfType<PlayerForm>().Where(p => p.ReferencesFiles(paths)).ToArray()) preview.Close();
            }
            return approved;
        });
    }
    private void AppendLog(string text) => log.AppendText(text + Environment.NewLine);
    private void Error(Exception ex)
    {
        SetStatus("Не удалось завершить операцию", "Could not complete the operation"); AppendLog(ex.Message);
        if (!smoke && !closing) MessageBox.Show(this, ex.Message, "XDC Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task SmokeTest(string outputPath)
    {
        try
        {
            L.Set(false, false); profile.SelectedIndex = 1; start.Value = 30; duration.Value = 3; output.Text = outputPath;
            await pendingPreview; int first = previewsCompleted;
            timeline.Value = 2500; await pendingPreview;
            bool seekWorked = previewsCompleted > first && Math.Abs(previewTime - SelectedTime) < 0.001;
            int afterSeek = previewsCompleted;
            profile.SelectedIndex = 2; dither.SelectedIndex = 3; fit.SelectedIndex = 2;
            await pendingPreview;
            bool autoWorked = previewsCompleted > afterSeek && ReadJob().Mode == 3;
            profile.SelectedIndex = 0; dither.SelectedIndex = 0; fit.SelectedIndex = 0; await pendingPreview;
            static string Hash(Bitmap image)
            {
                using var bytes = new MemoryStream(); image.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
            }
            string original = Hash(picture.Frames!.Source), neutral = Hash(picture.Frames.Result);
            compositeColor.SelectedIndex = 2; await pendingPreview;
            bool legacyColor = compositeColor.Text == "old mode" && ReadJob().CompositeAlgorithm == "legacy" && Hash(picture.Frames!.Result) != neutral;
            compositeColor.SelectedIndex = 1; await pendingPreview;
            bool lateColor = ReadJob().CompositeNewCga && MonitorSettings.Current.NewCga && Hash(picture.Frames!.Result) != neutral;
            compositeColor.SelectedIndex = 0; await pendingPreview;
            bool earlyColor = !ReadJob().CompositeNewCga && ReadJob().CompositeAlgorithm == "ntsc" && Hash(picture.Frames!.Result) == neutral;
            bool encodingSelection = legacyColor && lateColor && earlyColor && Hash(picture.Frames.Source) == original;
            dither.SelectedIndex = 1; await pendingPreview;
            bool bayer4Works = ReadJob().Dither == "bayer4" && Hash(picture.Frames!.Result) != neutral;
            dither.SelectedIndex = 2; await pendingPreview;
            bool blueWorks = ReadJob().Dither == "blue" && Hash(picture.Frames!.Result) != neutral;
            dither.SelectedIndex = 0; await pendingPreview;
            temporalStability.Value = 75; await pendingPreview;
            bool stabilityWorks = stabilityControl.Enabled && ReadJob().TemporalStability == 75 && Hash(picture.Frames!.Result) == neutral;
            temporalStability.Value = 50; await pendingPreview;
            gamma.Value = 1.35m; await pendingPreview;
            string gammaHash = Hash(picture.Frames!.Result);
            bool gammaWorks = gammaHash != neutral && original == Hash(picture.Frames.Source);
            saturation.Value = 140; await pendingPreview;
            string saturationHash = Hash(picture.Frames!.Result);
            bool saturationWorks = saturationHash != gammaHash && original == Hash(picture.Frames.Source);
            ditherStrength.Value = 40; await pendingPreview;
            bool strengthWorks = Hash(picture.Frames!.Result) != saturationHash && original == Hash(picture.Frames.Source);
            comparison.Checked = false; bool comparisonToggle = !picture.Compare;
            comparison.Checked = true; picture.Divider = 0.35;
            await SaveLayout(outputPath + ".comparison.png");
            resetColor.PerformClick(); await pendingPreview;
            bool resetWorks = Hash(picture.Frames!.Result) == neutral && gamma.Value == 1 && saturation.Value == 100 && ditherStrength.Value == 100;
            picture.Divider = 0.5;
            var monitorOriginal = MonitorSettings.Current;
            MonitorSettings.Set(monitorOriginal with { Enabled = false }, false); await pendingPreview;
            bool monitorSwitch = Hash(picture.Frames!.Result) != neutral && Hash(picture.Frames.Source) == original;
            MonitorSettings.Set(monitorOriginal with { NewCga = true, Hue = 15 }, false); await pendingPreview;
            bool monitorTune = Hash(picture.Frames!.Result) != neutral && Hash(picture.Frames.Source) == original;
            MonitorSettings.Set(monitorOriginal, false); await pendingPreview;
            bool monitorReset = Hash(picture.Frames!.Result) == neutral;
            L.Set(true, false);
            bool translated = convert.Text == "Convert to XDV" && logLabel.Text == "Log" && compositeColor.Items[2]?.ToString() == "old mode" && profile.SelectedIndex == 0;
            bool helpEnglish = help.Text == "Help" && HelpFile.EndsWith("help.en.html") && File.Exists(HelpFile);
            await SaveLayout(outputPath + ".en.png");
            L.Set(false, false);
            bool helpRussian = help.Text == "Справка" && HelpFile.EndsWith("help.ru.html") && File.Exists(HelpFile);
            await SaveLayout(outputPath + ".ru.png"); L.Set(true, false);
            profile.SelectedIndex = 1; dither.SelectedIndex = 0; fit.SelectedIndex = 0;
            await pendingPreview;
            await ConvertVideo();
            if (player == null) throw new Exception(log.Text);
            await player.Ready;
            await player.SeekToAsync(60); string forward = player.LastFrameHash;
            await player.SeekToAsync(12); await player.SeekToAsync(60);
            bool seekResult = player.FrameNumber == 60 && forward == player.LastFrameHash;
            using (var image = new Bitmap(player.Width, player.Height))
            {
                player.DrawToBitmap(image, new Rectangle(0, 0, player.Width, player.Height)); image.Save(outputPath + ".player.png");
            }
            await player.SeekToAsync(0); player.StartPlayback(); await Task.Delay(450);
            bool playback = player.FrameNumber > 0; bool audioPlaying = player.AudioActive;
            player.PausePlayback();
            byte[] previousResult = File.ReadAllBytes(outputPath);
            int overwriteRequests = 0;
            overwritePrompt = paths => { overwriteRequests++; return false; };
            await ConvertVideo();
            bool declinedOverwrite = overwriteRequests == 1 && File.ReadAllBytes(outputPath).AsSpan().SequenceEqual(previousResult) && player is { IsDisposed: false };
            profile.SelectedIndex = 2; await pendingPreview;
            overwritePrompt = paths => { overwriteRequests++; return true; };
            await ConvertVideo();
            bool acceptedOverwrite = overwriteRequests == 2 && XdvValidator.Read(outputPath).Mode == 3 && !File.ReadAllBytes(outputPath).AsSpan().SequenceEqual(previousResult);
            if (player != null) await player.Ready;
            overwritePrompt = null;
            bool buttonsVisible = new[] { convert, cancel, folder, resultPreview, help }.All(b =>
            {
                var rect = RectangleToClient(b.RectangleToScreen(b.ClientRectangle));
                return ClientRectangle.Contains(rect) && b.Height >= b.PreferredSize.Height;
            });
            bool helpAligned = Math.Abs(help.PointToScreen(Point.Empty).Y - convert.PointToScreen(Point.Empty).Y) <= 4;
            Scale(new SizeF(1.5f, 1.5f));
            await SaveLayout(outputPath + ".150.png");
            bool scaledButtonsVisible = new[] { convert, cancel, folder, resultPreview, help }.All(b =>
                ClientRectangle.Contains(RectangleToClient(b.RectangleToScreen(b.ClientRectangle))) && b.Parent!.ClientRectangle.Contains(b.Bounds));
            var result = new { Success = helpAligned && helpEnglish && helpRussian && bayer4Works && blueWorks && stabilityWorks && encodingSelection && lastOutput != null && seekWorked && autoWorked && translated && seekResult && playback && buttonsVisible && scaledButtonsVisible && declinedOverwrite && acceptedOverwrite && gammaWorks && saturationWorks && strengthWorks && comparisonToggle && resetWorks && monitorSwitch && monitorTune && monitorReset,
                HelpEnglish = helpEnglish, HelpRussian = helpRussian, HelpAlignedWithActions = helpAligned,
                Bayer4 = bayer4Works, BlueNoise = blueWorks, StabilityControl = stabilityWorks,
                NtscEncodingSelection = encodingSelection, NtscSwitch = monitorSwitch, NtscTuning = monitorTune, NtscReset = monitorReset,
                GammaAdjustment = gammaWorks, SaturationAdjustment = saturationWorks, DitherStrengthAdjustment = strengthWorks,
                ComparisonToggle = comparisonToggle, ColorReset = resetWorks, SourceUnaffectedByAdjustments = original == Hash(picture.Frames!.Source),
                PreviewLoaded = picture.Frames != null, ProfileCount = profile.Items.Count, MediaLoaded = media != null,
                ControlsRestored = convert.Enabled && !cancel.Enabled, SourceSeek = seekWorked, AutomaticPreview = autoWorked,
                English = translated, ResultSeek = seekResult, Playback = playback, AudioActive = audioPlaying,
                OverwriteDeclinedPreservesFile = declinedOverwrite, OverwriteAccepted = acceptedOverwrite, OverwriteRequests = overwriteRequests,
                FileButtonWidthAt96Dpi = 86,
                ButtonsFullyVisible = buttonsVisible, ScaledButtonsFullyVisible = scaledButtonsVisible, DeviceDpi, PreviewsCompleted = previewsCompleted };
            File.WriteAllText(outputPath + ".ui-test.json", JsonSerializer.Serialize(result, Data.Json));
        }
        catch (Exception ex) { File.WriteAllText(outputPath + ".ui-test.json", JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }, Data.Json)); }
        finally { player?.Close(); Close(); }
    }

    // App-owned render for layout regression tests; does not capture other windows.
    private async Task SaveLayout(string path)
    {
        PerformLayout();
        await Task.Delay(50); // Let native combo boxes finish repainting after a language change.
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
        image.Save(path);
    }
}
