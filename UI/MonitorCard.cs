using MonitorControl.Models;

namespace MonitorControl.UI;

internal sealed class MonitorCard : RoundedPanel
{
    private readonly DisplayDevice _device;
    private readonly Panel _accent;
    private readonly Label _nameLabel;
    private readonly Label _infoLabel;
    private readonly Label _statusBadge;
    private readonly Button _primaryButton;
    private readonly Button _soloButton;
    private readonly Button _toggleButton;

    private readonly Label _resolutionLabel;
    private readonly Label _rateLabel;
    private readonly Label _orientationLabel;
    private readonly ComboBox _resolutionCombo;
    private readonly ComboBox _refreshRateCombo;
    private readonly ComboBox _orientationCombo;
    private readonly Button _applyButton;
    private readonly Label _inactiveHintLabel;

    private bool _suppressSelectionEvents;
    private bool _interactionLocked;
    private readonly bool _canToggle;
    private readonly bool _canSolo;
    private readonly bool _showPrimaryButton;

    public string IdentityKey => _device.IdentityKey;

    public event Action<DisplayDevice, bool>? ToggleRequested;
    public event Action<DisplayDevice>? SoloRequested;
    public event Action<DisplayDevice>? SetPrimaryRequested;
    public event Action<DisplayDevice, uint, uint, uint, DisplayOrientation>? ApplyModeRequested;
    public event Action<string>? CardSelected;

    public MonitorCard(DisplayDevice device, int activeMonitorCount, ToolTip toolTip)
    {
        _device = device;
        CornerRadius = 16;
        BackColor = Theme.SurfaceWhite;

        _canToggle = !device.IsActive || activeMonitorCount > 1;
        _canSolo = !(device.IsActive && activeMonitorCount <= 1);
        _showPrimaryButton = device.IsActive && !device.IsPrimary && activeMonitorCount > 1;

        _accent = new Panel
        {
            BackColor = device.IsActive ? Theme.AccentBlue : Theme.InactiveAccent
        };

        _nameLabel = new Label
        {
            Text = $"{device.DisplayNumber}. {device.DisplayTitle}",
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.CardTitleFont,
            ForeColor = Theme.TextPrimary
        };

        _infoLabel = new Label
        {
            Text = BuildSubtitle(device),
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.CardSubtitleFont,
            ForeColor = Theme.TextMuted
        };

        toolTip.SetToolTip(_nameLabel, $"{device.DisplayNumber}. {device.DisplayTitle}");
        toolTip.SetToolTip(_infoLabel, _infoLabel.Text);

        var (badgeText, badgeBack, badgeFore) = device switch
        {
            { IsActive: true, IsPrimary: true } => ("●  Principal", Theme.PrimaryBadgeBack, Theme.PrimaryBadgeFore),
            { IsActive: true } => ("●  Ativo", Theme.ActiveBadgeBack, Theme.ActiveBadgeFore),
            _ => ("●  Inativo", Theme.InactiveBadgeBack, Theme.InactiveBadgeFore)
        };

        _statusBadge = new Label
        {
            Text = badgeText,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = badgeBack,
            ForeColor = badgeFore,
            Font = Theme.BadgeFont
        };

        _primaryButton = CreateSecondaryButton("Tornar principal");
        _primaryButton.Visible = _showPrimaryButton;

        _soloButton = CreateSecondaryButton("Somente este");
        _soloButton.Enabled = _canSolo;

        _toggleButton = CreateSecondaryButton(device.IsActive ? "Desativar" : "Ativar");
        _toggleButton.Enabled = _canToggle;

        if (!_canToggle && device.IsActive)
        {
            toolTip.SetToolTip(_toggleButton, "Não é possível desativar o único monitor ativo.");
        }
        if (_canSolo && !device.IsActive)
        {
            toolTip.SetToolTip(_soloButton, "Ativar este monitor e desligar todos os outros.");
        }

        _resolutionLabel = CreateFieldLabel("RESOLUÇÃO");
        _rateLabel = CreateFieldLabel("TAXA DE ATUALIZAÇÃO");
        _orientationLabel = CreateFieldLabel("ORIENTAÇÃO");

        _resolutionCombo = CreateComboBox(device.IsActive);
        _refreshRateCombo = CreateComboBox(device.IsActive);
        _orientationCombo = CreateComboBox(device.IsActive);

        _applyButton = new Button
        {
            Text = "Aplicar alterações",
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Font = Theme.ButtonSemiboldFont,
            Enabled = false,
            BackColor = Theme.AccentBlueDisabled,
            Cursor = Cursors.Hand
        };
        _applyButton.FlatAppearance.BorderSize = 0;

        _inactiveHintLabel = new Label
        {
            Text = "Este monitor está desativado. Clique em \"Ativar\" ou \"Somente este\" para configurar resolução, taxa e orientação.",
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.CardSubtitleFont,
            ForeColor = Theme.TextSecondary,
            Visible = !device.IsActive
        };

        var modeControlsVisible = device.IsActive;
        _resolutionLabel.Visible = modeControlsVisible;
        _rateLabel.Visible = modeControlsVisible;
        _orientationLabel.Visible = modeControlsVisible;
        _resolutionCombo.Visible = modeControlsVisible;
        _refreshRateCombo.Visible = modeControlsVisible;
        _orientationCombo.Visible = modeControlsVisible;
        _applyButton.Visible = modeControlsVisible;

        if (device.IsActive)
        {
            PopulateControls();
        }

        _resolutionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelectionEvents) return;
            PopulateRefreshRates(preferredRate: null);
            UpdateApplyButtonState();
        };
        _refreshRateCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelectionEvents) return;
            UpdateApplyButtonState();
        };
        _orientationCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelectionEvents) return;
            UpdateApplyButtonState();
        };

        _toggleButton.Click += (_, _) => ToggleRequested?.Invoke(_device, _device.IsActive);
        _soloButton.Click += (_, _) => SoloRequested?.Invoke(_device);
        _primaryButton.Click += (_, _) => SetPrimaryRequested?.Invoke(_device);
        _applyButton.Click += (_, _) =>
        {
            if (_resolutionCombo.SelectedItem is DisplayResolution res &&
                _refreshRateCombo.SelectedItem is RefreshRateOption rate &&
                _orientationCombo.SelectedItem is OrientationOption orient)
            {
                ApplyModeRequested?.Invoke(_device, res.Width, res.Height, rate.Rate, orient.Value);
            }
        };

        Controls.AddRange(
        [
            _accent,
            _nameLabel,
            _infoLabel,
            _statusBadge,
            _primaryButton,
            _soloButton,
            _toggleButton,
            _resolutionLabel,
            _rateLabel,
            _orientationLabel,
            _resolutionCombo,
            _refreshRateCombo,
            _orientationCombo,
            _applyButton,
            _inactiveHintLabel
        ]);

        Click += (_, _) => CardSelected?.Invoke(_device.IdentityKey);
        _nameLabel.Click += (_, _) => CardSelected?.Invoke(_device.IdentityKey);
        _infoLabel.Click += (_, _) => CardSelected?.Invoke(_device.IdentityKey);
        _resolutionCombo.Enter += (_, _) => CardSelected?.Invoke(_device.IdentityKey);
        _refreshRateCombo.Enter += (_, _) => CardSelected?.Invoke(_device.IdentityKey);
        _orientationCombo.Enter += (_, _) => CardSelected?.Invoke(_device.IdentityKey);

        PerformCardLayout();
    }

    public void SetSelectedHighlight(bool selected)
    {
        var targetBorder = selected ? Color.FromArgb(132, 156, 246) : Theme.CardBorder;
        if (BorderColor != targetBorder)
        {
            BorderColor = targetBorder;
            Invalidate();
        }
    }

    public void SetInteractionLocked(bool locked)
    {
        _interactionLocked = locked;
        _toggleButton.Enabled = !locked && _canToggle;
        _soloButton.Enabled = !locked && _canSolo;
        _primaryButton.Enabled = !locked;
        _resolutionCombo.Enabled = !locked && _device.IsActive;
        _refreshRateCombo.Enabled = !locked && _device.IsActive;
        _orientationCombo.Enabled = !locked && _device.IsActive;
        UpdateApplyButtonState();
    }

    private void PopulateControls()
    {
        _suppressSelectionEvents = true;
        try
        {
            var resolutions = _device.AvailableModes
                .Select(m => new DisplayResolution(m.Width, m.Height))
                .Distinct()
                .OrderByDescending(r => (ulong)r.Width * r.Height)
                .ThenByDescending(r => r.Width)
                .ToList();

            foreach (var res in resolutions)
            {
                _resolutionCombo.Items.Add(res);
            }

            foreach (var orient in OrientationOption.All)
            {
                _orientationCombo.Items.Add(orient);
            }

            if (_device.CurrentMode is not null)
            {
                var currentRes = resolutions.FirstOrDefault(r =>
                    r.Width == _device.CurrentMode.Width &&
                    r.Height == _device.CurrentMode.Height);

                if (currentRes is not null)
                {
                    _resolutionCombo.SelectedItem = currentRes;
                }
                else if (_resolutionCombo.Items.Count > 0)
                {
                    _resolutionCombo.SelectedIndex = 0;
                }

                PopulateRefreshRates(_device.CurrentMode.RefreshRate);

                var currentOrient = OrientationOption.All.FirstOrDefault(o => o.Value == _device.CurrentMode.Orientation)
                    ?? OrientationOption.All[0];
                _orientationCombo.SelectedItem = currentOrient;
            }
            else if (_resolutionCombo.Items.Count > 0)
            {
                _resolutionCombo.SelectedIndex = 0;
                PopulateRefreshRates(null);
                _orientationCombo.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
            UpdateApplyButtonState();
        }
    }

    private void PopulateRefreshRates(uint? preferredRate)
    {
        var previousSelection = preferredRate ?? (_refreshRateCombo.SelectedItem as RefreshRateOption)?.Rate;
        _refreshRateCombo.Items.Clear();

        if (_resolutionCombo.SelectedItem is not DisplayResolution selectedRes) return;

        var rates = _device.AvailableModes
            .Where(m => m.Width == selectedRes.Width && m.Height == selectedRes.Height)
            .Select(m => m.RefreshRate)
            .Distinct()
            .OrderByDescending(r => r)
            .Select(r => new RefreshRateOption(r))
            .ToList();

        foreach (var rate in rates)
        {
            _refreshRateCombo.Items.Add(rate);
        }

        if (rates.Count == 0) return;

        var match = previousSelection.HasValue
            ? rates.FirstOrDefault(r => r.Rate == previousSelection.Value)
            : null;

        _refreshRateCombo.SelectedItem = match ?? rates[0];
    }

    private void UpdateApplyButtonState()
    {
        var canApply = false;
        if (!_interactionLocked &&
            _device.IsActive &&
            _resolutionCombo.SelectedItem is DisplayResolution res &&
            _refreshRateCombo.SelectedItem is RefreshRateOption rate &&
            _orientationCombo.SelectedItem is OrientationOption orient)
        {
            if (_device.CurrentMode is null)
            {
                canApply = true;
            }
            else
            {
                canApply =
                    res.Width != _device.CurrentMode.Width ||
                    res.Height != _device.CurrentMode.Height ||
                    rate.Rate != _device.CurrentMode.RefreshRate ||
                    orient.Value != _device.CurrentMode.Orientation;
            }
        }

        _applyButton.Enabled = canApply;
        _applyButton.BackColor = canApply ? Theme.AccentBlue : Theme.AccentBlueDisabled;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        PerformCardLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformCardLayout();
    }

    private void PerformCardLayout()
    {
        int S(int px) => Theme.Scale(this, px);

        var targetHeight = _device.IsActive ? S(168) : S(132);
        if (Height != targetHeight)
        {
            Height = targetHeight;
        }

        Margin = new Padding(0, 0, 0, S(16));

        _accent.Location = new Point(0, S(22));
        _accent.Size = new Size(S(4), Height - S(44));

        var rightEdge = Width - S(20);
        var topRowY = S(18);
        var btnHeight = S(34);

        _toggleButton.Size = new Size(S(98), btnHeight);
        _toggleButton.Location = new Point(rightEdge - _toggleButton.Width, topRowY);
        rightEdge = _toggleButton.Left - S(8);

        _soloButton.Size = new Size(S(114), btnHeight);
        _soloButton.Location = new Point(rightEdge - _soloButton.Width, topRowY);
        rightEdge = _soloButton.Left - S(8);

        if (_showPrimaryButton)
        {
            _primaryButton.Size = new Size(S(128), btnHeight);
            _primaryButton.Location = new Point(rightEdge - _primaryButton.Width, topRowY);
            rightEdge = _primaryButton.Left - S(8);
        }

        _statusBadge.Size = new Size(S(96), S(26));
        _statusBadge.Location = new Point(rightEdge - _statusBadge.Width - S(2), topRowY + S(4));
        rightEdge = _statusBadge.Left - S(14);

        var leftMargin = S(24);
        var textWidth = Math.Max(S(140), rightEdge - leftMargin);

        _nameLabel.Location = new Point(leftMargin, S(18));
        _nameLabel.Size = new Size(textWidth, S(28));

        _infoLabel.Location = new Point(leftMargin + S(1), _nameLabel.Bottom + S(2));
        _infoLabel.Size = new Size(textWidth, S(22));

        if (_device.IsActive)
        {
            var labelY = S(88);
            var controlY = S(109);

            var resX = leftMargin;
            var resWidth = S(175);
            _resolutionLabel.Location = new Point(resX + S(1), labelY);
            _resolutionCombo.Location = new Point(resX, controlY);
            _resolutionCombo.Width = resWidth;

            var rateX = _resolutionCombo.Right + S(14);
            var rateWidth = S(148);
            _rateLabel.Location = new Point(rateX + S(1), labelY);
            _refreshRateCombo.Location = new Point(rateX, controlY);
            _refreshRateCombo.Width = rateWidth;

            var orientX = _refreshRateCombo.Right + S(14);
            var orientWidth = S(162);
            _orientationLabel.Location = new Point(orientX + S(1), labelY);
            _orientationCombo.Location = new Point(orientX, controlY);
            _orientationCombo.Width = orientWidth;

            var applyX = _orientationCombo.Right + S(16);
            _applyButton.Size = new Size(S(148), S(33));
            _applyButton.Location = new Point(applyX, controlY - S(2));
        }
        else
        {
            _inactiveHintLabel.Location = new Point(leftMargin + S(1), S(86));
            _inactiveHintLabel.Size = new Size(Math.Max(S(200), Width - leftMargin - S(24)), S(26));
        }
    }

    private static string BuildSubtitle(DisplayDevice device)
    {
        if (!device.IsActive)
        {
            return $"Monitor desativado • {device.ConnectorName}";
        }

        var role = device.IsPrimary ? "Tela principal" : "Tela estendida";
        if (device.CurrentMode is null)
        {
            return $"{role} • {device.ConnectorName}";
        }

        var orientSuffix = device.CurrentMode.Orientation != DisplayOrientation.Default
            ? $" • {OrientationOption.GetLabel(device.CurrentMode.Orientation)}"
            : string.Empty;

        return $"{role} • {device.ConnectorName} • {device.CurrentMode.Width} × {device.CurrentMode.Height} @ {device.CurrentMode.RefreshRate} Hz{orientSuffix}";
    }

    private static Button CreateSecondaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.SurfaceWhite,
            ForeColor = Theme.ButtonFore,
            Font = Theme.ButtonFont,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = Theme.ButtonBorder;
        return btn;
    }

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.FieldLabelFont,
        ForeColor = Theme.TextMuted
    };

    private static ComboBox CreateComboBox(bool enabled) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.InputBack,
        ForeColor = Theme.TextPrimary,
        Font = Theme.ComboFont,
        Enabled = enabled
    };
}
