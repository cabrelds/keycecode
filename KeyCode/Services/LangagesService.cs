namespace KeyCode.Services;

/// <summary>
/// Description d'un langage exécutable ou prévisualisable (lue par le Runner).
/// </summary>
public class LangageInfo
{
    public string Nom { get; init; } = "";
    public string Extension { get; init; } = "";
    public string Executable { get; init; } = "";
    public string Arguments { get; init; } = "\"{f}\"";
    public string? CommandeCompilation { get; init; }
    public bool EstApercuWeb { get; init; }
}

/// <summary>
/// Liste complète des langages et formats que KeyCode sait exécuter ou lancer.
/// </summary>
public static class LangagesService
{
    private static readonly LangageInfo _c = new()
    {
        Nom = "C", Extension = ".c",
        Executable = "cmd", Arguments = "/s /c \"\"%TEMP%\\keycode_c.exe\"\"",
        CommandeCompilation = "gcc \"{f}\" -o \"%TEMP%\\keycode_c.exe\""
    };

    private static readonly LangageInfo _cpp = new()
    {
        Nom = "C++", Extension = ".cpp",
        Executable = "cmd", Arguments = "/s /c \"\"%TEMP%\\keycode_cpp.exe\"\"",
        CommandeCompilation = "g++ \"{f}\" -o \"%TEMP%\\keycode_cpp.exe\""
    };

    private static readonly LangageInfo _python = new()
    {
        Nom = "Python", Extension = ".py", Executable = "python", Arguments = "-u \"{f}\""
    };

    private static readonly LangageInfo _js = new()
    {
        Nom = "JavaScript", Extension = ".js", Executable = "node"
    };

    private static readonly LangageInfo _ts = new()
    {
        Nom = "TypeScript", Extension = ".ts", Executable = "node", Arguments = "--experimental-strip-types \"{f}\""
    };

    private static readonly LangageInfo _html = new()
    {
        Nom = "HTML", Extension = ".html", Executable = "navigateur", EstApercuWeb = true
    };

    private static readonly Dictionary<string, LangageInfo> _langages = new()
    {
        [".py"]   = _python,
        [".pyw"]  = _python,
        [".js"]   = _js,
        [".mjs"]  = _js,
        [".cjs"]  = _js,
        [".ts"]   = _ts,
        [".tsx"]  = _ts,
        [".cs"]   = new() { Nom = "C#",         Extension = ".cs",   Executable = "dotnet",     Arguments = "run \"{f}\"" },
        [".java"] = new() { Nom = "Java",       Extension = ".java", Executable = "java" },
        [".c"]    = _c,
        [".cpp"]  = _cpp,
        [".cc"]   = _cpp,
        [".cxx"]  = _cpp,
        [".php"]  = new() { Nom = "PHP",        Extension = ".php",  Executable = "php" },
        [".ps1"]  = new() { Nom = "PowerShell", Extension = ".ps1",  Executable = "powershell", Arguments = "-ExecutionPolicy Bypass -File \"{f}\"" },
        [".bat"]  = new() { Nom = "Batch",      Extension = ".bat",  Executable = "cmd",        Arguments = "/c \"\"{f}\"\"" },
        [".cmd"]  = new() { Nom = "Batch",      Extension = ".cmd",  Executable = "cmd",        Arguments = "/c \"\"{f}\"\"" },
        [".go"]   = new() { Nom = "Go",         Extension = ".go",   Executable = "go",         Arguments = "run \"{f}\"" },
        [".rs"]   = new()
        {
            Nom = "Rust", Extension = ".rs",
            Executable = "cmd", Arguments = "/s /c \"\"%TEMP%\\keycode_rs.exe\"\"",
            CommandeCompilation = "rustc \"{f}\" -o \"%TEMP%\\keycode_rs.exe\""
        },
        [".html"] = _html,
        [".htm"]  = _html,
        [".svg"]  = new() { Nom = "SVG", Extension = ".svg", Executable = "navigateur", EstApercuWeb = true },
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
