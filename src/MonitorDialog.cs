namespace XdcConverter;

internal static class MonitorDialog
{
    public static void Show(IWin32Window? owner, string? testReport = null)
    {
        using var form = new Form { Text = L.T("Монитор · только предпросмотр", "Monitor · preview only"), Icon = AppBrand.Icon,
            Font = new Font("Segoe UI", 9), BackColor = MainForm.WindowColor, ClientSize = new Size(420, 235),
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        model.Items.AddRange([L.T("NTSC · ранний IBM CGA", "NTSC · early IBM CGA"), L.T("NTSC · поздний IBM CGA", "NTSC · late IBM CGA"), L.T("Палитра XDC (как в 0.4)", "XDC palette (as in 0.4)")]);
        model.SelectedIndex = !MonitorSettings.Current.Enabled ? 2 : MonitorSettings.Current.NewCga ? 1 : 0;
        var hue = new NumericUpDown { Minimum = -45, Maximum = 45, Value = (decimal)MonitorSettings.Current.Hue, Dock = DockStyle.Fill };
        var saturation = new NumericUpDown { Minimum = 0, Maximum = 200, Increment = 5, Value = (decimal)MonitorSettings.Current.Saturation * 100, Dock = DockStyle.Fill };
        void Row(int row, string title, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(control, 1, row); control.AccessibleName = title;
        }
        Row(0, L.T("Модель", "Model"), model); Row(1, L.T("Оттенок, градусы", "Hue, degrees"), hue); Row(2, L.T("Цветность, %", "Colour intensity, %"), saturation);
        var note = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = L.T("Настройки действуют в обоих окнах просмотра.\nГотовый XDV не меняется. Реальный монитор может отличаться.", "Applies to both preview windows.\nDoes not change XDV. Real monitors can differ.") };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.Controls.Add(note, 0, 3); layout.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = L.T("Закрыть", "Close"), AutoSize = true, DialogResult = DialogResult.OK };
        var reset = new Button { Text = L.T("Сбросить", "Reset"), AutoSize = true };
        buttons.Controls.AddRange([close, reset]); layout.Controls.Add(buttons, 0, 4); layout.SetColumnSpan(buttons, 2);
        bool updating = false;
        void Apply()
        {
            hue.Enabled = saturation.Enabled = model.SelectedIndex != 2;
            if (!updating) MonitorSettings.Set(new(model.SelectedIndex != 2, model.SelectedIndex == 1, (double)hue.Value, (double)saturation.Value / 100), testReport == null);
        }
        model.SelectedIndexChanged += (_, _) => Apply(); hue.ValueChanged += (_, _) => Apply(); saturation.ValueChanged += (_, _) => Apply();
        reset.Click += (_, _) => { updating = true; model.SelectedIndex = 0; hue.Value = 0; saturation.Value = 100; updating = false; Apply(); };
        hue.Enabled = saturation.Enabled = MonitorSettings.Current.Enabled;
        form.Controls.Add(layout); form.AcceptButton = close; form.CancelButton = close;
        if (testReport != null) form.Shown += async (_, _) =>
        {
            var previous = MonitorSettings.Current;
            try
            {
                model.SelectedIndex = 1; hue.Value = 12; saturation.Value = 85;
                bool tuning = MonitorSettings.Current == new MonitorOptions(true, true, 12, .85);
                model.SelectedIndex = 2; bool disabled = !hue.Enabled && !saturation.Enabled && !MonitorSettings.Current.Enabled;
                reset.PerformClick(); bool restored = MonitorSettings.Current == new MonitorOptions();
                await Task.Delay(80);
                using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(testReport + ".png");
                bool visible = form.ClientRectangle.Contains(form.RectangleToClient(close.RectangleToScreen(close.ClientRectangle)));
                File.WriteAllText(testReport, System.Text.Json.JsonSerializer.Serialize(new { Success = tuning && disabled && restored && visible, Tuning = tuning, PaletteDisablesControls = disabled, Reset = restored, ButtonsVisible = visible }, Data.Json));
            }
            catch (Exception ex) { File.WriteAllText(testReport, System.Text.Json.JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }, Data.Json)); }
            finally { MonitorSettings.Set(previous, false); form.Close(); }
        };
        form.ShowDialog(owner);
    }
}
