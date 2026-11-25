namespace BtBatteryTray;

/// <summary>
/// Fenêtre affichée pendant une mise à jour, du clic sur « Oui » jusqu'à l'apparition de la fenêtre
/// de l'assistant : téléchargement (avec pourcentage et bouton Annuler), vérification, lancement.
/// </summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly Label _status;
    private readonly ProgressBar _bar;
    private readonly Button _cancel;
    private readonly TableLayoutPanel _layout;
    private bool _canCancel = true;
    private bool _closingByCode;

    /// <summary>L'utilisateur a cliqué sur Annuler ou fermé la fenêtre pendant le téléchargement.</summary>
    public event EventHandler? CancelRequested;

    public UpdateProgressForm(Version version)
    {
        Text = "Mise à jour de BT Battery Tray";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        _layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(Padding.Left, Padding.Top),
        };
        _layout.Controls.Add(new Label
        {
            Text = $"Installation de la version {version}",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        });
        // Largeur fixe : le texte change pendant la mise à jour et la fenêtre ne doit pas « sauter ».
        _status = new Label { Text = "Téléchargement…", AutoSize = false, Size = new Size(340, 20), Margin = new Padding(0, 0, 0, 4) };
        _layout.Controls.Add(_status);
        _bar = new ProgressBar { Size = new Size(340, 18), Minimum = 0, Maximum = 100, Margin = new Padding(0, 0, 0, 12) };
        _layout.Controls.Add(_bar);
        _cancel = new Button { Text = "Annuler", AutoSize = true, Anchor = AnchorStyles.Right };
        _cancel.Click += (_, _) => Close();
        _layout.Controls.Add(_cancel);
        CancelButton = _cancel;

        Controls.Add(_layout);
    }

    public void SetDownloadProgress(int percent)
    {
        if (IsDisposed)
            return;
        _bar.Value = Math.Clamp(percent, 0, 100);
        _status.Text = percent >= 100 ? "Vérification du fichier…" : $"Téléchargement… {percent} %";
    }

    /// <summary>Phase finale : l'assistant démarre, l'annulation n'est plus possible.</summary>
    public void SetInstalling()
    {
        _canCancel = false;
        _cancel.Enabled = false;
        _bar.Style = ProgressBarStyle.Marquee;
        _status.Text = "Lancement de l'installation…";
    }

    public void CloseQuietly()
    {
        if (IsDisposed)
            return;
        _closingByCode = true;
        Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var size = _layout.PreferredSize;
        ClientSize = new Size(size.Width + Padding.Horizontal, size.Height + Padding.Vertical);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Close() appelé par le code arrive aussi avec CloseReason.UserClosing, d'où _closingByCode.
        if (!_closingByCode && e.CloseReason == CloseReason.UserClosing)
        {
            if (!_canCancel)
            {
                e.Cancel = true;
                return;
            }
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnFormClosing(e);
    }
}
