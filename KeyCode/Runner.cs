using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using KeyCode.Services;

namespace KeyCode;

/// <summary>
/// Exécuteur universel de fichiers (console, compilés, scripts, graphiques et pages web).
/// Gère de façon sûre la compilation préalable, les entrées/sorties en direct, les erreurs UTF-8 et l'arrêt.
/// </summary>
public class Runner
{
    private readonly object _verrou = new();
    private Process? _process;

    public bool EnCours
    {
        get
        {
            lock (_verrou)
            {
                try
                {
                    return _process is { HasExited: false };
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>Signature historique conservée pour compatibilité de contrat.</summary>
    public void Demarrer(string fichier, Action<string> surSortie, Action surFin)
    {
        Demarrer(fichier, (texte, _) => surSortie(texte), _ => surFin());
    }

    /// <summary>Lance le fichier avec distinction sortie standard / erreur et code de retour.</summary>
    public void Demarrer(string fichier, Action<string, bool> surSortie, Action<int> surFin)
    {
        var ext = Path.GetExtension(fichier).ToLowerInvariant();
        var langage = LangagesService.Obtenir(ext);

        if (langage == null)
        {
            surSortie($"Langage ou format non pris en charge pour l'exécution directe : {ext}\n", true);
            surFin(-1);
            return;
        }

        // Fichiers web / graphiques ouvrables directement dans le navigateur ou visionneur système
        if (langage.EstApercuWeb)
        {
            try
            {
                Process.Start(new ProcessStartInfo(fichier) { UseShellExecute = true });
                surSortie($"Ouverture de {Path.GetFileName(fichier)} dans le navigateur par défaut.\n", false);
                surFin(0);
            }
            catch (Exception ex)
            {
                surSortie($"Impossible d'ouvrir {Path.GetFileName(fichier)} : {ex.Message}\n", true);
                surFin(-1);
            }
            return;
        }

        var dossier = Path.GetDirectoryName(fichier) ?? Environment.CurrentDirectory;

        Task.Run(() =>
        {
            // Cas particulier C# : distingue fichier autonome (.NET 10) et projet .csproj
            if (ext == ".cs")
            {
                ExecuterCSharp(fichier, dossier, surSortie, surFin);
                return;
            }

            if (langage.CommandeCompilation != null)
            {
                surSortie($"Compilation de {Path.GetFileName(fichier)}...\n", false);
                if (!Compiler(langage, fichier, dossier, surSortie))
                {
                    surFin(1);
                    return;
                }
            }

            Lancer(langage.Executable, langage.Arguments.Replace("{f}", fichier), langage.Nom, dossier, surSortie, surFin);
        });
    }

    private void ExecuterCSharp(string fichier, string dossier, Action<string, bool> surSortie, Action<int> surFin)
    {
        var csproj = TrouverCsproj(dossier);
        if (csproj != null)
        {
            // Évite de relancer le propre projet KeyCode si l'utilisateur édite ses sources depuis KeyCode
            if (string.Equals(Path.GetFileNameWithoutExtension(csproj), "KeyCode", StringComparison.OrdinalIgnoreCase))
            {
                var dossierTemp = Path.Combine(Path.GetTempPath(), "keycode_cs_run");
                Directory.CreateDirectory(dossierTemp);
                var copie = Path.Combine(dossierTemp, Path.GetFileName(fichier));
                File.Copy(fichier, copie, overwrite: true);
                Lancer("dotnet", $"run \"{copie}\"", "C#", dossierTemp, surSortie, surFin);
                return;
            }

            Lancer("dotnet", $"run --project \"{csproj}\"", "C#", Path.GetDirectoryName(csproj) ?? dossier, surSortie, surFin);
            return;
        }

        Lancer("dotnet", $"run \"{fichier}\"", "C#", dossier, surSortie, surFin);
    }

    private static string? TrouverCsproj(string dossier)
    {
        var courant = dossier;
        for (var i = 0; i < 4 && !string.IsNullOrEmpty(courant); i++)
        {
            try
            {
                var fichiers = Directory.GetFiles(courant, "*.csproj");
                if (fichiers.Length > 0) return fichiers[0];
                courant = Path.GetDirectoryName(courant);
            }
            catch (Exception)
            {
                break;
            }
        }
        return null;
    }

    private static bool Compiler(LangageInfo langage, string fichier, string dossier, Action<string, bool> surSortie)
    {
        var commande = langage.CommandeCompilation!.Replace("{f}", fichier);
        var psi = new ProcessStartInfo("cmd", "/s /c \"" + commande + "\"")
        {
            WorkingDirectory = dossier,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var compilation = new Process { StartInfo = psi };
            compilation.OutputDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n", false); };
            compilation.ErrorDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n", true); };
            compilation.Start();
            compilation.BeginOutputReadLine();
            compilation.BeginErrorReadLine();
            compilation.WaitForExit();

            if (compilation.ExitCode != 0)
            {
                surSortie($"Échec de la compilation ({langage.Nom}). Vérifiez les erreurs ci-dessus ou l'installation du compilateur.\n", true);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            surSortie($"Impossible de compiler : {ex.Message}\n", true);
            return false;
        }
    }

    private void Lancer(
        string executable,
        string arguments,
        string nomLangage,
        string dossier,
        Action<string, bool> surSortie,
        Action<int> surFin)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = dossier,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        Process? processus = null;
        try
        {
            processus = new Process { StartInfo = psi };
            processus.Start();

            lock (_verrou)
            {
                _process = processus;
            }

            var sortie = Task.Run(() => Lire(processus.StandardOutput, t => surSortie(t, false)));
            var erreur = Task.Run(() => Lire(processus.StandardError, t => surSortie(t, true)));

            processus.WaitForExit();
            Task.WaitAll(sortie, erreur);

            var code = processus.ExitCode;
            if (code == 9009)
                surSortie($"\n{nomLangage} n'est pas installé ou introuvable (« {executable} »).\n", true);

            surFin(code);
        }
        catch (Win32Exception)
        {
            surSortie($"{nomLangage} n'est pas installé (« {executable} » est introuvable dans le PATH).\n", true);
            surFin(-1);
        }
        catch (Exception ex)
        {
            surSortie($"Impossible de lancer {executable} : {ex.Message}\n", true);
            surFin(-1);
        }
        finally
        {
            lock (_verrou)
            {
                if (ReferenceEquals(_process, processus))
                    _process = null;
            }
            try { processus?.Dispose(); }
            catch (Exception) { /* déjà libéré */ }
        }
    }

    private static void Lire(StreamReader lecteur, Action<string> surSortie)
    {
        var tampon = new char[512];
        try
        {
            int n;
            while ((n = lecteur.Read(tampon, 0, tampon.Length)) > 0)
                surSortie(new string(tampon, 0, n));
        }
        catch (Exception)
        {
            // Flux fermé lors d'un arrêt forcé
        }
    }

    public void Envoyer(string texte)
    {
        lock (_verrou)
        {
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.StandardInput.WriteLine(texte);
                    _process.StandardInput.Flush();
                }
            }
            catch (Exception)
            {
                // Le programme vient de se terminer
            }
        }
    }

    public void Arreter()
    {
        Process? cible;
        lock (_verrou)
        {
            cible = _process;
        }

        if (cible == null) return;

        try
        {
            if (!cible.HasExited)
                cible.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Processus déjà terminé ou accès refusé
        }
    }
}
