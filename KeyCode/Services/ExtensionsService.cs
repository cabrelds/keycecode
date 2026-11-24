using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KeyCode.Services;

/// <summary>
/// Catalogue et état des extensions KeyCode (installation, activation, mise à jour automatique).
/// L'état est persistant via SettingsService ; tout changement est signalé par AppEvents.ExtensionsChangees.
/// </summary>
public static class ExtensionsService
{
    public const string IdMaterialIcon = "pkief.material-icon-theme";
    public const string IdOneDarkPro = "zhuangtongfa.material-theme";
    public const string IdErrorLens = "usernamehw.errorlens";
    public const string IdBetterComments = "aaron-bond.better-comments";
    public const string IdCodeRunner = "formulahendry.code-runner";

    // Valeurs d'affichage : celles des captures VS Code ; Better Comments et les tailles sont provisoires.
    private static readonly List<ExtensionInfo> _catalogue =
    [
        new ExtensionInfo
        {
            Id = IdMaterialIcon, Nom = "Material Icon Theme", Editeur = "Philipp Kief",
            Description = "Material Design Icons for Visual Studio Code",
            Version = "5.40.0", Telechargements = 36318255, Note = 5, NombreAvis = 402, Commanditaires = 1,
            Categorie = "Themes", Taille = "6.32 Mo",
            Publie = "il y a 10 ans", DernierePublication = "il y a 4 semaines", DerniereMiseAJour = "il y a 4 semaines",
            Depot = "https://github.com/PKief/vscode-material-icon-theme",
            Details =
            [
                "# Material Icon Theme",
                "Remplace les icônes de fichiers et de dossiers de l'explorateur par les icônes Material Design.",
                "## Fonctionnalités",
                "- Une icône colorée par type de fichier (C#, Python, JavaScript, HTML, JSON, Markdown...)",
                "- Des icônes dédiées aux dossiers courants (src, docs, assets, tests...)",
                "- Suit automatiquement le thème Clair+ ou Sombre+",
            ],
            Fonctionnalites = ["Thèmes d'icônes de fichiers (1) : Material Icon Theme"],
            Journal = ["5.40.0|Intégration native à KeyCode 2.0."]
        },
        new ExtensionInfo
        {
            Id = IdOneDarkPro, Nom = "One Dark Pro", Editeur = "binaryify",
            Description = "Atom's iconic One Dark theme for Visual Studio Code",
            Version = "3.20.7", Telechargements = 12948092, Note = 4.5, NombreAvis = 222,
            Categorie = "Themes", Taille = "1.24 Mo",
            Publie = "il y a 11 ans", DernierePublication = "il y a 4 jours", DerniereMiseAJour = "il y a 4 jours",
            Depot = "https://github.com/Binaryify/OneDark-Pro",
            Details =
            [
                "# One Dark Pro",
                "Le célèbre thème One Dark d'Atom pour KeyCode : couleurs de l'interface, de l'éditeur et du terminal.",
                "## Fonctionnalités",
                "- Interface sombre bleutée (fond #282C34)",
                "- Activé dès l'installation ; retour à Sombre+ à la désactivation",
                "- Sélectionnable aussi depuis Affichage > Thème > One Dark Pro",
            ],
            Fonctionnalites = ["Thèmes de couleurs (1) : One Dark Pro"],
            Commandes = ["Affichage : Thème One Dark Pro|Applique le thème One Dark Pro|"],
            Journal = ["3.20.7|Intégration native à KeyCode 2.0."]
        },
        new ExtensionInfo
        {
            Id = IdErrorLens, Nom = "Error Lens", Editeur = "Alexander",
            Description = "Improve highlighting of errors, warnings and other language diagnostics.",
            Version = "3.30.0", Telechargements = 10372090, Note = 5, NombreAvis = 191,
            Categorie = "Other", Taille = "194.82 Ko",
            Publie = "il y a 8 ans", DernierePublication = "Il y a 1 mois", DerniereMiseAJour = "il y a 7 jours",
            Depot = "https://github.com/usernamehw/vscode-error-lens",
            Details =
            [
                "# Error Lens",
                "ErrorLens met en évidence les diagnostics : la ligne entière est colorée dès qu'un diagnostic existe, et le message est affiché en fin de ligne.",
                "## Fonctionnalités",
                "- Mettre en évidence les lignes contenant des diagnostics",
                "- Afficher le message du diagnostic à la fin de la ligne",
                "- Afficher des icônes dans la gouttière",
                "- Afficher le message dans la barre d'état",
            ],
            Fonctionnalites =
            [
                "Colorer les lignes contenant une erreur ou un avertissement",
                "Message du diagnostic affiché en fin de ligne",
            ],
            Journal = ["3.30.0|Intégration native à KeyCode 2.0."]
        },
        new ExtensionInfo
        {
            Id = IdBetterComments, Nom = "Better Comments", Editeur = "Aaron Bond",
            Description = "Improve your code commenting by annotating with alert, informational, TODOs, and more!",
            Version = "3.0.2", Telechargements = 10250000, Note = 4.5, NombreAvis = 183,
            Categorie = "Other", Taille = "142.10 Ko",
            Publie = "il y a 8 ans", DernierePublication = "il y a 2 ans", DerniereMiseAJour = "il y a 2 ans",
            Depot = "https://github.com/aaron-bond/better-comments",
            Details =
            [
                "# Better Comments",
                "Améliore la lisibilité des commentaires grâce à des annotations colorées.",
                "## Annotations reconnues",
                "> // ! Alerte (rouge)",
                "> // ? Question (bleu)",
                "> // TODO Tâche à faire (orange)",
                "> // * Information importante (vert)",
                "> // // Code mis de côté (barré)",
            ],
            Fonctionnalites = ["Coloration des commentaires selon leur annotation"],
            Journal = ["3.0.2|Intégration native à KeyCode 2.0."]
        },
        new ExtensionInfo
        {
            Id = IdCodeRunner, Nom = "Code Runner", Editeur = "Jun Han",
            Description = "Run C, C++, Java, JS, PHP, Python, Perl, Ruby, Go, Lua, Groovy, PowerShell, CMD, BASH, F#, C#, VBScript, TypeScript, CoffeeScript, Scala, Swift, Julia, Crystal, OCaml, R, AppleScript, Elixir, VB.NET, Clojure, Haxe, Obj-C, Rust, Racket, Scheme, AutoHotkey, AutoIt, Kotlin, Dart, Pascal, Haskell, Nim, D, Lisp, Kit, V, SCSS, Sass, CUDA, Less, Fortran, Ring, Standard ML, Zig, Mojo, Erlang, SPWN, Pkl, Gleam",
            Version = "0.12.2", Telechargements = 42915700, Note = 4.5, NombreAvis = 286, Commanditaires = 1,
            Categorie = "Programming Languages", Taille = "412.50 Ko",
            Publie = "il y a 10 ans", DernierePublication = "il y a 3 ans", DerniereMiseAJour = "il y a 3 ans",
            Depot = "https://github.com/formulahendry/vscode-code-runner",
            Details =
            [
                "# Code Runner",
                "Exécute un fragment de code ou un fichier complet pour de nombreux langages.",
                "## Utilisation",
                "- Sélectionner du code puis appuyer sur Ctrl+Alt+N pour exécuter uniquement la sélection",
                "- Sans sélection, le fichier actif est enregistré puis exécuté",
                "- La sortie s'affiche dans le panneau Terminal (vue Exécution)",
                "- C# et Java : la sélection seule n'est pas prise en charge, exécutez le fichier complet",
                "## Langages pris en charge",
                "Python, JavaScript, TypeScript, Java, C, C++, C#, PHP, PowerShell, Batch, Go, Rust, HTML (aperçu).",
            ],
            Fonctionnalites =
            [
                "Exécuter la sélection de code",
                "Exécuter le fichier actif si rien n'est sélectionné",
            ],
            Commandes =
            [
                "Code Runner : Exécuter le code|Exécute la sélection ou le fichier actif|Ctrl+Alt+N",
                "Exécuter : Arrêter l'exécution|Arrête le programme en cours|",
            ],
            Journal = ["0.12.2|Intégration native à KeyCode 2.0."]
        },
    ];

    private static readonly HashSet<string> _operations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> _tempsActivation = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex _jetons = new("(?:[^\\s\"]+|\"[^\"]*\")+", RegexOptions.Compiled);
    private static bool _actualisation;

    public static IReadOnlyList<ExtensionInfo> Catalogue => _catalogue;

    /// <summary>Vrai pendant une installation ou une actualisation (sert au badge de la barre d'activité).</summary>
    public static bool OperationEnCours => _actualisation || _operations.Count > 0;

    public static bool EstEnCours(string id) => _operations.Contains(id);

    public static ExtensionInfo? Obtenir(string id) =>
        _catalogue.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool EstInstallee(string id) => Contient(SettingsService.ExtensionsInstallees, id);

    public static bool EstActive(string id) =>
        EstInstallee(id) && !Contient(SettingsService.ExtensionsDesactivees, id);

    public static bool MiseAJourAuto(string id) => !Contient(SettingsService.ExtensionsSansMiseAJourAuto, id);

    public static int TempsActivationMs(string id) => _tempsActivation.TryGetValue(id, out var ms) ? ms : 0;

    /// <summary>À appeler au démarrage, après SettingsService.Charger() et avant ThemeManager.Appliquer().</summary>
    public static void Initialiser()
    {
        SettingsService.ExtensionsInstallees ??= new List<string>();
        SettingsService.ExtensionsDesactivees ??= new List<string>();
        SettingsService.ExtensionsSansMiseAJourAuto ??= new List<string>();

        SettingsService.ExtensionsInstallees.RemoveAll(i => Obtenir(i) == null);
        SettingsService.ExtensionsDesactivees.RemoveAll(i => !EstInstallee(i));
        SettingsService.ExtensionsSansMiseAJourAuto.RemoveAll(i => !EstInstallee(i));

        ThemeManager.DefinirVariante("");
        foreach (var info in _catalogue.Where(i => EstActive(i.Id)))
        {
            var chrono = Stopwatch.StartNew();
            ActiverAuDemarrage(info.Id);
            _tempsActivation[info.Id] = Math.Max(1, (int)chrono.ElapsedMilliseconds);
        }
    }

    private static void ActiverAuDemarrage(string id)
    {
        if (string.Equals(id, IdOneDarkPro, StringComparison.OrdinalIgnoreCase)
            && SettingsService.VarianteTheme == "OneDarkPro"
            && SettingsService.Theme == "Sombre")
        {
            ThemeManager.DefinirVariante("OneDarkPro");
        }
        // Les effets des autres extensions seront branchés ici (lot 2).
    }

    public static async Task InstallerAsync(string id)
    {
        if (Obtenir(id) == null || EstInstallee(id) || !_operations.Add(id)) return;
        AppEvents.SignalerExtensionsChangees();

        try
        {
            await Task.Delay(900); // simulation du téléchargement
            var chrono = Stopwatch.StartNew();
            Ajouter(SettingsService.ExtensionsInstallees, id);
            Retirer(SettingsService.ExtensionsDesactivees, id);
            AppliquerEffets(id);
            _tempsActivation[id] = Math.Max(1, (int)chrono.ElapsedMilliseconds);
        }
        finally
        {
            _operations.Remove(id);
        }

        SettingsService.Enregistrer();
        AppEvents.SignalerExtensionsChangees();
    }

    public static void Desinstaller(string id)
    {
        if (!EstInstallee(id)) return;
        Retirer(SettingsService.ExtensionsInstallees, id);
        Retirer(SettingsService.ExtensionsDesactivees, id);
        Retirer(SettingsService.ExtensionsSansMiseAJourAuto, id);
        _tempsActivation.Remove(id);
        AppliquerEffets(id);
        SettingsService.Enregistrer();
        AppEvents.SignalerExtensionsChangees();
    }

    public static void Activer(string id)
    {
        if (!EstInstallee(id)) return;
        Retirer(SettingsService.ExtensionsDesactivees, id);
        _tempsActivation[id] = 1;
        AppliquerEffets(id);
        SettingsService.Enregistrer();
        AppEvents.SignalerExtensionsChangees();
    }

    public static void Desactiver(string id)
    {
        if (!EstInstallee(id)) return;
        Ajouter(SettingsService.ExtensionsDesactivees, id);
        _tempsActivation.Remove(id);
        AppliquerEffets(id);
        SettingsService.Enregistrer();
        AppEvents.SignalerExtensionsChangees();
    }

    public static void ToutDesactiver()
    {
        foreach (var id in SettingsService.ExtensionsInstallees.ToList())
        {
            Ajouter(SettingsService.ExtensionsDesactivees, id);
            _tempsActivation.Remove(id);
            AppliquerEffets(id);
        }
        SettingsService.Enregistrer();
        AppEvents.SignalerExtensionsChangees();
    }

    public static void ToutActiver()
    {
        foreach (var id in SettingsService.ExtensionsInstallees.ToList())
        {
            Retirer(SettingsService.ExtensionsDesactivees, id);
            _tempsActivation[id] = 1;
            AppliquerEffets(id);
        }
        SettingsService.Enregistrer();
        AppEvents.SignalerExtensionsChangees();
    }

    public static void DefinirMiseAJourAuto(string id, bool actif)
    {
        if (!EstInstallee(id)) return;
        if (actif) Retirer(SettingsService.ExtensionsSansMiseAJourAuto, id);
        else Ajouter(SettingsService.ExtensionsSansMiseAJourAuto, id);
        SettingsService.Enregistrer();
        AppEvents.SignalerExtensionsChangees();
    }

    /// <summary>Simule l'actualisation de la Place de marché (fait tourner le badge de la barre d'activité).</summary>
    public static async Task ActualiserAsync()
    {
        if (_actualisation) return;
        _actualisation = true;
        AppEvents.SignalerExtensionsChangees();
        await Task.Delay(700);
        _actualisation = false;
        AppEvents.SignalerExtensionsChangees();
    }

    /// <summary>
    /// Recherche dans le catalogue. Filtres : @installed, @enabled, @disabled, @recommended,
    /// @category:"nom", @sort:installs|rating|name ; les autres mots filtrent nom, id, éditeur et description.
    /// </summary>
    public static List<ExtensionInfo> Rechercher(string? requete)
    {
        IEnumerable<ExtensionInfo> resultat = _catalogue;
        var mots = new List<string>();
        var tri = "";

        foreach (Match m in _jetons.Matches(requete ?? ""))
        {
            var jeton = m.Value.Trim().ToLowerInvariant();
            if (jeton == "@installed") resultat = resultat.Where(e => EstInstallee(e.Id));
            else if (jeton == "@enabled") resultat = resultat.Where(e => EstActive(e.Id));
            else if (jeton == "@disabled") resultat = resultat.Where(e => EstInstallee(e.Id) && !EstActive(e.Id));
            else if (jeton == "@recommended") resultat = resultat.Where(e => !EstInstallee(e.Id));
            else if (jeton.StartsWith("@category:", StringComparison.Ordinal))
            {
                var categorie = jeton["@category:".Length..].Trim('"');
                resultat = resultat.Where(e => string.Equals(e.Categorie, categorie, StringComparison.OrdinalIgnoreCase));
            }
            else if (jeton.StartsWith("@sort:", StringComparison.Ordinal)) tri = jeton["@sort:".Length..];
            else mots.Add(jeton.Trim('"'));
        }

        foreach (var mot in mots)
        {
            resultat = resultat.Where(e =>
                (e.Nom + " " + e.Id + " " + e.Editeur + " " + e.Description).Contains(mot, StringComparison.OrdinalIgnoreCase));
        }

        resultat = tri switch
        {
            "rating" => resultat.OrderByDescending(e => e.Note).ThenByDescending(e => e.NombreAvis),
            "name" => resultat.OrderBy(e => e.Nom, StringComparer.CurrentCultureIgnoreCase),
            _ => resultat.OrderByDescending(e => e.Telechargements)
        };

        return resultat.ToList();
    }

    /// <summary>Applique les effets réels d'une extension selon son état courant.</summary>
    private static void AppliquerEffets(string id)
    {
        if (string.Equals(id, IdOneDarkPro, StringComparison.OrdinalIgnoreCase))
        {
            if (EstActive(id))
            {
                SettingsService.Theme = "Sombre";
                SettingsService.VarianteTheme = "OneDarkPro";
                ThemeManager.AppliquerVariante("OneDarkPro");
            }
            else if (ThemeManager.Variante == "OneDarkPro")
            {
                SettingsService.VarianteTheme = "";
                ThemeManager.AppliquerVariante("");
            }
        }
        // Les effets des autres extensions seront branchés ici (lot 2).
    }

    private static bool Contient(List<string>? liste, string id) =>
        liste != null && liste.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

    private static void Ajouter(List<string> liste, string id)
    {
        if (!Contient(liste, id)) liste.Add(id);
    }

    private static void Retirer(List<string> liste, string id) =>
        liste.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
}