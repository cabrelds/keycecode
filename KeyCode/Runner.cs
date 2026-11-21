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
            var processus = new Process { StartInfo = psi };
            processus.Start();
            _process = processus;

            // Lecture en direct, sans attendre les retours à la ligne
            var sortie = Task.Run(() => Lire(processus.StandardOutput, surSortie));
            var erreur = Task.Run(() => Lire(processus.StandardError, surSortie));

            processus.WaitForExit();
            Task.WaitAll(sortie, erreur); // on attend d'avoir tout reçu
            surFin();
        }
        catch (Win32Exception)
        {
            surSortie($"{langage.Nom} n'est pas installé (« {langage.Executable} » est introuvable).\n");
            surFin();
        }
        catch (Exception ex)
        {
            surSortie($"Impossible de lancer {langage.Executable} : {ex.Message}\n");
            surFin();
        }
    }

    // Transmet le texte dès qu'il arrive, même sans retour à la ligne
    private static void Lire(StreamReader lecteur, Action<string> surSortie)
    {
        var tampon = new char[256];
        int n;
        while ((n = lecteur.Read(tampon, 0, tampon.Length)) > 0)
            surSortie(new string(tampon, 0, n));
    }    public void Envoyer(string texte)

    {
        if (EnCours) _process!.StandardInput.WriteLine(texte);
    }

    public void Arreter()
    {
        if (EnCours) _process!.Kill(true);
    }
}