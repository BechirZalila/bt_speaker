using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BtBatteryTray;

/// <summary>
/// Dessine une icône de la zone de notification contenant le niveau de batterie.
/// </summary>
internal static class BatteryIcon
{
    public static Icon Create(int? level)
    {
        var size = SystemInformation.SmallIconSize;
        var text = level is null ? "--" : level.Value.ToString();
        var color = level switch
        {
            null => Color.Gray,
            > 50 => Color.FromArgb(0x3C, 0xC8, 0x50),
            > 20 => Color.Orange,
            _ => Color.FromArgb(0xF0, 0x40, 0x40),
        };

        using var bitmap = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var font = FitFont(g, text, size);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            g.DrawString(text, font, brush, new RectangleF(0, 0, size.Width, size.Height + 1), format);
        }

        // Icon.FromHandle ne possède pas le handle : on clone puis on libère le handle natif.
        var hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    // Plus grande police qui fait tenir le texte (jusqu'à "100") dans l'icône.
    private static Font FitFont(Graphics g, string text, Size size)
    {
        for (var px = size.Height; px > 6; px--)
        {
            var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            if (measured.Width <= size.Width && measured.Height <= size.Height + 2)
                return font;
            font.Dispose();
        }
        return new Font("Segoe UI", 6, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
