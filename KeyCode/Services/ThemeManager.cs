namespace KeyCode.Services;

public static class ThemeManager
{
    public static string ThemeActuel { get; private set; } = "Clair";

    public static void Appliquer(string theme)
    {
        ThemeActuel = theme;
        AppEvents.SignalerThemeChange(theme);
    }
}