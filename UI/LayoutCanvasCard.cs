using System.Drawing.Drawing2D;
using MonitorControl.Models;
using MonitorControl.Services;

namespace MonitorControl.UI;

internal sealed class LayoutCanvasCard : RoundedPanel
{
    private readonly List<LayoutTile> _tiles;
    private readonly Label _titleLabel;
    private readonly Label _hintLabel;
    private readonly Button _resetButton;
    private readonly Button _applyButton;
    private readonly CanvasSurface _viewport;

    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private bool _isDragging;
    private bool _interactionLocked;

    private Point _dragStartMouse;
    private Point _dragStartTilePos;
    private double _frozenScale = 0.05;
    private PointF _frozenWorldOrigin;
    private List<AlignmentGuide> _activeGuides = [];

    public event Action<Dictionary<string, Point>>? ApplyLayoutRequested;
    public event Action<string>? TileSelected;

    public LayoutCanvasCard(IReadOnlyList<DisplayDevice> devices, ToolTip toolTip)
    {
        CornerRadius = 16;
        BackColor = Theme.SurfaceWhite;

        _tiles = DisplayLayoutEngine.CreateTiles(devices);
        _selectedIndex = _tiles.FindIndex(t => !t.IsPrimary);
        if (_selectedIndex < 0) _selectedIndex = _tiles.Count > 0 ? 0 : -1;

        _titleLabel = new Label
        {
            Text = "Posicionamento das telas",
            AutoSize = true,
            Font = Theme.CardTitleFont,
            ForeColor = Theme.TextPrimary
        };

        _hintLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.CardSubtitleFont,
            ForeColor = Theme.TextMuted
        };

        _resetButton = new Button
        {
            Text = "Desfazer",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.SurfaceWhite,
            ForeColor = Theme.ButtonFore,
            Font = Theme.ButtonFont,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _resetButton.FlatAppearance.BorderColor = Theme.ButtonBorder;
        toolTip.SetToolTip(_resetButton, "Voltar ao posicionamento atual do Windows");

        _applyButton = new Button
        {
            Text = "Aplicar posição",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.AccentBlueDisabled,
            ForeColor = Color.White,
            Font = Theme.ButtonSemiboldFont,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _applyButton.FlatAppearance.BorderSize = 0;
        toolTip.SetToolTip(_applyButton, "Salvar a nova posição das telas no Windows");

        _viewport = new CanvasSurface
        {
            TabStop = true
        };

        _viewport.Paint += OnViewportPaint;
        _viewport.MouseDown += OnViewportMouseDown;
        _viewport.MouseMove += OnViewportMouseMove;
        _viewport.MouseUp += OnViewportMouseUp;
        _viewport.MouseLeave += (_, _) =>
        {
            if (!_isDragging && _hoveredIndex != -1)
            {
                _hoveredIndex = -1;
                _viewport.Cursor = Cursors.Default;
                _viewport.Invalidate();
            }
        };
        _viewport.KeyDown += OnViewportKeyDown;

        _resetButton.Click += (_, _) =>
        {
            DisplayLayoutEngine.ResetToInitial(_tiles);
            _activeGuides.Clear();
            UpdateActionState();
            _viewport.Invalidate();
        };

        _applyButton.Click += (_, _) =>
        {
            if (!DisplayLayoutEngine.HasPendingChanges(_tiles)) return;
            var map = DisplayLayoutEngine.BuildDevicePositionMap(_tiles);
            ApplyLayoutRequested?.Invoke(map);
        };

        Controls.AddRange([_titleLabel, _hintLabel, _resetButton, _applyButton, _viewport]);

        UpdateActionState();
        PerformCanvasCardLayout();
    }

    public void SelectByDeviceIdentity(string identityKey)
    {
        var idx = _tiles.FindIndex(t => t.Devices.Any(d => string.Equals(d.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase)));
        if (idx >= 0 && idx != _selectedIndex)
        {
            _selectedIndex = idx;
            UpdateHintText();
            _viewport.Invalidate();
        }
    }

    public void SetInteractionLocked(bool locked)
    {
        _interactionLocked = locked;
        if (locked && _isDragging)
        {
            CancelActiveDrag();
        }
        UpdateActionState();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        PerformCanvasCardLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformCanvasCardLayout();
    }

    private void PerformCanvasCardLayout()
    {
        int S(int px) => Theme.Scale(this, px);

        var targetHeight = S(286);
        if (Height != targetHeight)
        {
            Height = targetHeight;
        }
        Margin = new Padding(0, 0, 0, S(16));

        var leftPad = S(24);
        var rightEdge = Width - S(20);
        var topY = S(16);
        var btnHeight = S(34);

        _applyButton.Size = new Size(S(136), btnHeight);
        _applyButton.Location = new Point(rightEdge - _applyButton.Width, topY + S(2));
        rightEdge = _applyButton.Left - S(8);

        _resetButton.Size = new Size(S(96), btnHeight);
        _resetButton.Location = new Point(rightEdge - _resetButton.Width, topY + S(2));
        rightEdge = _resetButton.Left - S(16);

        _titleLabel.Location = new Point(leftPad, topY);
        _hintLabel.Location = new Point(leftPad + S(1), _titleLabel.Bottom + S(2));
        _hintLabel.Size = new Size(Math.Max(S(180), rightEdge - leftPad), S(22));

        var viewportTop = S(70);
        _viewport.Location = new Point(leftPad, viewportTop);
        _viewport.Size = new Size(Math.Max(S(200), Width - leftPad - S(20)), Height - viewportTop - S(18));
        _viewport.Invalidate();
    }

    private void UpdateActionState()
    {
        var dirty = DisplayLayoutEngine.HasPendingChanges(_tiles);
        var canApply = !_interactionLocked && dirty && _tiles.Count >= 2;

        _resetButton.Enabled = canApply;
        _applyButton.Enabled = canApply;
        _applyButton.BackColor = canApply ? Theme.AccentBlue : Theme.AccentBlueDisabled;

        UpdateHintText();
    }

    private void UpdateHintText()
    {
        if (_tiles.Count <= 1)
        {
            _hintLabel.Text = "Ative duas ou mais telas para arrastar e organizar o posicionamento entre elas.";
            _hintLabel.ForeColor = Theme.TextMuted;
            return;
        }

        if (_isDragging && _selectedIndex >= 0)
        {
            _hintLabel.Text = DisplayLayoutEngine.DescribeRelativePosition(_tiles, _selectedIndex);
            _hintLabel.ForeColor = Theme.AccentBlue;
            return;
        }

        if (DisplayLayoutEngine.HasPendingChanges(_tiles) && _selectedIndex >= 0)
        {
            _hintLabel.Text = $"{DisplayLayoutEngine.DescribeRelativePosition(_tiles, _selectedIndex)}  •  Clique em \"Aplicar posição\" para salvar";
            _hintLabel.ForeColor = Theme.AccentBlue;
            return;
        }

        _hintLabel.Text = "Arraste as telas para posicionar à esquerda, direita, acima ou abaixo (setas = ajuste fino; Shift = 1 px).";
        _hintLabel.ForeColor = Theme.TextMuted;
    }

    private (double Scale, PointF WorldOrigin) GetActiveTransform()
    {
        if (_isDragging)
        {
            return (_frozenScale, _frozenWorldOrigin);
        }

        var vw = Math.Max(40, _viewport.ClientSize.Width);
        var vh = Math.Max(40, _viewport.ClientSize.Height);

        if (_tiles.Count == 0)
        {
            return (0.05, new PointF(vw / 2f, vh / 2f));
        }

        var minX = _tiles.Min(t => t.Bounds.Left);
        var minY = _tiles.Min(t => t.Bounds.Top);
        var maxX = _tiles.Max(t => t.Bounds.Right);
        var maxY = _tiles.Max(t => t.Bounds.Bottom);

        double unionW = Math.Max(800, maxX - minX);
        double unionH = Math.Max(600, maxY - minY);

        // Leave generous breathing room around the cluster so dragging to any side has visual space.
        var paddedW = unionW * 1.36;
        var paddedH = unionH * 1.36;

        var scaleX = (vw - Theme.Scale(this, 32)) / paddedW;
        var scaleY = (vh - Theme.Scale(this, 24)) / paddedH;
        var scale = Math.Clamp(Math.Min(scaleX, scaleY), 0.012, 0.14);

        var centerWorldX = (minX + maxX) / 2.0;
        var centerWorldY = (minY + maxY) / 2.0;

        var originX = (float)(vw / 2.0 - centerWorldX * scale);
        var originY = (float)(vh / 2.0 - centerWorldY * scale);

        return (scale, new PointF(originX, originY));
    }

    private static RectangleF WorldToScreen(Rectangle world, double scale, PointF origin) =>
        new(
            (float)(origin.X + world.Left * scale),
            (float)(origin.Y + world.Top * scale),
            Math.Max(24f, (float)(world.Width * scale)),
            Math.Max(20f, (float)(world.Height * scale)));

    private int HitTestTile(Point mousePt, double scale, PointF origin)
    {
        // Check selected tile first (since it renders on top), then reverse order.
        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count)
        {
            var selRect = WorldToScreen(_tiles[_selectedIndex].Bounds, scale, origin);
            if (selRect.Contains(mousePt)) return _selectedIndex;
        }

        for (var i = _tiles.Count - 1; i >= 0; i--)
        {
            if (i == _selectedIndex) continue;
            var rect = WorldToScreen(_tiles[i].Bounds, scale, origin);
            if (rect.Contains(mousePt)) return i;
        }

        return -1;
    }

    private void OnViewportMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _tiles.Count == 0) return;

        _viewport.Focus();
        var (scale, origin) = GetActiveTransform();
        var hit = HitTestTile(e.Location, scale, origin);
        if (hit < 0) return;

        _selectedIndex = hit;
        TileSelected?.Invoke(_tiles[hit].Devices[0].IdentityKey);

        if (!_interactionLocked && _tiles.Count >= 2)
        {
            _frozenScale = scale;
            _frozenWorldOrigin = origin;
            _dragStartMouse = e.Location;
            _dragStartTilePos = _tiles[hit].Position;
            _isDragging = true;
            _viewport.Capture = true;
            _viewport.Cursor = Cursors.SizeAll;
        }

        UpdateHintText();
        _viewport.Invalidate();
    }

    private void OnViewportMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDragging && _selectedIndex >= 0 && _selectedIndex < _tiles.Count)
        {
            var dxWorld = (int)Math.Round((e.X - _dragStartMouse.X) / _frozenScale);
            var dyWorld = (int)Math.Round((e.Y - _dragStartMouse.Y) / _frozenScale);
            var rawPos = new Point(_dragStartTilePos.X + dxWorld, _dragStartTilePos.Y + dyWorld);

            var edgeSnapWorld = Math.Max(24, (int)Math.Round(Theme.Scale(this, 13) / _frozenScale));
            var alignSnapWorld = Math.Max(16, (int)Math.Round(Theme.Scale(this, 9) / _frozenScale));

            var (snappedPos, guides) = DisplayLayoutEngine.SnapDuringDrag(
                _tiles,
                _selectedIndex,
                rawPos,
                edgeSnapWorld,
                alignSnapWorld);

            _tiles[_selectedIndex].Position = snappedPos;
            _activeGuides = guides;
            UpdateHintText();
            _viewport.Invalidate();
            return;
        }

        var (scale, origin) = GetActiveTransform();
        var hit = HitTestTile(e.Location, scale, origin);
        if (hit != _hoveredIndex)
        {
            _hoveredIndex = hit;
            _viewport.Cursor = hit >= 0 && !_interactionLocked && _tiles.Count >= 2
                ? Cursors.SizeAll
                : hit >= 0 ? Cursors.Hand : Cursors.Default;
            _viewport.Invalidate();
        }
    }

    private void OnViewportMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isDragging || e.Button != MouseButtons.Left) return;

        _isDragging = false;
        _viewport.Capture = false;
        _activeGuides.Clear();

        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count)
        {
            DisplayLayoutEngine.CommitPlacement(_tiles, _selectedIndex, _tiles[_selectedIndex].Position);
        }

        UpdateActionState();
        _viewport.Invalidate();
    }

    private void CancelActiveDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        _viewport.Capture = false;
        _activeGuides.Clear();

        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count)
        {
            _tiles[_selectedIndex].Position = _dragStartTilePos;
        }
        UpdateActionState();
        _viewport.Invalidate();
    }

    private void OnViewportKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && _isDragging)
        {
            CancelActiveDrag();
            e.Handled = true;
            return;
        }

        if (_interactionLocked || _isDragging || _tiles.Count < 2 || _selectedIndex < 0 || _selectedIndex >= _tiles.Count)
        {
            return;
        }

        var step = e.Shift ? 1 : e.Control ? 100 : 20;
        var delta = e.KeyCode switch
        {
            Keys.Left => new Point(-step, 0),
            Keys.Right => new Point(step, 0),
            Keys.Up => new Point(0, -step),
            Keys.Down => new Point(0, step),
            _ => Point.Empty
        };

        if (delta == Point.Empty) return;

        e.Handled = true;
        var current = _tiles[_selectedIndex].Position;
        var desired = new Point(current.X + delta.X, current.Y + delta.Y);
        DisplayLayoutEngine.CommitPlacement(_tiles, _selectedIndex, desired);
        UpdateActionState();
        _viewport.Invalidate();
    }

    private void OnViewportPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = new Rectangle(0, 0, _viewport.Width - 1, _viewport.Height - 1);
        if (bounds.Width <= 4 || bounds.Height <= 4) return;

        var radius = Theme.Scale(this, 12);
        using var bgPath = Theme.CreateRoundedRect(bounds, radius);
        using var bgBrush = new SolidBrush(Color.FromArgb(241, 244, 250));
        g.FillPath(bgBrush, bgPath);

        // Clip inside rounded viewport
        var oldClip = g.Clip;
        g.SetClip(bgPath);

        // Subtle dot matrix background
        var gridStep = Theme.Scale(this, 20);
        using (var dotBrush = new SolidBrush(Color.FromArgb(218, 224, 235)))
        {
            for (var y = gridStep / 2; y < bounds.Height; y += gridStep)
            {
                for (var x = gridStep / 2; x < bounds.Width; x += gridStep)
                {
                    g.FillRectangle(dotBrush, x, y, 2, 2);
                }
            }
        }

        var (scale, origin) = GetActiveTransform();

        // Draw magnetic alignment guides while dragging
        if (_isDragging && _activeGuides.Count > 0)
        {
            using var guidePen = new Pen(Color.FromArgb(180, Theme.AccentBlue), 1.5F)
            {
                DashStyle = DashStyle.Dash
            };
            foreach (var guide in _activeGuides)
            {
                if (guide.Orientation == GuideOrientation.Vertical)
                {
                    var sx = (float)(origin.X + guide.Coordinate * scale);
                    var sy1 = (float)(origin.Y + guide.Start * scale) - Theme.Scale(this, 10);
                    var sy2 = (float)(origin.Y + guide.End * scale) + Theme.Scale(this, 10);
                    g.DrawLine(guidePen, sx, sy1, sx, sy2);
                }
                else
                {
                    var sy = (float)(origin.Y + guide.Coordinate * scale);
                    var sx1 = (float)(origin.X + guide.Start * scale) - Theme.Scale(this, 10);
                    var sx2 = (float)(origin.X + guide.End * scale) + Theme.Scale(this, 10);
                    g.DrawLine(guidePen, sx1, sy, sx2, sy);
                }
            }
        }

        // Draw stationary tiles first, then selected tile on top
        for (var i = 0; i < _tiles.Count; i++)
        {
            if (i == _selectedIndex) continue;
            DrawTile(g, _tiles[i], i, scale, origin);
        }

        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count)
        {
            DrawTile(g, _tiles[_selectedIndex], _selectedIndex, scale, origin);
        }

        g.Clip = oldClip;

        using var borderPen = new Pen(
            _viewport.Focused ? Color.FromArgb(165, 184, 248) : Theme.CardBorder,
            1F);
        g.DrawPath(borderPen, bgPath);
    }

    private void DrawTile(Graphics g, LayoutTile tile, int index, double scale, PointF origin)
    {
        var screenRectF = WorldToScreen(tile.Bounds, scale, origin);
        // Inset by 1px so adjacent touching monitors still show a crisp 2px visual seam between bezels
        var drawRect = Rectangle.Round(RectangleF.Inflate(screenRectF, -1f, -1f));
        if (drawRect.Width < 16 || drawRect.Height < 14) return;

        var isSelected = index == _selectedIndex;
        var isHovered = index == _hoveredIndex;
        var tileRadius = Theme.Scale(this, 8);

        if (_isDragging && isSelected)
        {
            var shadowRect = drawRect;
            shadowRect.Offset(0, Theme.Scale(this, 4));
            using var shadowPath = Theme.CreateRoundedRect(shadowRect, tileRadius);
            using var shadowBrush = new SolidBrush(Color.FromArgb(28, 15, 23, 42));
            g.FillPath(shadowBrush, shadowPath);
        }

        var fillColor = isSelected
            ? Color.FromArgb(_isDragging ? 222 : 235, 240, 255)
            : Color.White;

        var borderColor = isSelected
            ? Theme.AccentBlue
            : isHovered && !_interactionLocked
                ? Color.FromArgb(115, 138, 236)
                : Color.FromArgb(176, 186, 204);

        var borderWidth = isSelected ? 2F : 1.4F;

        using var path = Theme.CreateRoundedRect(drawRect, tileRadius);
        using (var fillBrush = new SolidBrush(fillColor))
        {
            g.FillPath(fillBrush, path);
        }

        // Primary monitor taskbar indicator bar along the bottom edge (like Windows 11)
        if (tile.IsPrimary && drawRect.Width >= Theme.Scale(this, 36) && drawRect.Height >= Theme.Scale(this, 28))
        {
            var barWidth = Math.Min(Theme.Scale(this, 36), drawRect.Width / 2);
            var barHeight = Math.Max(3, Theme.Scale(this, 3));
            var barRect = new Rectangle(
                drawRect.Left + (drawRect.Width - barWidth) / 2,
                drawRect.Bottom - barHeight - Theme.Scale(this, 5),
                barWidth,
                barHeight);
            using var barBrush = new SolidBrush(Theme.AccentBlue);
            g.FillRectangle(barBrush, barRect);
        }

        using (var pen = new Pen(borderColor, borderWidth))
        {
            g.DrawPath(pen, path);
        }

        // Badge number + optional monitor name if space permits
        var showTitle = drawRect.Height >= Theme.Scale(this, 54) && drawRect.Width >= Theme.Scale(this, 56);
        var numberColor = isSelected ? Theme.PrimaryBadgeFore : Theme.TextPrimary;

        if (showTitle)
        {
            var numRect = new Rectangle(
                drawRect.Left + 4,
                drawRect.Top + Theme.Scale(this, 6),
                drawRect.Width - 8,
                (int)(drawRect.Height * 0.54));

            TextRenderer.DrawText(
                g,
                tile.BadgeText,
                Theme.CanvasNumberFont,
                numRect,
                numberColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);

            var titleRect = new Rectangle(
                drawRect.Left + Theme.Scale(this, 6),
                numRect.Bottom + Theme.Scale(this, 1),
                drawRect.Width - Theme.Scale(this, 12),
                Theme.Scale(this, 18));

            TextRenderer.DrawText(
                g,
                tile.Title,
                Theme.CanvasSmallFont,
                titleRect,
                Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
        else
        {
            TextRenderer.DrawText(
                g,
                tile.BadgeText,
                Theme.CanvasNumberFont,
                drawRect,
                numberColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private sealed class CanvasSurface : Control
    {
        public CanvasSurface()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Escape)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }
    }
}
