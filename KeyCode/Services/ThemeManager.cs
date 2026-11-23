using System.Windows;
using System.Windows.Media;

namespace KeyCode.Services;

/// <summary>
/// Gestionnaire centralisé des thèmes Clair+ et Sombre+ (style VS Code).
/// Remplit dynamiquement un ResourceDictionary branché dans Application.Resources.
/// </summary>
public static class ThemeManager
{
    public static string ThemeActuel { get; private set; } = "Sombre";

    private static readonly Dictionary<string, string> _clair = new()
    {
        ["ThemeFond"] = "#FFFFFF",
        ["ThemeTexte"] = "#1E1E1E",
        ["ThemeFondSecondaire"] = "#F3F3F3",
        ["ThemeFondTertiaire"] = "#ECECEC",
        ["ThemeBarreActivite"] = "#E8E8E8",
        ["ThemeBarreTitre"] = "#DDDDDD",
        ["ThemeBarreStatut"] = "#007ACC",
        ["ThemeBarreStatutTexte"] = "#FFFFFF",
        ["ThemeBordure"] = "#D4D4D4",
        ["ThemeAccent"] = "#007ACC",
        ["ThemeAccentSurvol"] = "#0062A3",
        ["ThemeSurvol"] = "#E5E5E5",
        ["ThemeNumerosLigne"] = "#237893",
        ["ThemeSelection"] = "#ADD6FF",
        ["ThemeLigneCourante"] = "#F3F6FA",
        ["ThemeTexteSecondaire"] = "#666666",
        ["ThemeSaisieFond"] = "#FFFFFF",
        ["ThemeSaisieBordure"] = "#CECECE",
        ["ThemeScrollThumb"] = "#C1C1C1",
        ["ThemeScrollThumbSurvol"] = "#929292",
        ["ThemeErreur"] = "#D32F2F",
        ["ThemeAvertissement"] = "#B7791F",
        ["ThemeSucces"] = "#2E7D32",
        ["ThemeTerminalFond"] = "#FAFAFA",
        ["ThemeTerminalTexte"] = "#1E1E1E",
    };

    private static readonly Dictionary<string, string> _sombre = new()
    {
        ["ThemeFond"] = "#1E1E1E",
        ["ThemeTexte"] = "#CCCCCC",
        ["ThemeFondSecondaire"] = "#252526",
        ["ThemeFondTertiaire"] = "#2D2D2D",
        ["ThemeBarreActivite"] = "#333333",
        ["ThemeBarreTitre"] = "#323233",
        ["ThemeBarreStatut"] = "#007ACC",
        ["ThemeBarreStatutTexte"] = "#FFFFFF",
        ["ThemeBordure"] = "#3C3C3C",
        ["ThemeAccent"] = "#007FD4",
        ["ThemeAccentSurvol"] = "#1C97EA",
        ["ThemeSurvol"] = "#2A2D2E",
        ["ThemeNumerosLigne"] = "#858585",
        ["ThemeSelection"] = "#264F78",
        ["ThemeLigneCourante"] = "#2A2D2E",
        ["ThemeTexteSecondaire"] = "#969696",
        ["ThemeSaisieFond"] = "#3C3C3C",
        ["ThemeSaisieBordure"] = "#4E4E4E",
        ["ThemeScrollThumb"] = "#424242",
        ["ThemeScrollThumbSurvol"] = "#5A5A5A",
        ["ThemeErreur"] = "#F48771",
        ["ThemeAvertissement"] = "#CCA700",
        ["ThemeSucces"] = "#89D185",
        ["ThemeTerminalFond"] = "#181818",
        ["ThemeTerminalTexte"] = "#CCCCCC",
    };

    private static readonly ResourceDictionary _dictionnaire = new();
    private static bool _ajouteALApplication;

    static ThemeManager()
    {
        Remplir(_sombre);
    }

    /// <summary>Applique un thème ("Clair" ou "Sombre") et notifie tous les contrôles.</summary>
    public static void Appliquer(string theme)
    {
        var sombre = !string.Equals(theme, "Clair", StringComparison.OrdinalIgnoreCase);
        ThemeActuel = sombre ? "Sombre" : "Clair";

        Remplir(sombre ? _sombre : _clair);
        AjouterALApplication();

        AppEvents.SignalerThemeChange(ThemeActuel);
    }

    /// <summary>Retourne le pinceau du thème actuel pour une clé donnée.</summary>
    public static Brush Pinceau(string cle) =>
        _dictionnaire[cle] as Brush ?? Brushes.Transparent;

    private static void Remplir(Dictionary<string, string> palette)
    {
        foreach (var (cle, hex) in palette)
        {
            var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            pinceau.Freeze();
            _dictionnaire[cle] = pinceau;
        }
    }

    private static void AjouterALApplication()
    {
        if (_ajouteALApplication || Application.Current == null) return;
        Application.Current.Resources.MergedDictionaries.Add(_dictionnaire);
        _ajouteALApplication = true;
    }
}
