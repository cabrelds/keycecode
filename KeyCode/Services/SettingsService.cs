namespace KeyCode.Services;

public static class SettingsService
{
    public static double TaillePolice { get; set; } = 14;
    public static string Theme { get; set; } = "Clair";
    public static List<string> DerniersFichiers { get; set; } = new();
    public static string DernierDossier { get; set; } = "";

    public static void Charger() { /* à faire par nyangono */ }
    public static void Enregistrer() { /* à faire par nyangono */ }
}