using MonitorControl.Models;

namespace MonitorControl.Services;

internal enum GuideOrientation
{
    Vertical,
    Horizontal
}

internal readonly record struct AlignmentGuide(
    GuideOrientation Orientation,
    int Coordinate,
    int Start,
    int End);

internal sealed class LayoutTile
{
    public required string Key { get; init; }
    public required IReadOnlyList<DisplayDevice> Devices { get; init; }
    public required string BadgeText { get; init; }
    public required string Title { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool IsPrimary { get; init; }
    public Point InitialPosition { get; set; }

    public Point Position { get; set; }

    public Rectangle Bounds => new(Position.X, Position.Y, Width, Height);
    public Rectangle InitialBounds => new(InitialPosition.X, InitialPosition.Y, Width, Height);
}

internal static class DisplayLayoutEngine
{
    private const int DefaultMinSharedEdge = 80;

    public static List<LayoutTile> CreateTiles(IEnumerable<DisplayDevice> devices)
    {
        var active = devices
            .Where(d => d.IsActive && d.CurrentMode is not null && d.CurrentMode.Width > 0 && d.CurrentMode.Height > 0)
            .ToList();

        if (active.Count == 0) return [];

        // Group monitors that share the exact same desktop rectangle (Clone / Duplicate mode) into one tile.
        var groups = active
            .GroupBy(d => (
                d.Position.X,
                d.Position.Y,
                Width: (int)d.CurrentMode!.Width,
                Height: (int)d.CurrentMode!.Height))
            .ToList();

        var tiles = new List<LayoutTile>(groups.Count);
        foreach (var group in groups)
        {
            var members = group.OrderBy(d => d.DisplayNumber).ToList();
            var badge = string.Join(" | ", members.Select(m => m.DisplayNumber));
            var title = members.Count == 1
                ? members[0].DisplayTitle
                : string.Join(" / ", members.Select(m => m.DisplayTitle));
            var isPrimary = members.Any(m => m.IsPrimary);
            var pos = new Point(group.Key.X, group.Key.Y);

            tiles.Add(new LayoutTile
            {
                Key = members[0].IdentityKey,
                Devices = members,
                BadgeText = badge,
                Title = title,
                Width = group.Key.Width,
                Height = group.Key.Height,
                IsPrimary = isPrimary,
                InitialPosition = pos,
                Position = pos
            });
        }

        NormalizeToPrimaryOrigin(tiles, updateInitial: true);
        return tiles;
    }

    public static bool HasPendingChanges(IReadOnlyList<LayoutTile> tiles) =>
        tiles.Any(t => t.Position != t.InitialPosition);

    public static void ResetToInitial(IReadOnlyList<LayoutTile> tiles)
    {
        foreach (var tile in tiles)
        {
            tile.Position = tile.InitialPosition;
        }
    }

    public static Dictionary<string, Point> BuildDevicePositionMap(IReadOnlyList<LayoutTile> tiles)
    {
        var clone = tiles.Select(t => new LayoutTile
        {
            Key = t.Key,
            Devices = t.Devices,
            BadgeText = t.BadgeText,
            Title = t.Title,
            Width = t.Width,
            Height = t.Height,
            IsPrimary = t.IsPrimary,
            InitialPosition = t.InitialPosition,
            Position = t.Position
        }).ToList();

        NormalizeToPrimaryOrigin(clone, updateInitial: false);

        var map = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in clone)
        {
            foreach (var device in tile.Devices)
            {
                map[device.IdentityKey] = tile.Position;
            }
        }
        return map;
    }

    /// <summary>
    /// Applies magnetic edge and alignment snapping while the user is actively dragging a tile.
    /// Does not block movement across other monitors so dragging from one side to the opposite side feels smooth.
    /// </summary>
    public static (Point SnappedPosition, List<AlignmentGuide> Guides) SnapDuringDrag(
        IReadOnlyList<LayoutTile> tiles,
        int draggedIndex,
        Point rawPosition,
        int edgeSnapThreshold,
        int alignSnapThreshold)
    {
        var guides = new List<AlignmentGuide>();
        if (draggedIndex < 0 || draggedIndex >= tiles.Count || tiles.Count <= 1)
        {
            return (rawPosition, guides);
        }

        var moving = tiles[draggedIndex];
        var snappedX = rawPosition.X;
        var snappedY = rawPosition.Y;

        int bestDx = edgeSnapThreshold + 1;
        int bestDy = edgeSnapThreshold + 1;
        AlignmentGuide? guideX = null;
        AlignmentGuide? guideY = null;

        // Pass 1: Edge-to-edge docking snap (Left<->Right, Top<->Bottom)
        for (var i = 0; i < tiles.Count; i++)
        {
            if (i == draggedIndex) continue;
            var other = tiles[i].Bounds;

            var nearVertically =
                rawPosition.Y <= other.Bottom + edgeSnapThreshold * 2 &&
                rawPosition.Y + moving.Height >= other.Top - edgeSnapThreshold * 2;

            if (nearVertically)
            {
                // moving.Right -> other.Left
                var targetLeftSide = other.Left - moving.Width;
                var distLeft = Math.Abs(rawPosition.X - targetLeftSide);
                if (distLeft < bestDx)
                {
                    bestDx = distLeft;
                    snappedX = targetLeftSide;
                    guideX = new AlignmentGuide(
                        GuideOrientation.Vertical,
                        other.Left,
                        Math.Min(rawPosition.Y, other.Top),
                        Math.Max(rawPosition.Y + moving.Height, other.Bottom));
                }

                // moving.Left -> other.Right
                var targetRightSide = other.Right;
                var distRight = Math.Abs(rawPosition.X - targetRightSide);
                if (distRight < bestDx)
                {
                    bestDx = distRight;
                    snappedX = targetRightSide;
                    guideX = new AlignmentGuide(
                        GuideOrientation.Vertical,
                        other.Right,
                        Math.Min(rawPosition.Y, other.Top),
                        Math.Max(rawPosition.Y + moving.Height, other.Bottom));
                }
            }

            var nearHorizontally =
                rawPosition.X <= other.Right + edgeSnapThreshold * 2 &&
                rawPosition.X + moving.Width >= other.Left - edgeSnapThreshold * 2;

            if (nearHorizontally)
            {
                // moving.Bottom -> other.Top
                var targetTopSide = other.Top - moving.Height;
                var distTop = Math.Abs(rawPosition.Y - targetTopSide);
                if (distTop < bestDy)
                {
                    bestDy = distTop;
                    snappedY = targetTopSide;
                    guideY = new AlignmentGuide(
                        GuideOrientation.Horizontal,
                        other.Top,
                        Math.Min(rawPosition.X, other.Left),
                        Math.Max(rawPosition.X + moving.Width, other.Right));
                }

                // moving.Top -> other.Bottom
                var targetBottomSide = other.Bottom;
                var distBottom = Math.Abs(rawPosition.Y - targetBottomSide);
                if (distBottom < bestDy)
                {
                    bestDy = distBottom;
                    snappedY = targetBottomSide;
                    guideY = new AlignmentGuide(
                        GuideOrientation.Horizontal,
                        other.Bottom,
                        Math.Min(rawPosition.X, other.Left),
                        Math.Max(rawPosition.X + moving.Width, other.Right));
                }
            }
        }

        // Pass 2: Parallel edge / center alignment guides on the axis that didn't dock-snap
        if (guideX is null)
        {
            var bestAlignDx = alignSnapThreshold + 1;
            for (var i = 0; i < tiles.Count; i++)
            {
                if (i == draggedIndex) continue;
                var other = tiles[i].Bounds;

                CheckVerticalAlign(rawPosition.X, other.Left, other.Left);
                CheckVerticalAlign(rawPosition.X, other.Right - moving.Width, other.Right);
                CheckVerticalAlign(rawPosition.X, other.Left + (other.Width - moving.Width) / 2, other.Left + other.Width / 2);

                void CheckVerticalAlign(int rawX, int candidateX, int lineX)
                {
                    var d = Math.Abs(rawX - candidateX);
                    if (d < bestAlignDx)
                    {
                        bestAlignDx = d;
                        snappedX = candidateX;
                        guideX = new AlignmentGuide(
                            GuideOrientation.Vertical,
                            lineX,
                            Math.Min(snappedY, other.Top),
                            Math.Max(snappedY + moving.Height, other.Bottom));
                    }
                }
            }
        }

        if (guideY is null)
        {
            var bestAlignDy = alignSnapThreshold + 1;
            for (var i = 0; i < tiles.Count; i++)
            {
                if (i == draggedIndex) continue;
                var other = tiles[i].Bounds;

                CheckHorizontalAlign(rawPosition.Y, other.Top, other.Top);
                CheckHorizontalAlign(rawPosition.Y, other.Bottom - moving.Height, other.Bottom);
                CheckHorizontalAlign(rawPosition.Y, other.Top + (other.Height - moving.Height) / 2, other.Top + other.Height / 2);

                void CheckHorizontalAlign(int rawY, int candidateY, int lineY)
                {
                    var d = Math.Abs(rawY - candidateY);
                    if (d < bestAlignDy)
                    {
                        bestAlignDy = d;
                        snappedY = candidateY;
                        guideY = new AlignmentGuide(
                            GuideOrientation.Horizontal,
                            lineY,
                            Math.Min(snappedX, other.Left),
                            Math.Max(snappedX + moving.Width, other.Right));
                    }
                }
            }
        }

        if (guideX.HasValue) guides.Add(guideX.Value);
        if (guideY.HasValue) guides.Add(guideY.Value);

        return (new Point(snappedX, snappedY), guides);
    }

    /// <summary>
    /// Resolves a dropped or nudged tile position so that:
    /// 1. No two tiles overlap.
    /// 2. All tiles form a single contiguous desktop with shared passable edges.
    /// 3. The primary tile remains anchored at (0, 0).
    /// </summary>
    public static void CommitPlacement(IReadOnlyList<LayoutTile> tiles, int movedIndex, Point desiredPosition)
    {
        if (tiles.Count == 0) return;
        if (tiles.Count == 1)
        {
            tiles[0].Position = Point.Empty;
            return;
        }

        var moved = tiles[movedIndex];
        var stationary = tiles.Where((_, idx) => idx != movedIndex).ToList();

        moved.Position = FindNearestValidPosition(moved.Width, moved.Height, desiredPosition, stationary);

        // Ensure multi-monitor (3+) connectivity so no monitor is left stranded as a disconnected island.
        EnsureSingleConnectedCluster(tiles, movedIndex);

        NormalizeToPrimaryOrigin(tiles, updateInitial: false);
    }

    public static string DescribeRelativePosition(IReadOnlyList<LayoutTile> tiles, int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= tiles.Count) return string.Empty;
        var tile = tiles[targetIndex];
        if (tiles.Count == 1) return $"{tile.BadgeText}. {tile.Title} (0, 0)";

        var primary = tiles.FirstOrDefault(t => t.IsPrimary) ?? tiles[0];
        if (ReferenceEquals(tile, primary))
        {
            return $"{tile.BadgeText}. {tile.Title} • Tela principal ({tile.Position.X}, {tile.Position.Y})";
        }

        var dx = (tile.Bounds.Left + tile.Width / 2) - (primary.Bounds.Left + primary.Width / 2);
        var dy = (tile.Bounds.Top + tile.Height / 2) - (primary.Bounds.Top + primary.Height / 2);

        string direction;
        if (tile.Bounds.Right <= primary.Bounds.Left)
            direction = tile.Bounds.Bottom <= primary.Bounds.Top ? "Superior esquerda"
                      : tile.Bounds.Top >= primary.Bounds.Bottom ? "Inferior esquerda"
                      : "À esquerda";
        else if (tile.Bounds.Left >= primary.Bounds.Right)
            direction = tile.Bounds.Bottom <= primary.Bounds.Top ? "Superior direita"
                      : tile.Bounds.Top >= primary.Bounds.Bottom ? "Inferior direita"
                      : "À direita";
        else if (tile.Bounds.Bottom <= primary.Bounds.Top)
            direction = "Acima";
        else if (tile.Bounds.Top >= primary.Bounds.Bottom)
            direction = "Abaixo";
        else
            direction = Math.Abs(dx) >= Math.Abs(dy)
                ? (dx < 0 ? "À esquerda" : "À direita")
                : (dy < 0 ? "Acima" : "Abaixo");

        return $"{tile.BadgeText}. {tile.Title} • {direction} da principal ({tile.Position.X}, {tile.Position.Y})";
    }

    private static Point FindNearestValidPosition(
        int width,
        int height,
        Point desired,
        IReadOnlyList<LayoutTile> obstacles)
    {
        var candidateRect = new Rectangle(desired.X, desired.Y, width, height);
        if (IsValidPlacement(candidateRect, obstacles))
        {
            return desired;
        }

        Point? bestPoint = null;
        var bestDistSq = double.MaxValue;

        foreach (var obs in obstacles)
        {
            var b = obs.Bounds;
            var minOverlapY = GetMinOverlap(height, b.Height);
            var minOverlapX = GetMinOverlap(width, b.Width);

            // 1. Left of obstacle: x = b.Left - width
            EvaluateVerticalSegment(
                fixedX: b.Left - width,
                yMin: b.Top - height + minOverlapY,
                yMax: b.Bottom - minOverlapY);

            // 2. Right of obstacle: x = b.Right
            EvaluateVerticalSegment(
                fixedX: b.Right,
                yMin: b.Top - height + minOverlapY,
                yMax: b.Bottom - minOverlapY);

            // 3. Top of obstacle: y = b.Top - height
            EvaluateHorizontalSegment(
                fixedY: b.Top - height,
                xMin: b.Left - width + minOverlapX,
                xMax: b.Right - minOverlapX);

            // 4. Bottom of obstacle: y = b.Bottom
            EvaluateHorizontalSegment(
                fixedY: b.Bottom,
                xMin: b.Left - width + minOverlapX,
                xMax: b.Right - minOverlapX);
        }

        return bestPoint ?? new Point(obstacles[0].Bounds.Right, obstacles[0].Bounds.Top);

        void EvaluateVerticalSegment(int fixedX, int yMin, int yMax)
        {
            if (yMin > yMax) yMin = yMax = (yMin + yMax) / 2;
            var intervals = new List<(int Start, int End)> { (yMin, yMax) };

            foreach (var other in obstacles)
            {
                var ob = other.Bounds;
                // If horizontally overlapping with 'other', subtract vertical collision open interval (ob.Top - height + 1 .. ob.Bottom - 1)
                if (fixedX < ob.Right && fixedX + width > ob.Left)
                {
                    intervals = SubtractInterval(intervals, ob.Top - height + 1, ob.Bottom - 1);
                    if (intervals.Count == 0) return;
                }
            }

            foreach (var (start, end) in intervals)
            {
                var clampedY = Math.Clamp(desired.Y, start, end);
                ConsiderCandidate(new Point(fixedX, clampedY));
            }
        }

        void EvaluateHorizontalSegment(int fixedY, int xMin, int xMax)
        {
            if (xMin > xMax) xMin = xMax = (xMin + xMax) / 2;
            var intervals = new List<(int Start, int End)> { (xMin, xMax) };

            foreach (var other in obstacles)
            {
                var ob = other.Bounds;
                // If vertically overlapping with 'other', subtract horizontal collision open interval (ob.Left - width + 1 .. ob.Right - 1)
                if (fixedY < ob.Bottom && fixedY + height > ob.Top)
                {
                    intervals = SubtractInterval(intervals, ob.Left - width + 1, ob.Right - 1);
                    if (intervals.Count == 0) return;
                }
            }

            foreach (var (start, end) in intervals)
            {
                var clampedX = Math.Clamp(desired.X, start, end);
                ConsiderCandidate(new Point(clampedX, fixedY));
            }
        }

        void ConsiderCandidate(Point pt)
        {
            var rect = new Rectangle(pt.X, pt.Y, width, height);
            if (!IsValidPlacement(rect, obstacles)) return;

            double dx = pt.X - desired.X;
            double dy = pt.Y - desired.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestPoint = pt;
            }
        }
    }

    private static List<(int Start, int End)> SubtractInterval(
        List<(int Start, int End)> source,
        int forbiddenStart,
        int forbiddenEnd)
    {
        if (forbiddenStart > forbiddenEnd) return source;
        var result = new List<(int Start, int End)>(source.Count + 1);

        foreach (var (s, e) in source)
        {
            if (forbiddenEnd < s || forbiddenStart > e)
            {
                result.Add((s, e));
                continue;
            }

            if (forbiddenStart > s)
            {
                result.Add((s, forbiddenStart - 1));
            }
            if (forbiddenEnd < e)
            {
                result.Add((forbiddenEnd + 1, e));
            }
        }

        return result;
    }

    private static void EnsureSingleConnectedCluster(IReadOnlyList<LayoutTile> tiles, int movedIndex)
    {
        if (tiles.Count <= 2) return;

        var anchorIndex = tiles.ToList().FindIndex(t => t.IsPrimary);
        if (anchorIndex < 0) anchorIndex = 0;

        var connected = new HashSet<int> { anchorIndex };
        var queue = new Queue<int>();
        queue.Enqueue(anchorIndex);

        void ExpandCluster()
        {
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (var i = 0; i < tiles.Count; i++)
                {
                    if (connected.Contains(i)) continue;
                    if (SharesValidEdge(tiles[current].Bounds, tiles[i].Bounds))
                    {
                        connected.Add(i);
                        queue.Enqueue(i);
                    }
                }
            }
        }

        ExpandCluster();

        while (connected.Count < tiles.Count)
        {
            // Prefer docking non-moved orphans onto the main cluster; if the anchor itself was isolated, dock remaining tiles in order.
            var nextOrphan = Enumerable.Range(0, tiles.Count)
                .Where(i => !connected.Contains(i))
                .OrderBy(i => i == movedIndex ? 0 : 1)
                .First();

            var clusterTiles = connected.Select(idx => tiles[idx]).ToList();
            var orphan = tiles[nextOrphan];
            orphan.Position = FindNearestValidPosition(orphan.Width, orphan.Height, orphan.Position, clusterTiles);

            connected.Add(nextOrphan);
            queue.Enqueue(nextOrphan);
            ExpandCluster();
        }
    }

    private static bool IsValidPlacement(Rectangle candidate, IReadOnlyList<LayoutTile> obstacles)
    {
        var touchesAny = false;
        foreach (var obs in obstacles)
        {
            var b = obs.Bounds;
            if (candidate.IntersectsWith(b)) return false;
            if (SharesValidEdge(candidate, b)) touchesAny = true;
        }
        return touchesAny;
    }

    public static bool SharesValidEdge(Rectangle a, Rectangle b)
    {
        if (a.Right == b.Left || a.Left == b.Right)
        {
            var overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            return overlapY >= GetMinOverlap(a.Height, b.Height);
        }

        if (a.Bottom == b.Top || a.Top == b.Bottom)
        {
            var overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            return overlapX >= GetMinOverlap(a.Width, b.Width);
        }

        return false;
    }

    private static int GetMinOverlap(int sizeA, int sizeB) =>
        Math.Min(DefaultMinSharedEdge, Math.Max(1, Math.Min(sizeA, sizeB) / 4));

    public static void NormalizeToPrimaryOrigin(IReadOnlyList<LayoutTile> tiles, bool updateInitial)
    {
        if (tiles.Count == 0) return;

        var primary = tiles.FirstOrDefault(t => t.IsPrimary) ?? tiles[0];
        var offsetX = primary.Position.X;
        var offsetY = primary.Position.Y;

        if (offsetX != 0 || offsetY != 0)
        {
            foreach (var tile in tiles)
            {
                tile.Position = new Point(tile.Position.X - offsetX, tile.Position.Y - offsetY);
            }
        }

        if (updateInitial)
        {
            var initOx = primary.InitialPosition.X;
            var initOy = primary.InitialPosition.Y;
            if (initOx != 0 || initOy != 0)
            {
                foreach (var tile in tiles)
                {
                    tile.InitialPosition = new Point(tile.InitialPosition.X - initOx, tile.InitialPosition.Y - initOy);
                }
            }
        }
    }
}
