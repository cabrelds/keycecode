using System.Diagnostics;
using System.IO;

namespace KeyCode;

public class Runner
{
    private Process? _process;

    private static readonly Dictionary<string, (string Exe, string Args)> Langages = new()
    {
        [".py"]   = ("python", "-u \"{f}\""),
        [".js"]   = ("node", "\"{f}\""),
        [".cs"]   = ("dotnet", "run \"{f}\""),
        [".java"] = ("java", "\"{f}\""),
        [".ps1"]  = ("powershell", "-File \"{f}\""),
        [".bat"]  = ("cmd", "/c \"{f}\""),
    };

    public bool EnCours => _process is { HasExited: false };

    public void Demarrer(string fichier, Action<string> surSortie, Action surFin)
    {
        var ext = Path.GetExtension(fichier).ToLower();
        if (!Langages.TryGetValue(ext, out var l))
        {
            surSortie($"Langage non supporté : {ext}\n");
            surFin();
            return;
        }

        var psi = new ProcessStartInfo(l.Exe, l.Args.Replace("{f}", fichier))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(fichier)!
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n"); };
            _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n"); };
            _process.Exited += (_, _) => surFin();
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            surSortie($"Impossible de lancer {l.Exe} : {ex.Message}\n");
            surFin();
        }
    }

    public void Envoyer(string texte)
    {
        if (EnCours) _process!.StandardInput.WriteLine(texte);
    }

    public void Arreter()
    {
        if (EnCours) _process!.Kill(true);
    }
}