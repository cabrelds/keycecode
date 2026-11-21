using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using KeyCode.Services;

namespace KeyCode;

public class Runner
{
    private Process? _process;

    public bool EnCours => _process is { HasExited: false };

    public void Demarrer(string fichier, Action<string> surSortie, Action surFin)
    {
        var ext = Path.GetExtension(fichier).ToLower();
        var langage = LangagesService.Obtenir(ext);

        // Langage inconnu : on prévient et on s'arrête
        if (langage == null)
        {
            surSortie($"Langage non supporté : {ext}\n");
            surFin();
            return;
        }

        var dossier = Path.GetDirectoryName(fichier) ?? Environment.CurrentDirectory;

        // Task.Run : la compilation ne doit pas figer la fenêtre
        Task.Run(() =>
        {
            if (langage.CommandeCompilation != null)
            {
                surSortie($"Compilation de {Path.GetFileName(fichier)}...\n");
                if (!Compiler(langage, fichier, dossier, surSortie))
                {
                    surFin();
                    return;
                }
            }
            Lancer(langage, fichier, dossier, surSortie, surFin);
        });
    }

    private bool Compiler(LangageInfo langage, string fichier, string dossier, Action<string> surSortie)
    {
        var commande = langage.CommandeCompilation!.Replace("{f}", fichier);
        var psi = new ProcessStartInfo("cmd", "/c " + commande)
        {
            WorkingDirectory = dossier,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var compilation = new Process { StartInfo = psi };
            compilation.OutputDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n"); };
            compilation.ErrorDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n"); };
            compilation.Start();
            compilation.BeginOutputReadLine();
            compilation.BeginErrorReadLine();
            compilation.WaitForExit();

            if (compilation.ExitCode != 0)
            {
                surSortie($"Échec de la compilation. {langage.Nom} est-il installé ?\n");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            surSortie($"Impossible de compiler : {ex.Message}\n");
            return false;
        }
    }

    private void Lancer(LangageInfo langage, string fichier, string dossier,
                        Action<string> surSortie, Action surFin)
    {
        var psi = new ProcessStartInfo(langage.Executable, langage.Arguments.Replace("{f}", fichier))
        {
            WorkingDirectory = dossier,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n"); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n"); };
            _process.Exited += (_, _) => surFin();
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Win32Exception)
        {
            // Windows ne trouve pas le programme : le langage n'est pas installé
            surSortie($"{langage.Nom} n'est pas installé (« {langage.Executable} » est introuvable).\n");
            surFin();
        }
        catch (Exception ex)
        {
            surSortie($"Impossible de lancer {langage.Executable} : {ex.Message}\n");
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