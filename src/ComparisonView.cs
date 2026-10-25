using System.Drawing.Drawing2D;

namespace XdcConverter;

public sealed record PreviewFrames(Bitmap Source, Bitmap Result) : IDisposable
{
    public void Dispose() { Source.Dispose(); Result.Dispose(); }
}

public sealed class ComparisonView : Control
{
    private PreviewFrames? frames;
    private bool compare = true;
    private double divider = 0.5;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PreviewFrames? Frames
    {
        get => frames;
        set { if (!ReferenceEquals(frames, value)) { frames?.Dispose(); frames = value; Invalidate(); } }
    }
    [System.ComponentModel.DefaultValue(true)]
    public bool Compare { get => compare; set { compare = value; Cursor = value ? Cursors.VSplit : Cursors.Default; Invalidate(); } }
    [System.ComponentModel.DefaultValue(0.5)]
    public double Divider { get => divider; set { divider = Math.Clamp(value, 0, 1); Invalidate(); } }
    public ComparisonView()
    {
        DoubleBuffered = true; ResizeRedraw = true; TabStop = true;
        BackColor = Color.FromArgb(16, 23, 30); Cursor = Cursors.VSplit;
        AccessibleRole = AccessibleRole.Graphic;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (frames == null) return;
        double scale = Math.Min(Width / 640.0, Height / 480.0);
        var bounds = new Rectangle((Width - (int)(640 * scale)) / 2, (Height - (int)(480 * scale)) / 2, (int)(640 * scale), (int)(480 * scale));
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(compare ? frames.Source : frames.Result, bounds);
        if (compare)
        {
            int x = (int)Math.Round(Width * divider);
            var state = e.Graphics.Save(); e.Graphics.SetClip(new Rectangle(x, 0, Width - x, Height));
            e.Graphics.DrawImage(frames.Result, bounds); e.Graphics.Restore(state);
            using var line = new Pen(Color.White, Math.Max(1, DeviceDpi / 96f));
            e.Graphics.DrawLine(line, x, 0, x, Height);
            int radius = (int)(9 * DeviceDpi / 96f);
            using var brush = new SolidBrush(Color.FromArgb(0, 119, 128));
            e.Graphics.FillEllipse(brush, x - radius, Height / 2 - radius, radius * 2, radius * 2);
            e.Graphics.DrawLine(line, x - radius / 3, Height / 2 - radius / 2, x - radius / 3, Height / 2 + radius / 2);
            e.Graphics.DrawLine(line, x + radius / 3, Height / 2 - radius / 2, x + radius / 3, Height / 2 + radius / 2);
            if (x > TextRenderer.MeasureText(L.T("Исходник", "Source"), Font).Width + 16) Caption(e.Graphics, L.T("Исходник", "Source"), false);
        }
        if (!compare || Width * (1 - divider) > TextRenderer.MeasureText(L.T("Результат", "Result"), Font).Width + 16)
            Caption(e.Graphics, L.T("Результат", "Result"), true);
    }
    private void Caption(Graphics graphics, string text, bool right)
    {
        var size = TextRenderer.MeasureText(text, Font);
        var rect = new Rectangle(right ? Width - size.Width - 12 : 4, 4, size.Width + 8, size.Height + 4);
        using var brush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)); graphics.FillRectangle(brush, rect);
        TextRenderer.DrawText(graphics, text, Font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (compare && e.Button == MouseButtons.Left) { Focus(); Capture = true; Divider = e.X / (double)Math.Max(1, Width); }
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (compare && Capture) Divider = e.X / (double)Math.Max(1, Width); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!compare) return;
        switch (e.KeyCode)
        {
            case Keys.Left: Divider -= 0.02; break;
            case Keys.Right: Divider += 0.02; break;
            case Keys.Home: Divider = 0; break;
            case Keys.End: Divider = 1; break;
            default: return;
        }
        e.Handled = true;
    }
    protected override void Dispose(bool disposing) { if (disposing) Frames = null; base.Dispose(disposing); }
}
