using System.ComponentModel;
using System.Diagnostics;
using MonitorControl.Models;
using MonitorControl.Native;
using MonitorControl.Services;

namespace MonitorControl.UI;

internal sealed class MainForm : Form
{
    private readonly Panel _header;
    private readonly Label _markLabel;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Button _refreshButton;
    private readonly Button _restoreAllButton;
    private readonly Button _identifyButton;
    private readonly Button _windowsSettingsButton;

    private readonly FlowLayoutPanel _list;
    private readonly Panel _footer;
    private readonly Label _summaryLabel;

    private readonly System.Windows.Forms.Timer _reloadTimer;
    private readonly System.Windows.Forms.Timer _identifyTimer;
    private readonly ToolTip _toolTip;
    private readonly Icon _appIcon;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;

    private List<DisplayDevice> _devices = [];
    private List<IdentifyOverlayForm> _activeOverlays = [];
    private bool _confirmingModeChange;
    private bool _isBusy;

    public MainForm()
    {
        Text = "Controle de monitores";
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.AppBack;
        Font = Theme.AppDefaultFont;
        KeyPreview = true;

        _appIcon = Theme.CreateAppIcon();
        Icon = _appIcon;

        _toolTip = new ToolTip
        {
            InitialDelay = 350,
            ReshowDelay = 200,
            ShowAlways = true
        };

        _reloadTimer = new System.Windows.Forms.Timer { Interval = 800 };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            SetBusyState(false);
            RefreshMonitors();
        };

        _identifyTimer = new System.Windows.Forms.Timer { Interval = 2600 };
        _identifyTimer.Tick += (_, _) =>
        {
            _identifyTimer.Stop();
            CloseActiveOverlays();
        };

        // Header controls
        _header = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = Theme.SurfaceWhite
        };
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.DividerLine, 1F);
            e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };

        _markLabel = new Label
        {
            Text = "◈",
            AutoSize = true,
            Font = Theme.HeaderMarkFont,
            ForeColor = Theme.AccentBlue
        };

        _titleLabel = new Label
        {
            Text = "Monitores",
            AutoSize = true,
            Font = Theme.HeaderTitleFont,
            ForeColor = Theme.TextPrimary
        };

        _subtitleLabel = new Label
        {
            Text = "Gerencie telas ativas, resolução, taxa de atualização e orientação.",
            AutoSize = true,
            Font = Theme.HeaderSubtitleFont,
            ForeColor = Theme.TextSecondary
        };

        _refreshButton = new Button
        {
            Text = "Atualizar",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.AccentBlue,
            ForeColor = Color.White,
            Font = Theme.ButtonSemiboldFont,
            Cursor = Cursors.Hand
        };
        _refreshButton.FlatAppearance.BorderSize = 0;
        _toolTip.SetToolTip(_refreshButton, "Recarregar lista de monitores (F5)");

        _restoreAllButton = CreateHeaderSecondaryButton("Restabelecer telas");
        _toolTip.SetToolTip(_restoreAllButton, "Reativar todas as telas conectadas em modo estendido");

        _identifyButton = CreateHeaderSecondaryButton("Identificar");
        _toolTip.SetToolTip(_identifyButton, "Mostrar o número de cada tela ativa nos monitores");

        _windowsSettingsButton = CreateHeaderSecondaryButton("Configurações");
        _toolTip.SetToolTip(_windowsSettingsButton, "Abrir configurações de vídeo do Windows");

        _refreshButton.Click += (_, _) => RefreshMonitors();
        _restoreAllButton.Click += (_, _) => RestoreAll();
        _identifyButton.Click += (_, _) => ShowIdentifyOverlays();
        _windowsSettingsButton.Click += (_, _) => OpenWindowsDisplaySettings();

        _header.Controls.AddRange(
        [
            _markLabel,
            _titleLabel,
            _subtitleLabel,
            _refreshButton,
            _restoreAllButton,
            _identifyButton,
            _windowsSettingsButton
        ]);

        // Scrollable card list
        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.AppBack
        };

        // Footer
        _footer = new Panel
        {
            Dock = DockStyle.Bottom,
            BackColor = Theme.SurfaceWhite
        };
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.DividerLine, 1F);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        _summaryLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            Font = Theme.CardSubtitleFont
        };
        _footer.Controls.Add(_summaryLabel);

        Controls.Add(_list);
        Controls.Add(_footer);
        Controls.Add(_header);

        // System tray integration
        _trayMenu = new ContextMenuStrip { Font = Theme.ButtonFont };
        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Controle de monitores",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreAndActivateWindow();

        int S(int px) => Theme.Scale(this, px);
        MinimumSize = new Size(S(920), S(600));
        var workingHeight = Screen.PrimaryScreen?.WorkingArea.Height ?? 900;
        Size = new Size(S(1000), Math.Min(S(880), Math.Max(S(600), workingHeight - S(40))));

        _header.Resize += (_, _) => PerformHeaderLayout();
        _footer.Resize += (_, _) => PerformFooterLayout();
        _list.Resize += (_, _) => UpdateCardsWidth();
        DpiChanged += (_, _) => PerformAllLayouts();

        PerformAllLayouts();
        RefreshMonitors();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.F5 && !_isBusy)
        {
            e.Handled = true;
            RefreshMonitors();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (DisplayNative.WmActivateExistingInstance != 0 && m.Msg == DisplayNative.WmActivateExistingInstance)
        {
            RestoreAndActivateWindow();
            return;
        }

        base.WndProc(ref m);

        if (_confirmingModeChange) return;

        if (m.Msg == DisplayNative.WmDisplayChange ||
            (m.Msg == DisplayNative.WmDeviceChange && (int)m.WParam == DisplayNative.DbtDevNodesChanged))
        {
            // Debounce hardware / OS topology notifications while drivers settle.
            ScheduleReload(550, lockUi: false);
        }
    }

    private void PerformAllLayouts()
    {
        int S(int px) => Theme.Scale(this, px);
        MinimumSize = new Size(S(920), S(580));
        _header.Height = S(118);
        _footer.Height = S(44);
        _list.Padding = new Padding(S(28), S(22), S(28), S(22));

        PerformHeaderLayout();
        PerformFooterLayout();
        UpdateCardsWidth();
    }

    private void PerformHeaderLayout()
    {
        int S(int px) => Theme.Scale(this, px);

        _markLabel.Location = new Point(S(30), S(20));
        _titleLabel.Location = new Point(S(68), S(19));
        _subtitleLabel.Location = new Point(S(70), _titleLabel.Bottom + S(4));

        var btnY = S(28);
        var btnHeight = S(36);
        var right = _header.ClientSize.Width - S(28);

        _refreshButton.Size = new Size(S(102), btnHeight);
        _refreshButton.Location = new Point(right - _refreshButton.Width, btnY);
        right = _refreshButton.Left - S(8);

        _restoreAllButton.Size = new Size(S(138), btnHeight);
        _restoreAllButton.Location = new Point(right - _restoreAllButton.Width, btnY);
        right = _restoreAllButton.Left - S(8);

        _identifyButton.Size = new Size(S(102), btnHeight);
        _identifyButton.Location = new Point(right - _identifyButton.Width, btnY);
        right = _identifyButton.Left - S(8);

        _windowsSettingsButton.Size = new Size(S(126), btnHeight);
        _windowsSettingsButton.Location = new Point(right - _windowsSettingsButton.Width, btnY);
    }

    private void PerformFooterLayout()
    {
        int S(int px) => Theme.Scale(this, px);
        _summaryLabel.Location = new Point(S(30), (_footer.ClientSize.Height - _summaryLabel.Height) / 2);
    }

    private void UpdateCardsWidth()
    {
        int S(int px) => Theme.Scale(this, px);
        var scrollSlack = _list.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
        var targetWidth = Math.Max(S(760), _list.ClientSize.Width - _list.Padding.Horizontal - scrollSlack - S(4));

        foreach (Control c in _list.Controls)
        {
            if (c is RoundedPanel panel && panel.Width != targetWidth)
            {
                panel.Width = targetWidth;
            }
        }
    }

    private void ClearCards()
    {
        _toolTip.RemoveAll();
        // Re-register static header tooltips
        _toolTip.SetToolTip(_refreshButton, "Recarregar lista de monitores (F5)");
        _toolTip.SetToolTip(_restoreAllButton, "Reativar todas as telas conectadas em modo estendido");
        _toolTip.SetToolTip(_identifyButton, "Mostrar o número de cada tela ativa nos monitores");
        _toolTip.SetToolTip(_windowsSettingsButton, "Abrir configurações de vídeo do Windows");

        while (_list.Controls.Count > 0)
        {
            var ctrl = _list.Controls[0];
            _list.Controls.RemoveAt(0);
            ctrl.Dispose();
        }
    }

    private void RefreshMonitors()
    {
        _list.SuspendLayout();
        try
        {
            ClearCards();

            try
            {
                _devices = DisplayService.GetDevices();
            }
            catch (Exception ex)
            {
                _devices = [];
                _summaryLabel.Text = $"Erro ao ler monitores: {ex.Message}";
                RebuildTrayMenu();
                return;
            }

            var activeCount = _devices.Count(d => d.IsActive);
            var inactiveCount = _devices.Count - activeCount;

            _restoreAllButton.Enabled = !_isBusy && inactiveCount > 0;
            _identifyButton.Enabled = !_isBusy && activeCount > 0;

            _summaryLabel.Text = _devices.Count == 0
                ? "Nenhuma tela encontrada."
                : $"{_devices.Count} tela(s) conectada(s)  •  {activeCount} ativa(s)" +
                  (inactiveCount > 0 ? $"  •  {inactiveCount} desativada(s)" : string.Empty);
            PerformFooterLayout();

            if (_devices.Count == 0)
            {
                _list.Controls.Add(new Label
                {
                    Text = "Conecte um monitor e clique em Atualizar.",
                    AutoSize = true,
                    ForeColor = Theme.TextSecondary,
                    Font = Theme.AppDefaultFont,
                    Margin = new Padding(Theme.Scale(this, 4), Theme.Scale(this, 8), 0, 0)
                });
                RebuildTrayMenu();
                return;
            }

            var initialCardWidth = Math.Max(Theme.Scale(this, 760), _list.ClientSize.Width - _list.Padding.Horizontal - Theme.Scale(this, 8));

            if (activeCount > 0)
            {
                var layoutCanvas = new LayoutCanvasCard(_devices, _toolTip)
                {
                    Width = initialCardWidth
                };
                layoutCanvas.ApplyLayoutRequested += OnApplyLayoutRequested;
                layoutCanvas.TileSelected += OnCanvasTileSelected;
                if (_isBusy)
                {
                    layoutCanvas.SetInteractionLocked(true);
                }
                _list.Controls.Add(layoutCanvas);
            }

            foreach (var device in _devices)
            {
                var card = new MonitorCard(device, activeCount, _toolTip)
                {
                    Width = initialCardWidth
                };
                card.ToggleRequested += OnCardToggleRequested;
                card.SoloRequested += OnCardSoloRequested;
                card.SetPrimaryRequested += OnCardSetPrimaryRequested;
                card.ApplyModeRequested += OnCardApplyModeRequested;
                card.CardSelected += OnMonitorCardSelected;

                if (_isBusy)
                {
                    card.SetInteractionLocked(true);
                }

                _list.Controls.Add(card);
            }

            RebuildTrayMenu();
        }
        finally
        {
            _list.ResumeLayout(true);
            UpdateCardsWidth();
        }
    }

    private void RebuildTrayMenu()
    {
        _trayMenu.Items.Clear();

        var openItem = new ToolStripMenuItem("Abrir Controle de Monitores", null, (_, _) => RestoreAndActivateWindow())
        {
            Font = Theme.ButtonSemiboldFont
        };
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new ToolStripSeparator());

        var activeCount = _devices.Count(d => d.IsActive);
        foreach (var device in _devices)
        {
            var statusTag = device.IsPrimary ? " (Principal)" : device.IsActive ? " (Ativo)" : " (Inativo)";
            var item = new ToolStripMenuItem($"{device.DisplayNumber}. {device.DisplayTitle}{statusTag}")
            {
                Checked = device.IsActive,
                Enabled = !_isBusy && (!device.IsActive || activeCount > 1)
            };
            var captured = device;
            item.Click += (_, _) => OnCardToggleRequested(captured, captured.IsActive);
            _trayMenu.Items.Add(item);
        }

        if (_devices.Count > 0)
        {
            _trayMenu.Items.Add(new ToolStripSeparator());
        }

        var identifyItem = new ToolStripMenuItem("Identificar telas", null, (_, _) => ShowIdentifyOverlays())
        {
            Enabled = !_isBusy && activeCount > 0
        };
        var restoreItem = new ToolStripMenuItem("Restabelecer todas as telas", null, (_, _) => RestoreAll())
        {
            Enabled = !_isBusy && _devices.Any(d => !d.IsActive)
        };
        _trayMenu.Items.Add(identifyItem);
        _trayMenu.Items.Add(restoreItem);
        _trayMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Sair", null, (_, _) => Close());
        _trayMenu.Items.Add(exitItem);
    }

    private void RestoreAndActivateWindow()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Show();
        Activate();
    }

    private void OnCardToggleRequested(DisplayDevice device, bool currentlyActive)
    {
        if (_isBusy) return;
        SetBusyState(true, currentlyActive ? "Desativando monitor..." : "Ativando monitor...");

        var result = DisplayService.SetEnabled(device, !currentlyActive);
        if (!result.IsSuccess)
        {
            SetBusyState(false);
            ReportFailure(result);
            RefreshMonitors();
            return;
        }

        ScheduleReload(currentlyActive ? 850 : 1400, lockUi: true);
    }

    private void OnCardSoloRequested(DisplayDevice device)
    {
        if (_isBusy) return;
        SetBusyState(true, "Alternando para tela única...");

        var result = DisplayService.SetSolo(device);
        if (!result.IsSuccess)
        {
            SetBusyState(false);
            ReportFailure(result);
            RefreshMonitors();
            return;
        }

        ScheduleReload(1100, lockUi: true);
    }

    private void OnCardSetPrimaryRequested(DisplayDevice device)
    {
        if (_isBusy) return;
        SetBusyState(true, "Definindo tela principal...");

        var result = DisplayService.SetPrimary(device);
        if (!result.IsSuccess)
        {
            SetBusyState(false);
            ReportFailure(result);
            RefreshMonitors();
            return;
        }

        ScheduleReload(650, lockUi: true);
    }

    private void OnApplyLayoutRequested(Dictionary<string, Point> positionsByIdentity)
    {
        if (_isBusy) return;
        SetBusyState(true, "Aplicando posicionamento das telas...");

        var result = DisplayService.SetPositions(positionsByIdentity, _devices);
        if (!result.IsSuccess)
        {
            SetBusyState(false);
            ReportFailure(result);
            RefreshMonitors();
            return;
        }

        ScheduleReload(650, lockUi: true);
    }

    private void OnCanvasTileSelected(string identityKey)
    {
        foreach (Control ctrl in _list.Controls)
        {
            if (ctrl is MonitorCard card)
            {
                card.SetSelectedHighlight(string.Equals(card.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private void OnMonitorCardSelected(string identityKey)
    {
        foreach (Control ctrl in _list.Controls)
        {
            if (ctrl is LayoutCanvasCard canvas)
            {
                canvas.SelectByDeviceIdentity(identityKey);
            }
            else if (ctrl is MonitorCard card)
            {
                card.SetSelectedHighlight(string.Equals(card.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private void RestoreAll()
    {
        if (_isBusy) return;
        SetBusyState(true, "Restabelecendo todas as telas...");

        var result = DisplayService.RestoreAll();
        if (!result.IsSuccess)
        {
            SetBusyState(false);
            ReportFailure(result);
            RefreshMonitors();
            return;
        }

        ScheduleReload(1300, lockUi: true);
    }

    private void OnCardApplyModeRequested(
        DisplayDevice device,
        uint width,
        uint height,
        uint rate,
        DisplayOrientation orientation)
    {
        if (_isBusy) return;

        var previousMode = device.CurrentMode;
        var result = DisplayService.SetMode(device.GdiName, width, height, rate, orientation);
        if (!result.IsSuccess)
        {
            ReportFailure(result);
            RefreshMonitors();
            return;
        }

        if (result.RequiresRestart)
        {
            MessageBox.Show(
                this,
                result.Message,
                "Reinicialização necessária",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            RefreshMonitors();
            return;
        }

        if (previousMode is not null)
        {
            _confirmingModeChange = true;
            try
            {
                using var confirm = new ConfirmModeDialog();
                var answer = confirm.ShowDialog(this);
                if (answer != DialogResult.OK)
                {
                    DisplayService.SetMode(
                        device.GdiName,
                        previousMode.Width,
                        previousMode.Height,
                        previousMode.RefreshRate,
                        previousMode.Orientation);
                }
            }
            finally
            {
                _confirmingModeChange = false;
            }
        }

        RefreshMonitors();
    }

    private void ShowIdentifyOverlays()
    {
        _identifyTimer.Stop();
        CloseActiveOverlays();
        _activeOverlays = IdentifyOverlayForm.CreateAndShowForDevices(_devices);
        if (_activeOverlays.Count > 0)
        {
            _identifyTimer.Start();
        }
    }

    private void CloseActiveOverlays()
    {
        foreach (var overlay in _activeOverlays)
        {
            if (!overlay.IsDisposed)
            {
                overlay.Close();
                overlay.Dispose();
            }
        }
        _activeOverlays.Clear();
    }

    private static void OpenWindowsDisplaySettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Ignore shell launch failures on restricted environments.
        }
    }

    private void ScheduleReload(int delayMs, bool lockUi)
    {
        _reloadTimer.Stop();
        _reloadTimer.Interval = Math.Max(150, delayMs);
        if (lockUi)
        {
            SetBusyState(true);
        }
        _reloadTimer.Start();
    }

    private void SetBusyState(bool busy, string? statusMessage = null)
    {
        _isBusy = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _refreshButton.Enabled = !busy;
        _restoreAllButton.Enabled = !busy && _devices.Any(d => !d.IsActive);
        _identifyButton.Enabled = !busy && _devices.Any(d => d.IsActive);

        foreach (Control ctrl in _list.Controls)
        {
            if (ctrl is MonitorCard card)
            {
                card.SetInteractionLocked(busy);
            }
            else if (ctrl is LayoutCanvasCard canvas)
            {
                canvas.SetInteractionLocked(busy);
            }
        }

        if (busy && !string.IsNullOrWhiteSpace(statusMessage))
        {
            _summaryLabel.Text = statusMessage;
            PerformFooterLayout();
        }
    }

    private void ReportFailure(DisplayResult result)
    {
        if (result.RequiresElevation)
        {
            var answer = MessageBox.Show(
                this,
                $"{result.Message}\n\nDeseja reiniciar como administrador agora?",
                "Permissão necessária",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer == DialogResult.Yes)
            {
                RestartElevated();
            }
            return;
        }

        MessageBox.Show(
            this,
            result.Message,
            "Não foi possível aplicar",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void RestartElevated()
    {
        var path = Environment.ProcessPath;
        if (path is null) return;

        try
        {
            Process.Start(new ProcessStartInfo(path, "--relaunch") { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Win32Exception)
        {
            // Elevation UAC prompt was cancelled by the user.
        }
    }

    private static Button CreateHeaderSecondaryButton(string text)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reloadTimer.Stop();
            _reloadTimer.Dispose();
            _identifyTimer.Stop();
            _identifyTimer.Dispose();
            CloseActiveOverlays();
            ClearCards();
            _toolTip.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _appIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
