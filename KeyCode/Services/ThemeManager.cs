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

    /// <summary>Variante de couleurs appliquée sur la base Sombre : "" (Sombre+) ou "OneDarkPro".</summary>
    public static string Variante { get; private set; } = "";

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
        ["ThemeEtoile"] = "#DF6100",
        ["ThemeListeSelection"] = "#E4E6F1",
        ["ThemeLien"] = "#006AB1",
        ["ThemeBoutonExt"] = "#007ACC",
        ["ThemeBoutonExtSurvol"] = "#0062A3",
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
        ["ThemeEtoile"] = "#FF8E00",
        ["ThemeListeSelection"] = "#37373D",
        ["ThemeLien"] = "#3794FF",
        ["ThemeBoutonExt"] = "#0E639C",
        ["ThemeBoutonExtSurvol"] = "#1177BB",
    };

    // One Dark Pro : mêmes clés que Sombre+, avec les couleurs du thème d'Atom
    private static readonly Dictionary<string, string> _oneDark = Fusionner(_sombre, new()
    {
        ["ThemeFond"] = "#282C34",
        ["ThemeTexte"] = "#ABB2BF",
        ["ThemeFondSecondaire"] = "#21252B",
        ["ThemeFondTertiaire"] = "#2C313A",
        ["ThemeBarreActivite"] = "#21252B",
        ["ThemeBarreTitre"] = "#21252B",
        ["ThemeBarreStatut"] = "#21252B",
        ["ThemeBarreStatutTexte"] = "#9DA5B4",
        ["ThemeBordure"] = "#181A1F",
        ["ThemeAccent"] = "#528BFF",
        ["ThemeAccentSurvol"] = "#6B9CFF",
        ["ThemeSurvol"] = "#2C313A",
        ["ThemeNumerosLigne"] = "#636D83",
        ["ThemeSelection"] = "#3E4451",
        ["ThemeLigneCourante"] = "#2C313A",
        ["ThemeTexteSecondaire"] = "#7F848E",
        ["ThemeSaisieFond"] = "#1D1F23",
        ["ThemeSaisieBordure"] = "#3A3F4B",
        ["ThemeScrollThumb"] = "#4E5666",
        ["ThemeScrollThumbSurvol"] = "#5A6375",
        ["ThemeErreur"] = "#E06C75",
        ["ThemeAvertissement"] = "#E5C07B",
        ["ThemeSucces"] = "#98C379",
        ["ThemeTerminalFond"] = "#21252B",
        ["ThemeTerminalTexte"] = "#ABB2BF",
        ["ThemeListeSelection"] = "#323842",
        ["ThemeLien"] = "#61AFEF",
        ["ThemeBoutonExt"] = "#4D78CC",
        ["ThemeBoutonExtSurvol"] = "#5A86DB",
    });

    private static Dictionary<string, string> Fusionner(
        Dictionary<string, string> baseP, Dictionary<string, string> surcharge)
    {
        var resultat = new Dictionary<string, string>(baseP);
        foreach (var (cle, hex) in surcharge) resultat[cle] = hex;
        return resultat;
    }

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

        // La variante One Dark Pro ne s'applique que sur la base Sombre
        Remplir(!sombre ? _clair : Variante == "OneDarkPro" ? _oneDark : _sombre);
        AjouterALApplication();

        AppEvents.SignalerThemeChange(ThemeActuel);
    }

    /// <summary>Mémorise la variante sans rien appliquer ni notifier (à appeler avant le premier Appliquer).</summary>
    public static void DefinirVariante(string variante) =>
        Variante = string.Equals(variante, "OneDarkPro", StringComparison.OrdinalIgnoreCase) ? "OneDarkPro" : "";

    /// <summary>Change de variante et réapplique le thème. OneDarkPro force la base Sombre.</summary>
    public static void AppliquerVariante(string variante)
    {
        DefinirVariante(variante);
        Appliquer(Variante == "OneDarkPro" ? "Sombre" : ThemeActuel);
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
