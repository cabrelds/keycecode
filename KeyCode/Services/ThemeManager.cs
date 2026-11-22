using System.Windows;
using System.Windows.Media;

namespace KeyCode.Services;

/// <summary>
/// Thèmes Clair et Sombre. Les couleurs sont stockées dans un ResourceDictionary ajouté aux
/// ressources de l'application : on le remplit à chaud quand le thème change, ce qui met à jour
/// tout contrôle qui utilise {DynamicResource ThemeFond} (ou une autre clé ci-dessous).
/// Clés disponibles : ThemeFond, ThemeTexte, ThemeFondSecondaire, ThemeBordure, ThemeAccent,
/// ThemeNumerosLigne, ThemeSelection, ThemeLigneCourante, ThemeTexteSecondaire.
/// Propriétaire : nyangono (module repris par cabrelds).
/// </summary>
public static class ThemeManager
{
    public static string ThemeActuel { get; private set; } = "Clair";

    private static readonly Dictionary<string, string> _clair = new()
    {
        ["ThemeFond"] = "#FFFFFF",
        ["ThemeTexte"] = "#1E1E1E",
        ["ThemeFondSecondaire"] = "#F3F3F3",
        ["ThemeBordure"] = "#DDDDDD",
        ["ThemeAccent"] = "#007ACC",
        ["ThemeNumerosLigne"] = "#237893",
        ["ThemeSelection"] = "#ADD6FF",
        ["ThemeLigneCourante"] = "#F2F2F2",
        ["ThemeTexteSecondaire"] = "#666666",
    };

    private static readonly Dictionary<string, string> _sombre = new()
    {
        ["ThemeFond"] = "#1E1E1E",
        ["ThemeTexte"] = "#D4D4D4",
        ["ThemeFondSecondaire"] = "#252526",
        ["ThemeBordure"] = "#3C3C3C",
        ["ThemeAccent"] = "#007ACC",
        ["ThemeNumerosLigne"] = "#858585",
        ["ThemeSelection"] = "#264F78",
        ["ThemeLigneCourante"] = "#2A2D2E",
        ["ThemeTexteSecondaire"] = "#969696",
    };

    private static readonly ResourceDictionary _dictionnaire = new();
    private static bool _ajouteALApplication;

    static ThemeManager()
    {
        // Thème Clair par défaut, pour que Pinceau() fonctionne même avant le premier Appliquer()
        Remplir(_clair);
    }

    /// <summary>Applique un thème ("Clair" ou "Sombre", toute autre valeur donne "Clair") puis prévient les autres modules.</summary>
    public static void Appliquer(string theme)
    {
        var sombre = string.Equals(theme, "Sombre", StringComparison.OrdinalIgnoreCase);
        ThemeActuel = sombre ? "Sombre" : "Clair";

        Remplir(sombre ? _sombre : _clair);
        AjouterALApplication();

        // EditeurService écoute cet événement pour réappliquer couleurs et coloration à tous les éditeurs
        AppEvents.SignalerThemeChange(ThemeActuel);
    }

    /// <summary>Retourne le pinceau du thème actuel pour une clé (ex. "ThemeFond"). Transparent si la clé n'existe pas.</summary>
    public static Brush Pinceau(string cle) =>
        _dictionnaire[cle] as Brush ?? Brushes.Transparent;

    // Remplace les couleurs du dictionnaire (par de nouveaux pinceaux figés)
    private static void Remplir(Dictionary<string, string> palette)
    {
        foreach (var (cle, hex) in palette)
        {
            var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            pinceau.Freeze();
            _dictionnaire[cle] = pinceau;
        }
    }

    // Branche le dictionnaire sur l'application (une seule fois)
    private static void AjouterALApplication()
    {
        if (_ajouteALApplication || Application.Current == null) return;
        Application.Current.Resources.MergedDictionaries.Add(_dictionnaire);
        _ajouteALApplication = true;
    }
}
