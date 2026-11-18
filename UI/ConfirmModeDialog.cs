namespace MonitorControl.UI;

internal sealed class ConfirmModeDialog : Form
{
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly Label _countdownLabel;
    private int _secondsRemaining = 15;

    public ConfirmModeDialog()
    {
        Text = "Confirmar alterações de exibição";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.SurfaceWhite;
        Font = Theme.AppDefaultFont;
        TopMost = true;

        var title = new Label
        {
            Text = "Manter estas configurações de vídeo?",
            Font = Theme.DialogTitleFont,
            ForeColor = Theme.TextPrimary,
            AutoSize = true
        };

        var description = new Label
        {
            Text = "Caso a imagem não esteja adequada, pressione Esc ou aguarde para restaurar o modo anterior.",
            Font = Theme.CardSubtitleFont,
            ForeColor = Theme.TextSecondary,
            AutoSize = false
        };

        _countdownLabel = new Label
        {
            Text = GetCountdownText(),
            Font = Theme.ButtonSemiboldFont,
            ForeColor = Theme.AccentBlue,
            AutoSize = true
        };

        var keepButton = new Button
        {
            Text = "Manter alterações",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.AccentBlue,
            ForeColor = Color.White,
            Font = Theme.ButtonSemiboldFont
        };
        keepButton.FlatAppearance.BorderSize = 0;

        var revertButton = new Button
        {
            Text = "Reverter agora",
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.SurfaceWhite,
            ForeColor = Theme.ButtonFore,
            Font = Theme.ButtonFont
        };
        revertButton.FlatAppearance.BorderColor = Theme.ButtonBorder;

        AcceptButton = keepButton;
        CancelButton = revertButton;

        Controls.AddRange([title, description, _countdownLabel, keepButton, revertButton]);

        void PerformDialogLayout()
        {
            int S(int px) => Theme.Scale(this, px);

            ClientSize = new Size(S(440), S(195));
            title.Location = new Point(S(24), S(20));

            description.Location = new Point(S(24), title.Bottom + S(8));
            description.Size = new Size(ClientSize.Width - S(48), S(42));

            _countdownLabel.Location = new Point(S(24), description.Bottom + S(6));

            keepButton.Size = new Size(S(148), S(36));
            revertButton.Size = new Size(S(126), S(36));

            keepButton.Location = new Point(ClientSize.Width - keepButton.Width - S(24), ClientSize.Height - keepButton.Height - S(20));
            revertButton.Location = new Point(keepButton.Left - revertButton.Width - S(10), keepButton.Top);
        }

        Shown += (_, _) =>
        {
            PerformDialogLayout();
            revertButton.Focus(); // Default focus on Revert so Enter/Space or Esc safely reverts if user is blind, or click Keep.
        };
        DpiChanged += (_, _) => PerformDialogLayout();
        PerformDialogLayout();

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (_, _) =>
        {
            _secondsRemaining--;
            if (_secondsRemaining <= 0)
            {
                _countdownTimer.Stop();
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            _countdownLabel.Text = GetCountdownText();
        };
        _countdownTimer.Start();
    }

    private string GetCountdownText() =>
        $"Revertendo automaticamente em {_secondsRemaining} segundo(s)...";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Stop();
            _countdownTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
