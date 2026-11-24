using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BtBatteryTray;

/// <summary>
/// Dessine une icône de la zone de notification contenant le niveau de batterie.
/// </summary>
internal static class BatteryIcon
{
    public static Icon Create(int? level, Size? iconSize = null)
    {
        using var bitmap = Render(level, iconSize ?? SystemInformation.SmallIconSize);

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

    public static Bitmap Render(int? level, Size size)
    {
        var text = level is null ? "--" : level.Value.ToString();
        var color = level switch
        {
            null => Color.Gray,
            > 50 => Color.FromArgb(0x3C, 0xC8, 0x50),
            > 20 => Color.Orange,
            _ => Color.FromArgb(0xF0, 0x40, 0x40),
        };

        var bitmap = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Le texte est converti en contour vectoriel puis mis à l'échelle pour remplir l'icône :
        // aucun risque de troncature, quelle que soit la taille d'icône (16, 20, 24 px…).
        using var path = new GraphicsPath();
        using (var family = new FontFamily("Segoe UI"))
        using (var format = StringFormat.GenericTypographic)
        {
            path.AddString(text, family, (int)FontStyle.Bold, 100f, PointF.Empty, format);
        }

        var bounds = path.GetBounds();
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            const float margin = 0.5f;
            var maxWidth = size.Width - 2 * margin;
            var maxHeight = size.Height - 2 * margin;
            // Hauteur visée ≈ 80 % de l'icône ; on réduit si le texte est trop large (ex. "100").
            var scale = Math.Min(maxWidth / bounds.Width, maxHeight * 0.8f / bounds.Height);

            using var matrix = new Matrix();
            matrix.Translate(size.Width / 2f, size.Height / 2f);
            matrix.Scale(scale, scale);
            matrix.Translate(-(bounds.X + bounds.Width / 2f), -(bounds.Y + bounds.Height / 2f));
            path.Transform(matrix);

            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }

        return bitmap;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
