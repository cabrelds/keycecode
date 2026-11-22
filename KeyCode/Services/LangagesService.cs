namespace KeyCode.Services;

/// <summary>
/// Description d'un langage exécutable (lue par le Runner).
/// </summary>
public class LangageInfo
{
    public string Nom { get; init; } = "";
    public string Extension { get; init; } = "";
    public string Executable { get; init; } = "";
    public string Arguments { get; init; } = "\"{f}\"";
    public string? CommandeCompilation { get; init; }
}

/// <summary>
/// Liste des langages que KeyCode sait exécuter.
/// Propriétaire : nyangono (module repris par cabrelds).
/// </summary>
public static class LangagesService
{
    // Pour les langages compilés (C, C++, Rust) : l'exécutable est écrit dans le dossier temporaire de
    // Windows (%TEMP%) afin de ne pas polluer le dossier de l'utilisateur. Le Runner exécute la
    // compilation avec "cmd /c", donc %TEMP% est bien remplacé. Pour lancer le programme, on passe
    // aussi par "cmd /c" (l'Executable ne peut pas contenir de variable d'environnement).
    private static readonly LangageInfo _c = new()
    {
        Nom = "C", Extension = ".c",
        Executable = "cmd", Arguments = "/c \"%TEMP%\\keycode_c.exe\"",
        CommandeCompilation = "gcc \"{f}\" -o \"%TEMP%\\keycode_c.exe\""
    };

    private static readonly LangageInfo _cpp = new()
    {
        Nom = "C++", Extension = ".cpp",
        Executable = "cmd", Arguments = "/c \"%TEMP%\\keycode_cpp.exe\"",
        CommandeCompilation = "g++ \"{f}\" -o \"%TEMP%\\keycode_cpp.exe\""
    };

    private static readonly Dictionary<string, LangageInfo> _langages = new()
    {
        [".py"]   = new() { Nom = "Python",     Extension = ".py",   Executable = "python",     Arguments = "-u \"{f}\"" },
        [".js"]   = new() { Nom = "JavaScript", Extension = ".js",   Executable = "node" },
        [".cs"]   = new() { Nom = "C#",         Extension = ".cs",   Executable = "dotnet",     Arguments = "run \"{f}\"" },
        // Java : le mode "fichier source unique" (JDK 11 et plus) compile et lance en une seule commande
        [".java"] = new() { Nom = "Java",       Extension = ".java", Executable = "java" },
        [".c"]    = _c,
        [".cpp"]  = _cpp,
        [".cc"]   = _cpp,
        [".cxx"]  = _cpp,
        [".php"]  = new() { Nom = "PHP",        Extension = ".php",  Executable = "php" },
        [".ps1"]  = new() { Nom = "PowerShell", Extension = ".ps1",  Executable = "powershell", Arguments = "-File \"{f}\"" },
        [".bat"]  = new() { Nom = "Batch",      Extension = ".bat",  Executable = "cmd",        Arguments = "/c \"{f}\"" },
        [".go"]   = new() { Nom = "Go",         Extension = ".go",   Executable = "go",         Arguments = "run \"{f}\"" },
        [".rs"]   = new()
        {
            Nom = "Rust", Extension = ".rs",
            Executable = "cmd", Arguments = "/c \"%TEMP%\\keycode_rs.exe\"",
            CommandeCompilation = "rustc \"{f}\" -o \"%TEMP%\\keycode_rs.exe\""
        },
    };

    /// <summary>Retourne le langage associé à une extension (".py", ".C"...), ou null si inconnu.</summary>
    public static LangageInfo? Obtenir(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        extension = extension.Trim().ToLowerInvariant();
        if (!extension.StartsWith('.')) extension = "." + extension;
        return _langages.TryGetValue(extension, out var langage) ? langage : null;
    }
}
