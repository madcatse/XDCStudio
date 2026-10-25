namespace XdcConverter;

public enum TransportIcon { Play, Pause, Stop }

public sealed class TransportButton : Button
{
    public TransportIcon Icon { get; }
    public TransportButton(TransportIcon icon)
    {
        Icon = icon; Text = ""; Size = new Size(32, 28); Margin = new Padding(0, 0, 3, 0);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderColor = Color.FromArgb(170, 180, 190);
        BackColor = Color.White; Cursor = Cursors.Hand;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int size = Math.Max(10, (int)Math.Round(14 * DeviceDpi / 96.0));
        int x = (ClientSize.Width - size) / 2, y = (ClientSize.Height - size) / 2;
        using var brush = new SolidBrush(Enabled ? Color.Black : SystemColors.GrayText);
        switch (Icon)
        {
            case TransportIcon.Play:
                e.Graphics.FillPolygon(brush, [new Point(x, y), new Point(x + size, y + size / 2), new Point(x, y + size)]); break;
            case TransportIcon.Pause:
                int bar = Math.Max(3, size / 3);
                e.Graphics.FillRectangle(brush, x, y, bar, size);
                e.Graphics.FillRectangle(brush, x + size - bar, y, bar, size); break;
            case TransportIcon.Stop:
                e.Graphics.FillRectangle(brush, x, y, size, size); break;
        }
    }
}
