using System.IO;
using System.Text.Json;

namespace KeyCode.Services;

/// <summary>
/// Paramètres persistants de KeyCode sauvegardés en JSON dans %AppData%\KeyCode\settings.json.
/// </summary>
public static class SettingsService
{
    private const double TaillePoliceParDefaut = 14;
    private const double TaillePoliceMin = 8;
    private const double TaillePoliceMax = 48;

    public static double TaillePolice { get; set; } = TaillePoliceParDefaut;
    public static string Theme { get; set; } = "Sombre";
    public static List<string> DerniersFichiers { get; set; } = new();
    public static string DernierDossier { get; set; } = "";

    // Paramètres additionnels pour l'expérience VS Code et l'assistant Gemini
    public static List<string> OngletsOuverts { get; set; } = new();
    public static int OngletActifIndex { get; set; }
    public static string CleApiGemini { get; set; } = "";
    public static string ModeleGemini { get; set; } = "gemini-3.8-flash";
    public static double LargeurPanneauGauche { get; set; } = 260;
    public static double HauteurPanneauBas { get; set; } = 210;
    public static bool PanneauIAVisible { get; set; }
    public static double LargeurPanneauIA { get; set; } = 340;

    // Extensions (Place de marché)
    public static List<string> ExtensionsInstallees { get; set; } = new();
    public static List<string> ExtensionsDesactivees { get; set; } = new();
    public static List<string> ExtensionsSansMiseAJourAuto { get; set; } = new();
    public static string VarianteTheme { get; set; } = "";

    private static readonly string _dossier =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyCode");

    private static readonly string _fichier = Path.Combine(_dossier, "settings.json");
    private static readonly JsonSerializerOptions _optionsJson = new() { WriteIndented = true };

    private sealed class Donnees
    {
        public double TaillePolice { get; set; } = TaillePoliceParDefaut;
        public string? Theme { get; set; } = "Sombre";
        public List<string>? DerniersFichiers { get; set; } = new();
        public string? DernierDossier { get; set; } = "";
        public List<string>? OngletsOuverts { get; set; } = new();
        public int OngletActifIndex { get; set; }
        public string? CleApiGemini { get; set; } = "";
        public string? ModeleGemini { get; set; } = "gemini-3.8-flash";
        public double LargeurPanneauGauche { get; set; } = 260;
        public double HauteurPanneauBas { get; set; } = 210;
        public bool PanneauIAVisible { get; set; }
        public double LargeurPanneauIA { get; set; } = 340;
        public List<string>? ExtensionsInstallees { get; set; } = new();
        public List<string>? ExtensionsDesactivees { get; set; } = new();
        public List<string>? ExtensionsSansMiseAJourAuto { get; set; } = new();
        public string? VarianteTheme { get; set; } = "";
    }

    public static void Charger()
    {
        ReinitialiserParDefaut();

        try
        {
            if (!File.Exists(_fichier)) return;

            var donnees = JsonSerializer.Deserialize<Donnees>(File.ReadAllText(_fichier));
            if (donnees == null) return;

            if (!double.IsNaN(donnees.TaillePolice) && !double.IsInfinity(donnees.TaillePolice))
                TaillePolice = Math.Clamp(donnees.TaillePolice, TaillePoliceMin, TaillePoliceMax);

            Theme = string.Equals(donnees.Theme, "Clair", StringComparison.OrdinalIgnoreCase) ? "Clair" : "Sombre";

            DerniersFichiers = (donnees.DerniersFichiers ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            DernierDossier = donnees.DernierDossier ?? "";

            OngletsOuverts = (donnees.OngletsOuverts ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            OngletActifIndex = Math.Max(0, donnees.OngletActifIndex);
            CleApiGemini = donnees.CleApiGemini ?? "";
            ModeleGemini = MigrerModeleObsolete(donnees.ModeleGemini);
            LargeurPanneauGauche = donnees.LargeurPanneauGauche is >= 140 and <= 600 ? donnees.LargeurPanneauGauche : 260;
            HauteurPanneauBas = donnees.HauteurPanneauBas is >= 100 and <= 500 ? donnees.HauteurPanneauBas : 210;
            PanneauIAVisible = donnees.PanneauIAVisible;
            LargeurPanneauIA = donnees.LargeurPanneauIA is >= 240 and <= 650 ? donnees.LargeurPanneauIA : 340;
            ExtensionsInstallees = NettoyerListe(donnees.ExtensionsInstallees);
            ExtensionsDesactivees = NettoyerListe(donnees.ExtensionsDesactivees);
            ExtensionsSansMiseAJourAuto = NettoyerListe(donnees.ExtensionsSansMiseAJourAuto);
            VarianteTheme = string.Equals(donnees.VarianteTheme, "OneDarkPro", StringComparison.OrdinalIgnoreCase)
                ? "OneDarkPro"
                : "";
        }
        catch (Exception)
        {
            ReinitialiserParDefaut();
        }
    }

    public static void Enregistrer()
    {
        try
        {
            Directory.CreateDirectory(_dossier);

            var donnees = new Donnees
            {
                TaillePolice = TaillePolice,
                Theme = Theme,
                DerniersFichiers = DerniersFichiers ?? new List<string>(),
                DernierDossier = DernierDossier ?? "",
                OngletsOuverts = OngletsOuverts ?? new List<string>(),
                OngletActifIndex = OngletActifIndex,
                CleApiGemini = CleApiGemini ?? "",
                ModeleGemini = ModeleGemini ?? "gemini-3.8-flash",
                LargeurPanneauGauche = LargeurPanneauGauche,
                HauteurPanneauBas = HauteurPanneauBas,
                PanneauIAVisible = PanneauIAVisible,
                LargeurPanneauIA = LargeurPanneauIA,
                ExtensionsInstallees = ExtensionsInstallees ?? new List<string>(),
                ExtensionsDesactivees = ExtensionsDesactivees ?? new List<string>(),
                ExtensionsSansMiseAJourAuto = ExtensionsSansMiseAJourAuto ?? new List<string>(),
                VarianteTheme = VarianteTheme ?? ""
            };

            var temporaire = _fichier + ".tmp";
            File.WriteAllText(temporaire, JsonSerializer.Serialize(donnees, _optionsJson));
            File.Move(temporaire, _fichier, overwrite: true);
        }
        catch (Exception)
        {
            // Ignore les erreurs disque
        }
    }

    private static void ReinitialiserParDefaut()
    {
        TaillePolice = TaillePoliceParDefaut;
        Theme = "Sombre";
        DerniersFichiers = new List<string>();
        DernierDossier = "";
        OngletsOuverts = new List<string>();
        OngletActifIndex = 0;
        CleApiGemini = "";
        ModeleGemini = "gemini-3.8-flash";
        LargeurPanneauGauche = 260;
        HauteurPanneauBas = 210;
        PanneauIAVisible = false;
        LargeurPanneauIA = 340;
        ExtensionsInstallees = new List<string>();
        ExtensionsDesactivees = new List<string>();
        ExtensionsSansMiseAJourAuto = new List<string>();
        VarianteTheme = "";
    }

    private static List<string> NettoyerListe(List<string>? liste) =>
        (liste ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string MigrerModeleObsolete(string? modele)
    {
        if (string.IsNullOrWhiteSpace(modele)) return "gemini-3.8-flash";
        var m = modele.Trim().ToLowerInvariant();
        if (m is "gemini-2.0-flash" or "gemini-2.0-flash-exp" or "gemini-2.5-flash")
            return "gemini-3.8-flash";
        if (m is "gemini-2.5-pro")
            return "gemini-3.8-pro";
        return modele.Trim();
    }
}
