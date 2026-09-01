using System.Drawing;

namespace NfeAgendamento.App;

public static class CentralTheme
{
    public static Color BrandBlue { get; } = Color.FromArgb(13, 61, 112);
    public static Color BrandBlueSoft { get; } = Color.FromArgb(22, 77, 132);
    public static Color BrandYellow { get; } = Color.FromArgb(246, 201, 21);
    public static Color Background { get; } = Color.FromArgb(246, 248, 251);
    public static Color Surface { get; } = Color.FromArgb(255, 255, 255);
    public static Color Text { get; } = Color.FromArgb(25, 37, 52);
    public static Color MutedText { get; } = Color.FromArgb(93, 108, 124);
    public static Color Border { get; } = Color.FromArgb(214, 222, 231);
    public static Color Success { get; } = Color.FromArgb(29, 128, 86);
    public static Color Warning { get; } = Color.FromArgb(154, 111, 0);
    public static Color Danger { get; } = Color.FromArgb(180, 55, 55);
}
