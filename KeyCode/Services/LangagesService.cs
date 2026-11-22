namespace KeyCode.Services;

public class LangageInfo
{
    public string Nom { get; init; } = "";
    public string Extension { get; init; } = "";
    public string Executable { get; init; } = "";
    public string Arguments { get; init; } = "\"{f}\"";
    public string? CommandeCompilation { get; init; }
}

public static class LangagesService
{
    private static readonly Dictionary<string, LangageInfo> _langages = new()
    {
        [".py"]   = new() { Nom = "Python",     Extension = ".py",   Executable = "python",     Arguments = "-u \"{f}\"" },
        [".js"]   = new() { Nom = "JavaScript", Extension = ".js",   Executable = "node" },
        [".cs"]   = new() { Nom = "C#",         Extension = ".cs",   Executable = "dotnet",     Arguments = "run \"{f}\"" },
        [".java"] = new() { Nom = "Java",       Extension = ".java", Executable = "java" },
        [".ps1"]  = new() { Nom = "PowerShell", Extension = ".ps1",  Executable = "powershell", Arguments = "-File \"{f}\"" },
        [".bat"]  = new() { Nom = "Batch",      Extension = ".bat",  Executable = "cmd",        Arguments = "/c \"{f}\"" },
[".php"] = new() { Nom = "PHP", Extension = ".php", Executable = "php" },
[".go"] = new() { Nom = "Go", Extension = ".go", Executable = "go", Arguments = "run \"{f}\"" },
};
    public static LangageInfo? Obtenir(string extension) =>
        _langages.TryGetValue(extension.ToLower(), out var l) ? l : null;
}