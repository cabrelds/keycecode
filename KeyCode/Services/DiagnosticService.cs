using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace KeyCode.Services;

public sealed record DiagnosticItem(
    string Chemin,
    int Ligne,
    int Colonne,
    int Longueur,
    SeveriteDiagnostic Severite,
    string Message,
    string Source);

/// <summary>
/// Analyse syntaxique en direct (équilibre des délimiteurs, JSON, XML/XAML, Python, JS, PHP)
/// et extraction des erreurs depuis les sorties de compilation / exécution du terminal.
/// </summary>
public static class DiagnosticService
{
    private static readonly object _verrou = new();
    private static readonly Dictionary<string, List<DiagnosticItem>> _parFichier = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<DiagnosticItem> _erreursExecution = new();

    public static string DernierJournalExecution { get; set; } = "";

    private static readonly Regex _rxPythonFile = new(@"File ""([^""]+)"", line (\d+)", RegexOptions.Compiled);
    private static readonly Regex _rxMsbuild = new(@"^([^\(\r\n]+)\((\d+),(\d+)\):\s*(error|warning)\s+([A-Z0-9]+):\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _rxGccRust = new(@"^([A-Za-z]:[^:\r\n]+|[^:\r\n]+\.[A-Za-z0-9]+):(\d+):(?:(\d+):)?\s*(fatal error|error|warning):\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _rxGo = new(@"^([^\r\n:]+\.go):(\d+):(?:(\d+):)?\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex _rxNode = new(@"^([^\r\n]+\.(?:js|mjs|cjs|ts|tsx)):(\d+)(?::(\d+))?$", RegexOptions.Compiled);
    private static readonly Regex _rxPhp = new(@"(Parse|Fatal|Warning)[^:]*:\s*(.+?)\s+in\s+(.+?)\s+on line\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _rxPyCompile = new(@"line (\d+)", RegexOptions.Compiled);

    public static IReadOnlyList<DiagnosticItem> ObtenirTous()
    {
        lock (_verrou)
        {
            var liste = new List<DiagnosticItem>();
            foreach (var item in _parFichier.Values)
                liste.AddRange(item);
            liste.AddRange(_erreursExecution);
            return liste;
        }
    }

    public static IReadOnlyList<DiagnosticItem> ObtenirPourFichier(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return Array.Empty<DiagnosticItem>();
        var norm = Normaliser(chemin);
        lock (_verrou)
        {
            var liste = new List<DiagnosticItem>();
            if (_parFichier.TryGetValue(norm, out var locaux))
                liste.AddRange(locaux);
            liste.AddRange(_erreursExecution.Where(e =>
                string.Equals(Normaliser(e.Chemin), norm, StringComparison.OrdinalIgnoreCase)));
            return liste;
        }
    }

    public static (int Erreurs, int Avertissements) Compter()
    {
        var tous = ObtenirTous();
        var err = tous.Count(d => d.Severite == SeveriteDiagnostic.Erreur);
        var warn = tous.Count(d => d.Severite == SeveriteDiagnostic.Avertissement);
        return (err, warn);
    }

    public static void EffacerErreursExecution()
    {
        lock (_verrou)
        {
            _erreursExecution.Clear();
            DernierJournalExecution = "";
        }
        AppEvents.SignalerDiagnosticsChanges();
    }

    public static void SupprimerFichier(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return;
        var norm = Normaliser(chemin);
        lock (_verrou)
        {
            _parFichier.Remove(norm);
        }
        AppEvents.SignalerDiagnosticsChanges();
    }

    /// <summary>Analyse complète d'un document en arrière-plan (sans bloquer l'interface).</summary>
    public static async Task<List<DiagnosticItem>> AnalyserDocumentAsync(string? chemin, string texte, CancellationToken jeton)
    {
        var cle = string.IsNullOrWhiteSpace(chemin) ? "__sans_titre__" : Normaliser(chemin);
        var ext = string.IsNullOrWhiteSpace(chemin) ? "" : Path.GetExtension(chemin).ToLowerInvariant();

        var resultats = await Task.Run(() =>
        {
            var liste = new List<DiagnosticItem>();
            if (jeton.IsCancellationRequested) return liste;

            if (ext == ".json")
            {
                VerifierJson(cle, texte, liste);
            }
            else if (ext is ".xml" or ".xaml" or ".csproj" or ".svg" or ".config")
            {
                VerifierXml(cle, texte, liste);
            }
            else if (EstLangageCode(ext))
            {
                VerifierDelimiteurs(cle, ext, texte, liste);
            }

            if (jeton.IsCancellationRequested) return liste;

            if (ext is ".py" or ".pyw")
                VerifierPythonExterne(cle, texte, liste);
            else if (ext is ".js" or ".mjs" or ".cjs")
                VerifierNodeExterne(cle, texte, liste);
            else if (ext == ".php")
                VerifierPhpExterne(cle, texte, liste);

            return liste;
        }, jeton).ConfigureAwait(false);

        if (jeton.IsCancellationRequested) return resultats;

        lock (_verrou)
        {
            _parFichier[cle] = resultats;
        }
        AppEvents.SignalerDiagnosticsChanges();
        return resultats;
    }

    /// <summary>Extrait les erreurs/avertissements d'une sortie terminal et met à jour les diagnostics.</summary>
    public static void AnalyserSortieExecution(string sortieTotale, string? fichierParDefaut)
    {
        if (string.IsNullOrWhiteSpace(sortieTotale)) return;

        var trouves = new List<DiagnosticItem>();
        var lignes = sortieTotale.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lignes.Length; i++)
        {
            var ligne = lignes[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(ligne)) continue;

            // Python traceback : File "x.py", line 12
            var mPy = _rxPythonFile.Match(ligne);
            if (mPy.Success && int.TryParse(mPy.Groups[2].Value, out var lignePy))
            {
                var cheminPy = ResoudreChemin(mPy.Groups[1].Value, fichierParDefaut);
                var message = ChercherErreurPythonSuivante(lignes, i);
                trouves.Add(new DiagnosticItem(cheminPy, lignePy, 1, 80, SeveriteDiagnostic.Erreur, message, "Python"));
                continue;
            }

            // C# / MSBuild : file.cs(12,5): error CS1002: ...
            var mMs = _rxMsbuild.Match(ligne);
            if (mMs.Success && int.TryParse(mMs.Groups[2].Value, out var lMs) && int.TryParse(mMs.Groups[3].Value, out var cMs))
            {
                var sev = mMs.Groups[4].Value.Equals("warning", StringComparison.OrdinalIgnoreCase)
                    ? SeveriteDiagnostic.Avertissement : SeveriteDiagnostic.Erreur;
                var cheminCs = ResoudreChemin(mMs.Groups[1].Value.Trim(), fichierParDefaut);
                trouves.Add(new DiagnosticItem(cheminCs, lMs, cMs, 40, sev, $"{mMs.Groups[5].Value}: {mMs.Groups[6].Value}", "C#"));
                continue;
            }

            // GCC / G++ / Rust : file.c:12:5: error: ...
            var mGcc = _rxGccRust.Match(ligne);
            if (mGcc.Success && int.TryParse(mGcc.Groups[2].Value, out var lGcc))
            {
                var col = int.TryParse(mGcc.Groups[3].Value, out var c) ? c : 1;
                var sev = mGcc.Groups[4].Value.Equals("warning", StringComparison.OrdinalIgnoreCase)
                    ? SeveriteDiagnostic.Avertissement : SeveriteDiagnostic.Erreur;
                var cheminGcc = ResoudreChemin(mGcc.Groups[1].Value.Trim(), fichierParDefaut);
                trouves.Add(new DiagnosticItem(cheminGcc, lGcc, col, 40, sev, mGcc.Groups[5].Value.Trim(), "Compilateur"));
                continue;
            }

            // PHP : Parse error: ... in file.php on line 10
            var mPhp = _rxPhp.Match(ligne);
            if (mPhp.Success && int.TryParse(mPhp.Groups[4].Value, out var lPhp))
            {
                var sev = mPhp.Groups[1].Value.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                    ? SeveriteDiagnostic.Avertissement : SeveriteDiagnostic.Erreur;
                var cheminPhp = ResoudreChemin(mPhp.Groups[3].Value.Trim(), fichierParDefaut);
                trouves.Add(new DiagnosticItem(cheminPhp, lPhp, 1, 80, sev, mPhp.Groups[2].Value.Trim(), "PHP"));
                continue;
            }

            // Node.js : /path/file.js:12
            var mNode = _rxNode.Match(ligne);
            if (mNode.Success && int.TryParse(mNode.Groups[2].Value, out var lNode))
            {
                var cheminJs = ResoudreChemin(mNode.Groups[1].Value.Trim(), fichierParDefaut);
                var msg = ChercherErreurNodeSuivante(lignes, i);
                trouves.Add(new DiagnosticItem(cheminJs, lNode, 1, 80, SeveriteDiagnostic.Erreur, msg, "Node.js"));
                continue;
            }

            // Go : file.go:12:5: message
            var mGo = _rxGo.Match(ligne);
            if (mGo.Success && int.TryParse(mGo.Groups[2].Value, out var lGo))
            {
                var col = int.TryParse(mGo.Groups[3].Value, out var c) ? c : 1;
                var cheminGo = ResoudreChemin(mGo.Groups[1].Value.Trim(), fichierParDefaut);
                trouves.Add(new DiagnosticItem(cheminGo, lGo, col, 40, SeveriteDiagnostic.Erreur, mGo.Groups[4].Value.Trim(), "Go"));
            }
        }

        if (trouves.Count == 0) return;

        // Dédoublonne les diagnostics identiques
        var uniques = trouves
            .GroupBy(d => (Normaliser(d.Chemin), d.Ligne, d.Colonne, d.Message))
            .Select(g => g.First())
            .ToList();

        lock (_verrou)
        {
            _erreursExecution.Clear();
            _erreursExecution.AddRange(uniques);
        }
        AppEvents.SignalerDiagnosticsChanges();
    }

    private static string ChercherErreurPythonSuivante(string[] lignes, int indexFile)
    {
        for (var j = indexFile + 1; j < Math.Min(indexFile + 6, lignes.Length); j++)
        {
            var l = lignes[j].Trim();
            if (l.Contains("Error:", StringComparison.Ordinal) || l.Contains("Exception:", StringComparison.Ordinal))
                return l;
        }
        return "Erreur d'exécution Python";
    }

    private static string ChercherErreurNodeSuivante(string[] lignes, int indexFile)
    {
        for (var j = indexFile + 1; j < Math.Min(indexFile + 6, lignes.Length); j++)
        {
            var l = lignes[j].Trim();
            if (l.Contains("Error:", StringComparison.Ordinal))
                return l;
        }
        return "Erreur JavaScript / TypeScript";
    }

    private static string ResoudreChemin(string brut, string? defaut)
    {
        if (File.Exists(brut)) return Normaliser(brut);
        if (!string.IsNullOrWhiteSpace(defaut))
        {
            var dossier = Path.GetDirectoryName(defaut);
            if (dossier != null)
            {
                var combine = Path.Combine(dossier, Path.GetFileName(brut));
                if (File.Exists(combine)) return Normaliser(combine);
            }
            if (string.Equals(Path.GetFileName(brut), Path.GetFileName(defaut), StringComparison.OrdinalIgnoreCase))
                return Normaliser(defaut);
        }
        return brut;
    }

    private static bool EstLangageCode(string ext) => ext is
        ".py" or ".pyw" or ".js" or ".mjs" or ".cjs" or ".ts" or ".tsx" or
        ".cs" or ".java" or ".c" or ".h" or ".cpp" or ".cc" or ".cxx" or ".hpp" or
        ".php" or ".go" or ".rs" or ".css" or ".scss" or ".ps1";

    private static void VerifierJson(string chemin, string texte, List<DiagnosticItem> liste)
    {
        if (string.IsNullOrWhiteSpace(texte)) return;
        try
        {
            using var _ = JsonDocument.Parse(texte);
        }
        catch (JsonException ex)
        {
            var ligne = (int)(ex.LineNumber ?? 0) + 1;
            var col = (int)(ex.BytePositionInLine ?? 0) + 1;
            liste.Add(new DiagnosticItem(chemin, ligne, col, 10, SeveriteDiagnostic.Erreur, ex.Message, "JSON"));
        }
    }

    private static void VerifierXml(string chemin, string texte, List<DiagnosticItem> liste)
    {
        if (string.IsNullOrWhiteSpace(texte)) return;
        try
        {
            using var lecteur = XmlReader.Create(new StringReader(texte));
            while (lecteur.Read()) { }
        }
        catch (XmlException ex)
        {
            liste.Add(new DiagnosticItem(chemin, Math.Max(1, ex.LineNumber), Math.Max(1, ex.LinePosition), 12,
                SeveriteDiagnostic.Erreur, ex.Message, "XML"));
        }
    }

    private static void VerifierDelimiteurs(string chemin, string ext, string texte, List<DiagnosticItem> liste)
    {
        var pile = new Stack<(char Symbole, int Ligne, int Colonne)>();
        var ligne = 1;
        var colonne = 1;
        var commentaireHash = ext is ".py" or ".pyw" or ".php" or ".ps1";
        var commentaireSlash = ext is not (".py" or ".pyw");

        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (c == '\n') { ligne++; colonne = 1; continue; }

            // Commentaires ligne // ou #
            if (commentaireSlash && c == '/' && i + 1 < texte.Length && texte[i + 1] == '/')
            {
                while (i < texte.Length && texte[i] != '\n') i++;
                if (i < texte.Length && texte[i] == '\n') { ligne++; colonne = 1; }
                continue;
            }
            if (commentaireHash && c == '#')
            {
                while (i < texte.Length && texte[i] != '\n') i++;
                if (i < texte.Length && texte[i] == '\n') { ligne++; colonne = 1; }
                continue;
            }
            // Commentaire bloc /* ... */
            if (commentaireSlash && c == '/' && i + 1 < texte.Length && texte[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < texte.Length && !(texte[i] == '*' && texte[i + 1] == '/'))
                {
                    if (texte[i] == '\n') ligne++;
                    i++;
                }
                i++;
                colonne = 1;
                continue;
            }

            // Chaînes "..." ou '...'
            if (c is '"' or '\'')
            {
                // Ignore apostrophes dans les lifetimes Rust ('a) ou chaînes triples Python
                if (ext == ".rs" && c == '\'' && i + 1 < texte.Length && char.IsLetter(texte[i + 1]) &&
                    (i + 2 >= texte.Length || texte[i + 2] != '\''))
                {
                    colonne++;
                    continue;
                }

                var triple = ext is ".py" or ".pyw" && i + 2 < texte.Length && texte[i + 1] == c && texte[i + 2] == c;
                if (triple)
                {
                    var delim = new string(c, 3);
                    var fin = texte.IndexOf(delim, i + 3, StringComparison.Ordinal);
                    if (fin < 0)
                    {
                        liste.Add(new DiagnosticItem(chemin, ligne, colonne, 3, SeveriteDiagnostic.Erreur,
                            $"Chaîne multiligne {delim} non fermée", "Syntaxe"));
                        return;
                    }
                    for (var k = i; k < fin + 3; k++) if (texte[k] == '\n') ligne++;
                    i = fin + 2;
                    colonne = 1;
                    continue;
                }

                var debutCol = colonne;
                var debutLigne = ligne;
                var ferme = false;
                i++;
                colonne++;
                while (i < texte.Length && texte[i] != '\n')
                {
                    if (texte[i] == '\\') { i += 2; colonne += 2; continue; }
                    if (texte[i] == c) { ferme = true; break; }
                    i++;
                    colonne++;
                }
                if (!ferme && ext is not ".ps1")
                {
                    liste.Add(new DiagnosticItem(chemin, debutLigne, debutCol, 10, SeveriteDiagnostic.Erreur,
                        $"Chaîne {c} non fermée en fin de ligne", "Syntaxe"));
                }
                if (i < texte.Length && texte[i] == '\n') { ligne++; colonne = 1; }
                else colonne++;
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                pile.Push((c, ligne, colonne));
            }
            else if (c is ')' or ']' or '}')
            {
                var attendu = c switch { ')' => '(', ']' => '[', _ => '{' };
                if (pile.Count == 0)
                {
                    liste.Add(new DiagnosticItem(chemin, ligne, colonne, 1, SeveriteDiagnostic.Erreur,
                        $"Symbole '{c}' inattendu sans '{attendu}' correspondant", "Syntaxe"));
                }
                else if (pile.Peek().Symbole != attendu)
                {
                    var sommet = pile.Pop();
                    liste.Add(new DiagnosticItem(chemin, ligne, colonne, 1, SeveriteDiagnostic.Erreur,
                        $"Symbole '{c}' inattendu (attendu fermeture pour '{sommet.Symbole}' ligne {sommet.Ligne})", "Syntaxe"));
                }
                else
                {
                    pile.Pop();
                }
            }

            colonne++;
        }

        while (pile.Count > 0 && liste.Count < 25)
        {
            var reste = pile.Pop();
            var fermant = reste.Symbole switch { '(' => ')', '[' => ']', _ => '}' };
            liste.Add(new DiagnosticItem(chemin, reste.Ligne, reste.Colonne, 1, SeveriteDiagnostic.Erreur,
                $"Délimiteur '{reste.Symbole}' non fermé ('{fermant}' manquant)", "Syntaxe"));
        }
    }

    private static void VerifierPythonExterne(string chemin, string texte, List<DiagnosticItem> liste)
    {
        var sortie = ExecuterVerificateur("python", "-m py_compile \"{f}\"", ".py", texte);
        if (sortie == null) return;

        var mLigne = _rxPyCompile.Match(sortie);
        if (!mLigne.Success || !int.TryParse(mLigne.Groups[1].Value, out var numLigne)) return;

        var lignes = sortie.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var msg = lignes.LastOrDefault(l => l.Contains("Error:", StringComparison.Ordinal))?.Trim()
                  ?? "Erreur de syntaxe Python";
        if (!liste.Any(d => d.Ligne == numLigne))
            liste.Add(new DiagnosticItem(chemin, numLigne, 1, 40, SeveriteDiagnostic.Erreur, msg, "Python"));
    }

    private static void VerifierNodeExterne(string chemin, string texte, List<DiagnosticItem> liste)
    {
        var sortie = ExecuterVerificateur("node", "--check \"{f}\"", ".js", texte);
        if (string.IsNullOrWhiteSpace(sortie)) return;

        var lignes = sortie.Replace("\r\n", "\n").Split('\n');
        var numLigne = 1;
        foreach (var l in lignes)
        {
            var idx = l.LastIndexOf(':');
            if (idx > 0 && int.TryParse(l[(idx + 1)..].Trim(), out var parsed))
            {
                numLigne = parsed;
                break;
            }
        }
        var msg = lignes.FirstOrDefault(l => l.Contains("SyntaxError:", StringComparison.Ordinal))?.Trim()
                  ?? "Erreur de syntaxe JavaScript";
        if (!liste.Any(d => d.Ligne == numLigne))
            liste.Add(new DiagnosticItem(chemin, numLigne, 1, 40, SeveriteDiagnostic.Erreur, msg, "Node.js"));
    }

    private static void VerifierPhpExterne(string chemin, string texte, List<DiagnosticItem> liste)
    {
        var sortie = ExecuterVerificateur("php", "-l \"{f}\"", ".php", texte);
        if (string.IsNullOrWhiteSpace(sortie) || sortie.Contains("No syntax errors detected", StringComparison.OrdinalIgnoreCase))
            return;

        var m = _rxPhp.Match(sortie);
        var numLigne = m.Success && int.TryParse(m.Groups[4].Value, out var l) ? l : 1;
        var msg = m.Success ? m.Groups[2].Value.Trim() : sortie.Trim();
        if (!liste.Any(d => d.Ligne == numLigne))
            liste.Add(new DiagnosticItem(chemin, numLigne, 1, 40, SeveriteDiagnostic.Erreur, msg, "PHP"));
    }

    private static string? ExecuterVerificateur(string exe, string argsModele, string ext, string texte)
    {
        var dossier = Path.Combine(Path.GetTempPath(), "keycode_diag");
        var temp = Path.Combine(dossier, Guid.NewGuid().ToString("N") + ext);
        try
        {
            Directory.CreateDirectory(dossier);
            File.WriteAllText(temp, texte, Encoding.UTF8);

            var psi = new ProcessStartInfo(exe, argsModele.Replace("{f}", temp))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p == null) return null;

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(3000);
            if (p.ExitCode == 0) return null;
            return string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            try { if (File.Exists(temp + "c")) File.Delete(temp + "c"); } catch { }
        }
    }

    private static string Normaliser(string chemin)
    {
        try { return Path.GetFullPath(chemin); }
        catch { return chemin; }
    }
}
