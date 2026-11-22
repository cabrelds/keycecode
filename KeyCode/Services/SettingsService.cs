using System.IO;
using System.Text.Json;

namespace KeyCode.Services;

/// <summary>
/// Paramètres de l'application, sauvegardés en JSON dans %AppData%\KeyCode\settings.json.
/// Propriétaire : nyangono (module repris par cabrelds).
/// </summary>
public static class SettingsService
{
    private const double TaillePoliceParDefaut = 14;
    private const double TaillePoliceMin = 8;
    private const double TaillePoliceMax = 48;

    public static double TaillePolice { get; set; } = TaillePoliceParDefaut;
    public static string Theme { get; set; } = "Clair";
    public static List<string> DerniersFichiers { get; set; } = new();
    public static string DernierDossier { get; set; } = "";

    private static readonly string _dossier =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyCode");

    private static readonly string _fichier = Path.Combine(_dossier, "settings.json");

    private static readonly JsonSerializerOptions _optionsJson = new() { WriteIndented = true };

    // Forme du fichier JSON (classe privée : seule cette classe sait comment le fichier est écrit)
    private sealed class Donnees
    {
        public double TaillePolice { get; set; } = TaillePoliceParDefaut;
        public string? Theme { get; set; } = "Clair";
        public List<string>? DerniersFichiers { get; set; } = new();
        public string? DernierDossier { get; set; } = "";
    }

    /// <summary>
    /// Lit le fichier de paramètres. Si le fichier manque ou est invalide (corrompu, mal formé,
    /// valeurs incorrectes), les valeurs par défaut sont utilisées : aucune exception n'en sort.
    /// </summary>
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

            Theme = string.Equals(donnees.Theme, "Sombre", StringComparison.OrdinalIgnoreCase) ? "Sombre" : "Clair";

            DerniersFichiers = (donnees.DerniersFichiers ?? new List<string>())
                .Where(chemin => !string.IsNullOrWhiteSpace(chemin))
                .ToList();

            DernierDossier = donnees.DernierDossier ?? "";
        }
        catch (Exception)
        {
            // Fichier illisible ou invalide : on repart sur les valeurs par défaut
            ReinitialiserParDefaut();
        }
    }

    /// <summary>
    /// Écrit le fichier de paramètres. Écriture dans un fichier temporaire puis remplacement,
    /// pour ne jamais laisser un fichier à moitié écrit. Une erreur d'écriture ne plante pas l'application.
    /// </summary>
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
                DernierDossier = DernierDossier ?? ""
            };

            var temporaire = _fichier + ".tmp";
            File.WriteAllText(temporaire, JsonSerializer.Serialize(donnees, _optionsJson));
            File.Move(temporaire, _fichier, overwrite: true);
        }
        catch (Exception)
        {
            // Disque plein, droits insuffisants... : les paramètres ne sont pas sauvegardés, c'est tout
        }
    }

    private static void ReinitialiserParDefaut()
    {
        TaillePolice = TaillePoliceParDefaut;
        Theme = "Clair";
        DerniersFichiers = new List<string>();
        DernierDossier = "";
    }
}
