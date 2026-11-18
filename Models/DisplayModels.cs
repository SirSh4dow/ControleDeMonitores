namespace MonitorControl.Models;

internal enum DisplayOrientation : uint
{
    Default = 0,
    Rotate90 = 1,
    Rotate180 = 2,
    Rotate270 = 3
}

internal sealed record OrientationOption(DisplayOrientation Value, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<OrientationOption> All =
    [
        new(DisplayOrientation.Default, "Paisagem"),
        new(DisplayOrientation.Rotate90, "Retrato"),
        new(DisplayOrientation.Rotate180, "Paisagem (virado)"),
        new(DisplayOrientation.Rotate270, "Retrato (virado)")
    ];

    public static string GetLabel(DisplayOrientation orientation) =>
        All.FirstOrDefault(o => o.Value == orientation)?.Label ?? "Paisagem";
}

internal sealed record DisplayResolution(uint Width, uint Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

internal sealed record DisplayMode(
    uint Width,
    uint Height,
    uint RefreshRate,
    DisplayOrientation Orientation = DisplayOrientation.Default)
{
    public override string ToString() => $"{Width} × {Height} @ {RefreshRate} Hz";
}

internal sealed record RefreshRateOption(uint Rate)
{
    public override string ToString() => $"{Rate} Hz";
}

internal sealed record DisplayDevice(
    uint AdapterLowPart,
    int AdapterHighPart,
    uint TargetId,
    string IdentityKey,
    string GdiName,
    string MonitorName,
    string DisplayTitle,
    int DisplayNumber,
    string ConnectorName,
    bool IsActive,
    bool IsPrimary,
    Point Position,
    DisplayMode? CurrentMode,
    IReadOnlyList<DisplayMode> AvailableModes);

internal readonly record struct DisplayResult(int Code, string Message, bool RequiresElevation = false, bool RequiresRestart = false)
{
    public bool IsSuccess => Code == 0;

    public static DisplayResult Ok() => new(0, string.Empty);

    public static DisplayResult Fail(string message, int code = -1) => new(code, message);

    public static DisplayResult FromWin32(int code, string action) => code switch
    {
        0 => Ok(),
        5 => new(5, $"O Windows exige permissão de administrador para {action}.", RequiresElevation: true),
        31 => new(31, $"O driver de vídeo relatou uma falha geral ao tentar {action} (código 31)."),
        50 => new(50, $"Esta operação não é suportada pelo hardware ou driver atual ao tentar {action} (código 50)."),
        87 => new(87, $"O Windows rejeitou a combinação de topologia solicitada ao tentar {action} (parâmetro inválido, código 87)."),
        1610 => new(1610, $"A configuração de exibição solicitada é inválida para este conjunto de monitores ao tentar {action} (código 1610)."),
        _ => new(code, $"O Windows retornou o código de erro {code} ao tentar {action}.")
    };

    public static DisplayResult FromDispChange(int code, string action) => code switch
    {
        0 => Ok(),
        1 => new(1, "As alterações foram salvas, mas o Windows solicita reinicialização para concluí-las.", RequiresRestart: true),
        -1 => new(-1, $"O driver de vídeo falhou ao {action} (DISP_CHANGE_FAILED)."),
        -2 => new(-2, $"Esta combinação de resolução, taxa de atualização ou rotação não é suportada pelo monitor ou cabo atual."),
        -3 => new(-3, $"Não foi possível gravar as configurações no registro do Windows (permissão insuficiente)."),
        -4 => new(-4, $"Sinalizadores inválidos enviados ao driver ao tentar {action}."),
        -5 => new(-5, $"Parâmetros inválidos enviados ao driver ao tentar {action}."),
        -6 => new(-6, $"O sistema DualView recusou esta configuração ao tentar {action}."),
        _ => new(code, $"Não foi possível {action} (código de vídeo {code}).")
    };
}
