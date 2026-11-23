using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using KeyCode.Services;

namespace KeyCode;

/// <summary>
/// Exécuteur universel de fichiers (console, compilés, .NET SDK &amp; .NET Framework, graphiques et pages web).
/// Gère de façon sûre la compilation préalable, les entrées/sorties en direct, les erreurs UTF-8 et l'arrêt.
/// </summary>
public class Runner
{
    private static readonly Encoding _utf8SansBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly object _verrou = new();
    private Process? _process;

    static Runner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
            if (ext == ".cs")
            {
                ExecuterCSharp(fichier, dossier, surSortie, surFin);
                return;
            }

            if (langage.CommandeCompilation != null)
            {
                surSortie($"Compilation de {Path.GetFileName(fichier)}...\n", false);
                if (!CompilerCommande(langage, fichier, dossier, surSortie))
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
        if (csproj == null)
        {
            Lancer("dotnet", $"run \"{fichier}\"", "C#", dossier, surSortie, surFin);
            return;
        }

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

        var (estSdk, estBibliotheque, assemblyName, outputPath) = AnalyserCsproj(csproj);
        if (estBibliotheque)
        {
            surSortie($"Le projet « {Path.GetFileName(csproj)} » est une bibliothèque de classes (OutputType=Library) et ne peut pas être exécuté directement.\n", true);
            surFin(1);
            return;
        }

        var dossierProj = Path.GetDirectoryName(csproj) ?? dossier;
        if (estSdk)
        {
            Lancer("dotnet", $"run --project \"{csproj}\"", "C#", dossierProj, surSortie, surFin);
            return;
        }

        // Projet Visual Studio .NET Framework classique (<TargetFrameworkVersion> sans attribut Sdk)
        ExecuterCsprojNetFramework(csproj, dossierProj, assemblyName, outputPath, surSortie, surFin);
    }

    private void ExecuterCsprojNetFramework(
        string csproj,
        string dossierProj,
        string assemblyName,
        string? outputPath,
        Action<string, bool> surSortie,
        Action<int> surFin)
    {
        var msbuild = TrouverMsBuild();
        if (msbuild == null)
        {
            surSortie("MSBuild est introuvable pour compiler ce projet .NET Framework.\n", true);
            surFin(1);
            return;
        }

        surSortie($"Compilation .NET Framework ({Path.GetFileName(csproj)}) via MSBuild...\n", false);
        var okBuild = CompilerProcessus(
            msbuild,
            $"\"{csproj}\" /t:Build /p:Configuration=Debug /nologo /v:minimal",
            dossierProj,
            surSortie);

        if (!okBuild)
        {
            surFin(1);
            return;
        }

        var exe = TrouverExecutableCompile(dossierProj, assemblyName, outputPath);
        if (exe == null)
        {
            surSortie($"Compilation réussie mais exécutable introuvable dans bin\\ pour « {assemblyName} ».\n", true);
            surFin(1);
            return;
        }

        surSortie($"Lancement de {Path.GetFileName(exe)}...\n", false);
        Encoding? encodageFw = null;
        try
        {
            encodageFw = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception) { }
        Lancer(exe, "", "C# (.NET Framework)", Path.GetDirectoryName(exe) ?? dossierProj, surSortie, surFin, encodageFw);
    }

    private static (bool EstSdk, bool EstBibliotheque, string AssemblyName, string? OutputPath) AnalyserCsproj(string csproj)
    {
        var nomDefaut = Path.GetFileNameWithoutExtension(csproj);
        try
        {
            var doc = XDocument.Load(csproj);
            var root = doc.Root;
            if (root == null) return (true, false, nomDefaut, null);

            var estSdk = root.Attribute("Sdk") != null ||
                         root.Descendants().Any(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks");

            var outputType = root.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "OutputType")?.Value?.Trim();
            var estBibliotheque = string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase);

            var assemblyName = root.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(assemblyName)) assemblyName = nomDefaut;

            var outputPath = root.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "OutputPath")?.Value?.Trim();

            return (estSdk, estBibliotheque, assemblyName, outputPath);
        }
        catch (Exception)
        {
            return (true, false, nomDefaut, null);
        }
    }

    private static string? TrouverMsBuild()
    {
        var racines = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio")
        };

        var candidatsVs = new List<string>();
        foreach (var racine in racines)
        {
            if (!Directory.Exists(racine)) continue;
            try
            {
                foreach (var annee in Directory.GetDirectories(racine))
                {
                    foreach (var edition in Directory.GetDirectories(annee))
                    {
                        var chemin = Path.Combine(edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                        if (File.Exists(chemin))
                            candidatsVs.Add(chemin);
                    }
                }
            }
            catch (Exception) { }
        }

        if (candidatsVs.Count > 0)
            return candidatsVs.OrderByDescending(c => c, StringComparer.OrdinalIgnoreCase).First();

        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var fw64 = Path.Combine(winDir, "Microsoft.NET", "Framework64", "v4.0.30319", "MSBuild.exe");
        if (File.Exists(fw64)) return fw64;

        var fw32 = Path.Combine(winDir, "Microsoft.NET", "Framework", "v4.0.30319", "MSBuild.exe");
        return File.Exists(fw32) ? fw32 : null;
    }

    private static string? TrouverExecutableCompile(string dossierProj, string assemblyName, string? outputPath)
    {
        var cibles = new List<string>();
        if (!string.IsNullOrWhiteSpace(outputPath))
            cibles.Add(Path.GetFullPath(Path.Combine(dossierProj, outputPath, assemblyName + ".exe")));

        cibles.Add(Path.Combine(dossierProj, "bin", "Debug", assemblyName + ".exe"));
        cibles.Add(Path.Combine(dossierProj, "bin", "x64", "Debug", assemblyName + ".exe"));
        cibles.Add(Path.Combine(dossierProj, "bin", "x86", "Debug", assemblyName + ".exe"));
        cibles.Add(Path.Combine(dossierProj, "bin", "Release", assemblyName + ".exe"));

        foreach (var c in cibles)
        {
            if (File.Exists(c)) return c;
        }

        var dossierBin = Path.Combine(dossierProj, "bin");
        if (!Directory.Exists(dossierBin)) return null;

        try
        {
            return Directory.GetFiles(dossierBin, "*.exe", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".vshost.exe", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
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

    private static bool CompilerCommande(LangageInfo langage, string fichier, string dossier, Action<string, bool> surSortie)
    {
        var commande = langage.CommandeCompilation!.Replace("{f}", fichier);
        var ok = CompilerProcessus("cmd", "/s /c \"" + commande + "\"", dossier, surSortie);
        if (!ok)
            surSortie($"Échec de la compilation ({langage.Nom}). Vérifiez les erreurs ci-dessus ou l'installation du compilateur.\n", true);
        return ok;
    }

    private static bool CompilerProcessus(string executable, string arguments, string dossier, Action<string, bool> surSortie)
    {
        var psi = new ProcessStartInfo(executable, arguments)
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
            compilation.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                var estErr = e.Data.Contains("error ", StringComparison.OrdinalIgnoreCase);
                surSortie(e.Data + "\n", estErr);
            };
            compilation.ErrorDataReceived += (_, e) => { if (e.Data != null) surSortie(e.Data + "\n", true); };
            compilation.Start();
            compilation.BeginOutputReadLine();
            compilation.BeginErrorReadLine();
            compilation.WaitForExit();
            return compilation.ExitCode == 0;
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
        Action<int> surFin,
        Encoding? encodageSortie = null)
    {
        var sortieEnc = encodageSortie ?? _utf8SansBom;
        var psi = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = dossier,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = _utf8SansBom,
            StandardOutputEncoding = sortieEnc,
            StandardErrorEncoding = sortieEnc
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
