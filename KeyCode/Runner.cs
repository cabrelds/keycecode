using System.Diagnostics;
using System.IO;

namespace KeyCode;

public static class Runner
{
    private static readonly Dictionary<string, (string Exe, string Args)> Langages = new()
    {
        [".py"]   = ("python", "\"{f}\""),
        [".js"]   = ("node", "\"{f}\""),
        [".cs"]   = ("dotnet", "run \"{f}\""),
        [".java"] = ("java", "\"{f}\""),
        [".ps1"]  = ("powershell", "-File \"{f}\""),
        [".bat"]  = ("cmd", "/c \"{f}\""),
    };

    public static async Task<string> ExecuterAsync(string fichier)
    {
        var ext = Path.GetExtension(fichier).ToLower();
        if (!Langages.TryGetValue(ext, out var l))
            return $"Langage non supporté : {ext}";

        var psi = new ProcessStartInfo(l.Exe, l.Args.Replace("{f}", fichier))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(fichier)!
        };

        try
        {
            using var p = Process.Start(psi)!;
            var sortie = await p.StandardOutput.ReadToEndAsync();
            var erreur = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return sortie + erreur;
        }
        catch (Exception ex)
        {
            return $"Impossible de lancer {l.Exe} : {ex.Message}";
        }
    }
}