using System.Diagnostics;
using System.Runtime.InteropServices;
using MonitorControl.Models;
using MonitorControl.Native;

namespace MonitorControl.Services;

internal static unsafe class DisplayService
{
    public static List<DisplayDevice> GetDevices()
    {
        var (paths, modes) = QueryAllPaths();
        var primaryGdi = Screen.PrimaryScreen?.DeviceName;

        var groupedTargets = paths
            .Where(p => p.Target.TargetAvailable != 0)
            .GroupBy(p => TargetKey(p.Target.AdapterId, p.Target.Id));

        var rawDevices = new List<DisplayDevice>();

        foreach (var group in groupedTargets)
        {
            var key = group.Key;
            // Always prefer the active path for this target so Source.Id / GdiName matches the real active display.
            var bestPath = group
                .OrderByDescending(p => (p.Flags & DisplayNative.PathActive) != 0 ? 1 : 0)
                .First();

            var isActive = (bestPath.Flags & DisplayNative.PathActive) != 0;
            var (friendlyName, monitorPath) = GetTargetName(bestPath.Target.AdapterId, bestPath.Target.Id);
            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                friendlyName = $"Monitor {bestPath.Target.Id}";
            }

            var connector = DisplayNative.DescribeConnector(bestPath.Target.OutputTechnology);
            var identityKey = string.IsNullOrWhiteSpace(monitorPath) ? key : monitorPath;

            var gdiName = string.Empty;
            var isPrimary = false;
            var position = Point.Empty;
            DisplayMode? currentMode = null;
            IReadOnlyList<DisplayMode> availableModes = [];

            if (isActive)
            {
                gdiName = GetSourceName(bestPath.Source.AdapterId, bestPath.Source.Id);
                if (!string.IsNullOrWhiteSpace(gdiName))
                {
                    (currentMode, position) = GetCurrentModeAndPosition(gdiName);
                    availableModes = GetModes(gdiName);

                    if (TryGetSourcePosition(in bestPath, modes, out var sourcePos))
                    {
                        position = sourcePos;
                        isPrimary = sourcePos.X == 0 && sourcePos.Y == 0;
                    }
                    else
                    {
                        isPrimary = SameGdi(gdiName, primaryGdi) || (position.X == 0 && position.Y == 0);
                    }
                }
            }

            rawDevices.Add(new DisplayDevice(
                AdapterLowPart: bestPath.Target.AdapterId.LowPart,
                AdapterHighPart: bestPath.Target.AdapterId.HighPart,
                TargetId: bestPath.Target.Id,
                IdentityKey: identityKey,
                GdiName: gdiName,
                MonitorName: friendlyName,
                DisplayTitle: friendlyName,
                DisplayNumber: 0,
                ConnectorName: connector,
                IsActive: isActive,
                IsPrimary: isPrimary,
                Position: position,
                CurrentMode: currentMode,
                AvailableModes: availableModes));
        }

        // Stable ordering so cards do not jump around when toggled or made primary.
        var ordered = rawDevices
            .OrderBy(d => d.MonitorName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.AdapterHighPart)
            .ThenBy(d => d.AdapterLowPart)
            .ThenBy(d => d.TargetId)
            .ToList();

        var nameCounts = ordered
            .GroupBy(d => d.MonitorName, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);

        var seenByName = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var finalDevices = new List<DisplayDevice>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var dev = ordered[i];
            var displayNum = i + 1;
            var title = dev.MonitorName;

            if (nameCounts.TryGetValue(dev.MonitorName, out var count) && count > 1)
            {
                seenByName.TryGetValue(dev.MonitorName, out var occurrence);
                occurrence++;
                seenByName[dev.MonitorName] = occurrence;
                title = $"{dev.MonitorName} #{occurrence}";
            }

            finalDevices.Add(dev with
            {
                DisplayNumber = displayNum,
                DisplayTitle = title
            });
        }

        return finalDevices;
    }

    public static DisplayResult SetEnabled(DisplayDevice device, bool enabled)
    {
        const string action = "alterar o estado deste monitor";
        try
        {
            var (allPaths, modes) = QueryAllPaths();
            var targetKey = TargetKey(new DisplayNative.DisplayLuid
            {
                LowPart = device.AdapterLowPart,
                HighPart = device.AdapterHighPart
            }, device.TargetId);

            var availableTargetKeys = allPaths
                .Where(p => p.Target.TargetAvailable != 0)
                .Select(p => TargetKey(p.Target.AdapterId, p.Target.Id))
                .ToHashSet();

            var activePaths = allPaths.Where(p => (p.Flags & DisplayNative.PathActive) != 0).ToList();
            var activeTargetKeys = activePaths
                .Select(p => TargetKey(p.Target.AdapterId, p.Target.Id))
                .ToHashSet();

            var isCurrentlyActive = activeTargetKeys.Contains(targetKey);
            if (enabled == isCurrentlyActive) return DisplayResult.Ok();

            if (!enabled)
            {
                if (activeTargetKeys.Count <= 1)
                {
                    return DisplayResult.Fail("Não é possível desativar o único monitor ativo.");
                }

                var paths = allPaths.ToArray();
                for (var i = 0; i < paths.Length; i++)
                {
                    if (TargetKey(paths[i].Target.AdapterId, paths[i].Target.Id) == targetKey)
                    {
                        paths[i].Flags &= ~DisplayNative.PathActive;
                    }
                }

                var modesCopy = (DisplayNative.DisplayModeInfo[])modes.Clone();
                NormalizeLayout(paths, modesCopy);
                var result = Apply(paths, modesCopy, DisplayNative.ApplyFlags);
                if (result == 0) return DisplayResult.Ok();

                // Retry passing only the surviving active paths.
                var survivingActive = paths.Where(p => (p.Flags & DisplayNative.PathActive) != 0).ToArray();
                var modesRetry = (DisplayNative.DisplayModeInfo[])modes.Clone();
                NormalizeLayout(survivingActive, modesRetry);
                result = Apply(survivingActive, modesRetry, DisplayNative.ApplyFlags);
                return DisplayResult.FromWin32(result, action);
            }
            else
            {
                var candidates = ActivateCandidates(allPaths, activePaths, targetKey);
                if (candidates.Count == 0)
                {
                    return DisplayResult.Fail("O monitor selecionado não está disponível para ativação.");
                }

                var chosen = allPaths[candidates[0]];
                chosen.Flags = (chosen.Flags | DisplayNative.PathActive) & ~DisplayNative.PathSupportVirtualMode;
                chosen.Source.ModeInfoIdx = DisplayNative.InvalidModeIndex;
                chosen.Target.ModeInfoIdx = DisplayNative.InvalidModeIndex;

                // Attempt 1: Pass existing active paths (preserving their modes) + newly activated path.
                var desiredActive = activePaths.Append(chosen).ToArray();
                var result = Apply(desiredActive, modes, DisplayNative.ApplyFlags);
                if (result == 0) return DisplayResult.Ok();

                // Attempt 2: Ask Windows to resolve modes from its database for specifically the desired active targets.
                var freshPaths = desiredActive.Select(p =>
                {
                    p.Flags = DisplayNative.PathActive;
                    p.Source.ModeInfoIdx = DisplayNative.InvalidModeIndex;
                    p.Target.ModeInfoIdx = DisplayNative.InvalidModeIndex;
                    return p;
                }).ToArray();

                result = DisplayNative.SetDisplayConfig((uint)freshPaths.Length, freshPaths, 0, null, DisplayNative.ApplyBasicFlags);
                if (result == 0) return DisplayResult.Ok();

                // Attempt 3: Only use global Extend topology fallback if ALL connected monitors are supposed to become active
                // (prevents turning on a 3rd disabled monitor unintentionally).
                if (availableTargetKeys.Count <= activeTargetKeys.Count + 1)
                {
                    result = DisplayNative.SetDisplayConfig(0, null, 0, null, DisplayNative.SdcApply | DisplayNative.SdcTopologyExtend);
                    if (result == 0) return DisplayResult.Ok();
                    if (EnableWithWindowsDisplaySwitch() == 0) return DisplayResult.Ok();
                }

                return DisplayResult.FromWin32(result, action);
            }
        }
        catch (InvalidOperationException ex)
        {
            return DisplayResult.Fail($"Erro ao consultar a configuração das telas: {ex.Message}");
        }
    }

    public static DisplayResult SetSolo(DisplayDevice device)
    {
        const string action = "deixar apenas esta tela ativa";
        try
        {
            var (allPaths, modes) = QueryAllPaths();
            var targetKey = TargetKey(new DisplayNative.DisplayLuid
            {
                LowPart = device.AdapterLowPart,
                HighPart = device.AdapterHighPart
            }, device.TargetId);

            var activePaths = allPaths.Where(p => (p.Flags & DisplayNative.PathActive) != 0).ToList();
            if (activePaths.Count == 0)
            {
                return DisplayResult.Fail("Nenhuma tela ativa encontrada para referência.");
            }

            var paths = allPaths.ToArray();
            var activeIndexes = Enumerable.Range(0, paths.Length)
                .Where(i => (paths[i].Flags & DisplayNative.PathActive) != 0)
                .ToList();
            var keepIndexes = activeIndexes
                .Where(i => TargetKey(paths[i].Target.AdapterId, paths[i].Target.Id) == targetKey)
                .ToList();

            var activatedFromInactive = false;
            if (keepIndexes.Count == 0)
            {
                var candidates = ActivateCandidates(paths, activePaths, targetKey);
                if (candidates.Count == 0)
                {
                    return DisplayResult.Fail("O monitor selecionado não está disponível para ativação.");
                }

                var chosen = candidates[0];
                paths[chosen].Flags &= ~DisplayNative.PathSupportVirtualMode;
                paths[chosen].Source.ModeInfoIdx = DisplayNative.InvalidModeIndex;
                paths[chosen].Target.ModeInfoIdx = DisplayNative.InvalidModeIndex;
                keepIndexes.Add(chosen);
                activatedFromInactive = true;
            }
            else if (keepIndexes.Count == activeIndexes.Count)
            {
                return DisplayResult.Ok();
            }

            var keepSet = keepIndexes.ToHashSet();
            for (var i = 0; i < paths.Length; i++)
            {
                if (keepSet.Contains(i)) paths[i].Flags |= DisplayNative.PathActive;
                else paths[i].Flags &= ~DisplayNative.PathActive;
            }

            NormalizeLayout(paths, modes);
            var soloPaths = keepIndexes.Select(i => paths[i]).ToArray();

            var result = activatedFromInactive
                ? DisplayNative.SetDisplayConfig((uint)soloPaths.Length, soloPaths, 0, null, DisplayNative.ApplyBasicFlags)
                : Apply(soloPaths, modes, DisplayNative.ApplyFlags);

            if (result != 0)
            {
                result = Apply(paths, modes, DisplayNative.ApplyFlags);
            }

            if (result != 0)
            {
                for (var i = 0; i < soloPaths.Length; i++)
                {
                    soloPaths[i].Flags = DisplayNative.PathActive;
                    soloPaths[i].Source.ModeInfoIdx = DisplayNative.InvalidModeIndex;
                    soloPaths[i].Target.ModeInfoIdx = DisplayNative.InvalidModeIndex;
                }
                result = DisplayNative.SetDisplayConfig((uint)soloPaths.Length, soloPaths, 0, null, DisplayNative.ApplyBasicFlags);
            }

            return DisplayResult.FromWin32(result, action);
        }
        catch (InvalidOperationException ex)
        {
            return DisplayResult.Fail($"Erro ao consultar a configuração das telas: {ex.Message}");
        }
    }

    public static DisplayResult SetPrimary(DisplayDevice device)
    {
        const string action = "definir este monitor como tela principal";
        if (!device.IsActive || string.IsNullOrWhiteSpace(device.GdiName))
        {
            return DisplayResult.Fail("Ative o monitor antes de defini-lo como tela principal.");
        }

        try
        {
            var (allPaths, modes) = QueryAllPaths();
            var targetKey = TargetKey(new DisplayNative.DisplayLuid
            {
                LowPart = device.AdapterLowPart,
                HighPart = device.AdapterHighPart
            }, device.TargetId);

            var activePaths = allPaths.Where(p => (p.Flags & DisplayNative.PathActive) != 0).ToArray();
            var targetPathIndex = Array.FindIndex(activePaths, p => TargetKey(p.Target.AdapterId, p.Target.Id) == targetKey);

            if (targetPathIndex >= 0 && TryGetSourcePosition(in activePaths[targetPathIndex], modes, out var origin))
            {
                if (origin.X == 0 && origin.Y == 0) return DisplayResult.Ok();

                ShiftAllActiveSources(activePaths, modes, -origin.X, -origin.Y);
                var result = Apply(activePaths, modes, DisplayNative.ApplyFlags);
                if (result == 0) return DisplayResult.Ok();
            }

            // Fallback via GDI ChangeDisplaySettingsEx
            return SetPrimaryViaGdi(device.GdiName, action);
        }
        catch (InvalidOperationException)
        {
            return SetPrimaryViaGdi(device.GdiName, action);
        }
    }

    public static DisplayResult SetPositions(
        IReadOnlyDictionary<string, Point> positionsByIdentity,
        IReadOnlyList<DisplayDevice> devices)
    {
        const string action = "aplicar o novo posicionamento das telas";
        var activeDevices = devices.Where(d => d.IsActive && positionsByIdentity.ContainsKey(d.IdentityKey)).ToList();
        if (activeDevices.Count == 0)
        {
            return DisplayResult.Fail("Nenhum monitor ativo disponível para reposicionar.");
        }

        // Guarantee primary monitor remains anchored at (0, 0).
        var primaryDevice = activeDevices.FirstOrDefault(d => d.IsPrimary) ?? activeDevices[0];
        var primaryOrigin = positionsByIdentity[primaryDevice.IdentityKey];
        var normalizedByTargetKey = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        var normalizedByGdi = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);

        foreach (var dev in activeDevices)
        {
            var raw = positionsByIdentity[dev.IdentityKey];
            var norm = new Point(raw.X - primaryOrigin.X, raw.Y - primaryOrigin.Y);
            var tKey = TargetKey(new DisplayNative.DisplayLuid
            {
                LowPart = dev.AdapterLowPart,
                HighPart = dev.AdapterHighPart
            }, dev.TargetId);

            normalizedByTargetKey[tKey] = norm;
            if (!string.IsNullOrWhiteSpace(dev.GdiName))
            {
                normalizedByGdi[dev.GdiName] = norm;
            }
        }

        try
        {
            var (allPaths, modes) = QueryAllPaths();
            var activePaths = allPaths.Where(p => (p.Flags & DisplayNative.PathActive) != 0).ToArray();

            if (activePaths.Length > 0 && modes.Length > 0)
            {
                var modesCopy = (DisplayNative.DisplayModeInfo[])modes.Clone();
                if (WriteTargetPositionsToModes(activePaths, modesCopy, normalizedByTargetKey))
                {
                    var result = Apply(activePaths, modesCopy, DisplayNative.ApplyFlags);
                    if (result == 0) return DisplayResult.Ok();

                    var allModesCopy = (DisplayNative.DisplayModeInfo[])modes.Clone();
                    WriteTargetPositionsToModes(allPaths, allModesCopy, normalizedByTargetKey);
                    result = Apply(allPaths, allModesCopy, DisplayNative.ApplyFlags);
                    if (result == 0) return DisplayResult.Ok();
                }
            }

            return SetPositionsViaGdi(normalizedByGdi, primaryDevice.GdiName, action);
        }
        catch (InvalidOperationException)
        {
            return SetPositionsViaGdi(normalizedByGdi, primaryDevice.GdiName, action);
        }
    }

    public static DisplayResult RestoreAll()
    {
        const string action = "restabelecer todas as telas";
        try
        {
            var result = DisplayNative.SetDisplayConfig(0, null, 0, null, DisplayNative.SdcApply | DisplayNative.SdcTopologyExtend);
            if (result == 0) return DisplayResult.Ok();
            if (EnableWithWindowsDisplaySwitch() == 0) return DisplayResult.Ok();
            return DisplayResult.FromWin32(result, action);
        }
        catch (Exception ex)
        {
            if (EnableWithWindowsDisplaySwitch() == 0) return DisplayResult.Ok();
            return DisplayResult.Fail($"Não foi possível restabelecer as telas: {ex.Message}");
        }
    }

    public static List<DisplayMode> GetModes(string gdiName)
    {
        if (string.IsNullOrWhiteSpace(gdiName)) return [];

        var seen = new HashSet<(uint Width, uint Height, uint Rate)>();
        var results = new List<DisplayMode>();

        for (uint i = 0; ; i++)
        {
            var mode = DisplayNative.DevMode.Create();
            if (!DisplayNative.EnumDisplaySettings(gdiName, i, ref mode)) break;
            if (mode.PelsWidth == 0 || mode.PelsHeight == 0 || mode.DisplayFrequency == 0) continue;

            if (seen.Add((mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency)))
            {
                var orientation = Enum.IsDefined(typeof(DisplayOrientation), mode.DisplayOrientation)
                    ? (DisplayOrientation)mode.DisplayOrientation
                    : DisplayOrientation.Default;
                results.Add(new DisplayMode(mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency, orientation));
            }
        }

        return results;
    }

    public static (DisplayMode? Mode, Point Position) GetCurrentModeAndPosition(string gdiName)
    {
        if (string.IsNullOrWhiteSpace(gdiName)) return (null, Point.Empty);

        var mode = DisplayNative.DevMode.Create();
        if (!DisplayNative.EnumDisplaySettings(gdiName, DisplayNative.EnumCurrentSettings, ref mode))
        {
            return (null, Point.Empty);
        }

        var orientation = Enum.IsDefined(typeof(DisplayOrientation), mode.DisplayOrientation)
            ? (DisplayOrientation)mode.DisplayOrientation
            : DisplayOrientation.Default;

        return (
            new DisplayMode(mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency, orientation),
            new Point(mode.PositionX, mode.PositionY)
        );
    }

    public static DisplayResult SetMode(
        string gdiName,
        uint width,
        uint height,
        uint refreshRate,
        DisplayOrientation orientation)
    {
        const string action = "aplicar esta configuração de vídeo";
        if (string.IsNullOrWhiteSpace(gdiName))
        {
            return DisplayResult.Fail("O monitor selecionado não possui uma superfície GDI ativa.");
        }

        var current = DisplayNative.DevMode.Create();
        if (!DisplayNative.EnumDisplaySettings(gdiName, DisplayNative.EnumCurrentSettings, ref current))
        {
            return DisplayResult.Fail("Não foi possível ler o modo atual deste monitor.");
        }

        var currentOrientation = Enum.IsDefined(typeof(DisplayOrientation), current.DisplayOrientation)
            ? (DisplayOrientation)current.DisplayOrientation
            : DisplayOrientation.Default;

        var targetWidth = width;
        var targetHeight = height;

        // When rotating between Landscape (0/180) and Portrait (90/270), swap width & height if needed.
        if (IsPortrait(currentOrientation) != IsPortrait(orientation))
        {
            var shouldBePortrait = IsPortrait(orientation);
            if ((targetWidth > targetHeight) == shouldBePortrait)
            {
                (targetWidth, targetHeight) = (targetHeight, targetWidth);
            }
        }

        var candidate = BuildModeUpdate(current, targetWidth, targetHeight, refreshRate, orientation);
        var testCode = DisplayNative.ChangeDisplaySettingsEx(gdiName, ref candidate, IntPtr.Zero, DisplayNative.CdsTest, IntPtr.Zero);

        // Fallback test with unswapped dimensions in case the driver already reported rotated dimensions.
        if (testCode != 0 && (targetWidth != width || targetHeight != height))
        {
            var altCandidate = BuildModeUpdate(current, width, height, refreshRate, orientation);
            if (DisplayNative.ChangeDisplaySettingsEx(gdiName, ref altCandidate, IntPtr.Zero, DisplayNative.CdsTest, IntPtr.Zero) == 0)
            {
                candidate = altCandidate;
                testCode = 0;
            }
        }

        if (testCode != 0)
        {
            return DisplayResult.FromDispChange(testCode, action);
        }

        var applyCode = DisplayNative.ChangeDisplaySettingsEx(
            gdiName,
            ref candidate,
            IntPtr.Zero,
            DisplayNative.CdsUpdateRegistry,
            IntPtr.Zero);

        return DisplayResult.FromDispChange(applyCode, action);
    }

    private static DisplayNative.DevMode BuildModeUpdate(
        DisplayNative.DevMode baseMode,
        uint width,
        uint height,
        uint refreshRate,
        DisplayOrientation orientation)
    {
        baseMode.PelsWidth = width;
        baseMode.PelsHeight = height;
        baseMode.DisplayFrequency = refreshRate;
        baseMode.DisplayOrientation = (uint)orientation;
        baseMode.Fields =
            DisplayNative.DmPelsWidth |
            DisplayNative.DmPelsHeight |
            DisplayNative.DmDisplayFrequency |
            DisplayNative.DmDisplayOrientation;
        return baseMode;
    }

    private static bool IsPortrait(DisplayOrientation orientation) =>
        orientation is DisplayOrientation.Rotate90 or DisplayOrientation.Rotate270;

    private static DisplayResult SetPrimaryViaGdi(string primaryGdiName, string action)
    {
        var primaryMode = DisplayNative.DevMode.Create();
        if (!DisplayNative.EnumDisplaySettings(primaryGdiName, DisplayNative.EnumCurrentSettings, ref primaryMode))
        {
            return DisplayResult.Fail("Não foi possível ler a posição atual do monitor.");
        }

        var offsetX = primaryMode.PositionX;
        var offsetY = primaryMode.PositionY;
        if (offsetX == 0 && offsetY == 0) return DisplayResult.Ok();

        foreach (var screen in Screen.AllScreens)
        {
            var devMode = DisplayNative.DevMode.Create();
            if (!DisplayNative.EnumDisplaySettings(screen.DeviceName, DisplayNative.EnumCurrentSettings, ref devMode))
                continue;

            devMode.PositionX -= offsetX;
            devMode.PositionY -= offsetY;
            devMode.Fields = DisplayNative.DmPosition;

            var flags = DisplayNative.CdsUpdateRegistry | DisplayNative.CdsNoReset;
            if (SameGdi(screen.DeviceName, primaryGdiName))
            {
                flags |= DisplayNative.CdsSetPrimary;
            }

            var stepCode = DisplayNative.ChangeDisplaySettingsEx(screen.DeviceName, ref devMode, IntPtr.Zero, flags, IntPtr.Zero);
            if (stepCode != 0)
            {
                return DisplayResult.FromDispChange(stepCode, action);
            }
        }

        var commitCode = DisplayNative.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        return DisplayResult.FromDispChange(commitCode, action);
    }

    private static bool WriteTargetPositionsToModes(
        DisplayNative.DisplayPathInfo[] paths,
        DisplayNative.DisplayModeInfo[] modes,
        Dictionary<string, Point> normalizedByTargetKey)
    {
        var updatedIndices = new HashSet<int>();
        fixed (DisplayNative.DisplayModeInfo* modesPtr = modes)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if ((paths[i].Flags & DisplayNative.PathActive) == 0) continue;
                var tKey = TargetKey(paths[i].Target.AdapterId, paths[i].Target.Id);
                if (!normalizedByTargetKey.TryGetValue(tKey, out var pos)) continue;

                var idx = GetSourceModeIndex(in paths[i], modes.Length);
                if (idx < 0 || modes[idx].InfoType != DisplayNative.SourceModeType) continue;

                if (updatedIndices.Add(idx))
                {
                    var entry = (byte*)(modesPtr + idx);
                    *(int*)(entry + DisplayNative.ModePositionXOffset) = pos.X;
                    *(int*)(entry + DisplayNative.ModePositionYOffset) = pos.Y;
                }
            }
        }
        return updatedIndices.Count > 0;
    }

    private static DisplayResult SetPositionsViaGdi(
        Dictionary<string, Point> normalizedByGdi,
        string primaryGdiName,
        string action)
    {
        if (normalizedByGdi.Count == 0)
        {
            return DisplayResult.Fail("Nenhuma superfície GDI ativa para posicionar.");
        }

        foreach (var (gdiName, targetPos) in normalizedByGdi)
        {
            var devMode = DisplayNative.DevMode.Create();
            if (!DisplayNative.EnumDisplaySettings(gdiName, DisplayNative.EnumCurrentSettings, ref devMode))
                continue;

            devMode.PositionX = targetPos.X;
            devMode.PositionY = targetPos.Y;
            devMode.Fields = DisplayNative.DmPosition;

            var flags = DisplayNative.CdsUpdateRegistry | DisplayNative.CdsNoReset;
            if (SameGdi(gdiName, primaryGdiName))
            {
                flags |= DisplayNative.CdsSetPrimary;
            }

            var stepCode = DisplayNative.ChangeDisplaySettingsEx(gdiName, ref devMode, IntPtr.Zero, flags, IntPtr.Zero);
            if (stepCode != 0)
            {
                return DisplayResult.FromDispChange(stepCode, action);
            }
        }

        var commitCode = DisplayNative.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        return DisplayResult.FromDispChange(commitCode, action);
    }

    private static List<int> ActivateCandidates(
        DisplayNative.DisplayPathInfo[] paths,
        List<DisplayNative.DisplayPathInfo> active,
        string targetKey)
    {
        var usedSources = active
            .Select(p => TargetKey(p.Source.AdapterId, p.Source.Id))
            .ToHashSet();

        return Enumerable.Range(0, paths.Length)
            .Where(i => TargetKey(paths[i].Target.AdapterId, paths[i].Target.Id) == targetKey && paths[i].Target.TargetAvailable != 0)
            .OrderBy(i => usedSources.Contains(TargetKey(paths[i].Source.AdapterId, paths[i].Source.Id)) ? 1 : 0)
            .ToList();
    }

    private static int Apply(DisplayNative.DisplayPathInfo[] paths, DisplayNative.DisplayModeInfo[] modes, uint flags)
    {
        var modeArray = modes.Length == 0 ? null : modes;
        return DisplayNative.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modeArray, flags);
    }

    private static bool TryGetSourcePosition(
        in DisplayNative.DisplayPathInfo path,
        DisplayNative.DisplayModeInfo[] modes,
        out Point position)
    {
        position = Point.Empty;
        var index = GetSourceModeIndex(in path, modes.Length);
        if (index < 0 || modes[index].InfoType != DisplayNative.SourceModeType) return false;

        fixed (DisplayNative.DisplayModeInfo* modesPtr = modes)
        {
            var entry = (byte*)(modesPtr + index);
            var x = *(int*)(entry + DisplayNative.ModePositionXOffset);
            var y = *(int*)(entry + DisplayNative.ModePositionYOffset);
            position = new Point(x, y);
            return true;
        }
    }

    private static void NormalizeLayout(DisplayNative.DisplayPathInfo[] paths, DisplayNative.DisplayModeInfo[] modes)
    {
        if (modes.Length == 0) return;

        var distinctIndices = GetDistinctActiveSourceModeIndices(paths, modes);
        if (distinctIndices.Count == 0) return;

        fixed (DisplayNative.DisplayModeInfo* modesPtr = modes)
        {
            // If an active source already sits at (0, 0), keep existing primary origin intact.
            foreach (var idx in distinctIndices)
            {
                var entry = (byte*)(modesPtr + idx);
                var x = *(int*)(entry + DisplayNative.ModePositionXOffset);
                var y = *(int*)(entry + DisplayNative.ModePositionYOffset);
                if (x == 0 && y == 0) return;
            }

            // Pick the top-leftmost active monitor as the new (0, 0) origin so SetDisplayConfig succeeds.
            int originX = int.MaxValue, originY = int.MaxValue;
            foreach (var idx in distinctIndices)
            {
                var entry = (byte*)(modesPtr + idx);
                var x = *(int*)(entry + DisplayNative.ModePositionXOffset);
                var y = *(int*)(entry + DisplayNative.ModePositionYOffset);
                if (x < originX || (x == originX && y < originY))
                {
                    originX = x;
                    originY = y;
                }
            }

            foreach (var idx in distinctIndices)
            {
                var entry = (byte*)(modesPtr + idx);
                *(int*)(entry + DisplayNative.ModePositionXOffset) -= originX;
                *(int*)(entry + DisplayNative.ModePositionYOffset) -= originY;
            }
        }
    }

    private static void ShiftAllActiveSources(
        DisplayNative.DisplayPathInfo[] paths,
        DisplayNative.DisplayModeInfo[] modes,
        int deltaX,
        int deltaY)
    {
        if (modes.Length == 0) return;
        var distinctIndices = GetDistinctActiveSourceModeIndices(paths, modes);

        fixed (DisplayNative.DisplayModeInfo* modesPtr = modes)
        {
            foreach (var idx in distinctIndices)
            {
                var entry = (byte*)(modesPtr + idx);
                *(int*)(entry + DisplayNative.ModePositionXOffset) += deltaX;
                *(int*)(entry + DisplayNative.ModePositionYOffset) += deltaY;
            }
        }
    }

    private static HashSet<int> GetDistinctActiveSourceModeIndices(
        DisplayNative.DisplayPathInfo[] paths,
        DisplayNative.DisplayModeInfo[] modes)
    {
        var indices = new HashSet<int>();
        for (var i = 0; i < paths.Length; i++)
        {
            if ((paths[i].Flags & DisplayNative.PathActive) == 0) continue;
            var idx = GetSourceModeIndex(in paths[i], modes.Length);
            if (idx >= 0 && modes[idx].InfoType == DisplayNative.SourceModeType)
            {
                indices.Add(idx);
            }
        }
        return indices;
    }

    private static int GetSourceModeIndex(in DisplayNative.DisplayPathInfo path, int modeCount)
    {
        var raw = path.Source.ModeInfoIdx;
        int index;

        if ((path.Flags & DisplayNative.PathSupportVirtualMode) != 0)
        {
            var sourceIdx = (raw >> 16) & 0xFFFF;
            if (sourceIdx == DisplayNative.InvalidVirtualModeIndex) return -1;
            index = (int)sourceIdx;
        }
        else
        {
            if (raw == DisplayNative.InvalidModeIndex) return -1;
            index = (int)raw;
        }

        return index >= 0 && index < modeCount ? index : -1;
    }

    private static int EnableWithWindowsDisplaySwitch()
    {
        try
        {
            var executable = Path.Combine(Environment.SystemDirectory, "DisplaySwitch.exe");
            using var process = Process.Start(new ProcessStartInfo(executable, "/extend")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return -2;
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0 ? 0 : -2;
        }
        catch
        {
            return -2;
        }
    }

    private static (DisplayNative.DisplayPathInfo[] Paths, DisplayNative.DisplayModeInfo[] Modes) QueryAllPaths()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var result = DisplayNative.GetDisplayConfigBufferSizes(DisplayNative.ConfigFlags, out var pathCount, out var modeCount);
            if (result != 0) throw new InvalidOperationException($"GetDisplayConfigBufferSizes retornou {result}");

            var paths = new DisplayNative.DisplayPathInfo[pathCount];
            var modes = new DisplayNative.DisplayModeInfo[modeCount];
            result = DisplayNative.QueryDisplayConfig(DisplayNative.ConfigFlags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (result == DisplayNative.ErrorInsufficientBuffer) continue;
            if (result != 0) throw new InvalidOperationException($"QueryDisplayConfig retornou {result}");

            return (paths.AsSpan(0, (int)pathCount).ToArray(), modes.AsSpan(0, (int)modeCount).ToArray());
        }

        throw new InvalidOperationException("A configuração de monitores mudou continuamente durante a leitura.");
    }

    private static (string Name, string Path) GetTargetName(DisplayNative.DisplayLuid adapterId, uint targetId)
    {
        const int requestSize = 420;
        const int nameOffset = 36;
        var request = Marshal.AllocHGlobal(requestSize);
        try
        {
            new Span<byte>((void*)request, requestSize).Clear();
            Marshal.WriteInt32(request, 0, DisplayNative.GetTargetNameType);
            Marshal.WriteInt32(request, 4, requestSize);
            Marshal.StructureToPtr(adapterId, IntPtr.Add(request, 8), false);
            Marshal.WriteInt32(request, 16, unchecked((int)targetId));

            if (DisplayNative.DisplayConfigGetDeviceInfo(request) != 0) return (string.Empty, string.Empty);

            var name = Marshal.PtrToStringUni(IntPtr.Add(request, nameOffset))?.Trim() ?? string.Empty;
            var path = Marshal.PtrToStringUni(IntPtr.Add(request, nameOffset + 128))?.Trim() ?? string.Empty;
            return (name, path);
        }
        finally
        {
            Marshal.FreeHGlobal(request);
        }
    }

    private static string GetSourceName(DisplayNative.DisplayLuid adapterId, uint sourceId)
    {
        const int requestSize = 84;
        const int nameOffset = 20;
        var request = Marshal.AllocHGlobal(requestSize);
        try
        {
            new Span<byte>((void*)request, requestSize).Clear();
            Marshal.WriteInt32(request, 0, DisplayNative.GetSourceNameType);
            Marshal.WriteInt32(request, 4, requestSize);
            Marshal.StructureToPtr(adapterId, IntPtr.Add(request, 8), false);
            Marshal.WriteInt32(request, 16, unchecked((int)sourceId));

            return DisplayNative.DisplayConfigGetDeviceInfo(request) == 0
                ? Marshal.PtrToStringUni(IntPtr.Add(request, nameOffset))?.Trim() ?? string.Empty
                : string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(request);
        }
    }

    private static string TargetKey(DisplayNative.DisplayLuid luid, uint id) =>
        $"{luid.HighPart:X8}:{luid.LowPart:X8}:{id:X8}";

    public static bool SameGdi(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
