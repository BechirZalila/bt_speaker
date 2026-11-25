using System.Reflection;

namespace BtBatteryTray;

/// <summary>
/// Fenêtre « À propos » : version, auteur, licence et liens du projet.
/// Les textes viennent des attributs de l'assembly (renseignés dans le .csproj).
/// </summary>
internal sealed class AboutForm : Form
{
    private const string ProjectUrl = "https://github.com/BechirZalila/bt_speaker";

    public AboutForm()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "BT Battery Tray";
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        var author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";

        Text = $"À propos de {product}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            // Pas de Dock.Fill : c'est le tableau qui donne sa taille à la fenêtre (AutoSize), pas l'inverse.
            Location = new Point(Padding.Left, Padding.Top),
        };

        var picture = new PictureBox
        {
            Image = Icon?.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(48, 48),
            Margin = new Padding(0, 0, 16, 0),
        };
        layout.Controls.Add(picture, 0, 0);
        layout.SetRowSpan(picture, 8);

        var row = 0;
        void AddLabel(string text, Font? font = null)
        {
            layout.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Font = font ?? Font,
                Margin = new Padding(0, 0, 0, 6),
            }, 1, row++);
        }

        void AddLink(string prefix, string text, string url)
        {
            var link = new LinkLabel
            {
                Text = prefix + text,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            link.Links.Add(prefix.Length, text.Length, url);
            link.LinkClicked += (_, e) => UpdateChecker.OpenPage((string)e.Link!.LinkData!);
            layout.Controls.Add(link, 1, row++);
        }

        AddLabel(product, new Font(Font.FontFamily, Font.Size * 1.4f, FontStyle.Bold));
        AddLabel($"Version {UpdateChecker.CurrentVersion}");
        AddLabel(description);
        AddLabel($"Auteur : {author}");
        AddLabel(copyright);
        AddLink("Licence : ", "MIT", $"{ProjectUrl}/blob/main/LICENSE");
        AddLink("Code source : ", ProjectUrl, ProjectUrl);
        AddLink("Signaler un problème : ", "GitHub Issues", $"{ProjectUrl}/issues");

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, AutoSize = true };
        layout.Controls.Add(ok, 1, row);
        AcceptButton = CancelButton = ok;

        Controls.Add(layout);
    }

    // Form.AutoSize ne suit pas le tableau : on ajuste la taille une fois la mise à l'échelle DPI appliquée.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var size = ((TableLayoutPanel)Controls[0]).PreferredSize;
        ClientSize = new Size(size.Width + Padding.Horizontal, size.Height + Padding.Vertical);
    }
}
