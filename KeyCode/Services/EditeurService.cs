using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Indentation.CSharp;

namespace KeyCode.Services;

/// <summary>
/// Configuration d'un éditeur AvalonEdit : coloration, thème, police, numéros de ligne, pliage,
/// auto-indentation, fermeture auto des parenthèses/guillemets, auto-complétion, zoom Ctrl+molette,
/// soulignage des diagnostics en direct et raccourcis d'édition VS Code (Ctrl+/, Alt+Flèche, Ctrl+Maj+K).
/// </summary>
public static class EditeurService
{
    private enum TypePliage { Aucun, Accolades, Indentation, Xml }

    private sealed class EtatEditeur
    {
        public string? Chemin { get; set; }
        public bool Branche { get; set; }
        public FoldingManager? Pliage { get; set; }
        public TextDocument? DocumentPliage { get; set; }
        public TypePliage TypePliage { get; set; }
        public DispatcherTimer? MinuteurPliage { get; set; }
        public DispatcherTimer? MinuteurDiagnostic { get; set; }
        public CancellationTokenSource? JetonDiagnostic { get; set; }
        public CompletionWindow? Completion { get; set; }
        public MarqueurService? Marqueurs { get; set; }
    }

    private static readonly ConditionalWeakTable<TextEditor, EtatEditeur> _etats = new();
    private static readonly List<WeakReference<TextEditor>> _editeurs = new();
    private static readonly Regex _regexMot = new(@"[A-Za-z_][A-Za-z0-9_]{2,}", RegexOptions.Compiled);

    private static readonly HashSet<string> _extensionsTexte = new() { ".txt", ".md", ".csv", ".log" };
    private static readonly HashSet<string> _extensionsAccolades = new()
    {
        ".cs", ".java", ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx",
        ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".php", ".go", ".rs", ".json", ".css", ".scss"
    };
    private static readonly HashSet<string> _extensionsXml = new()
    {
        ".xml", ".xaml", ".csproj", ".svg", ".config", ".xsd", ".props", ".targets", ".resx", ".html", ".htm"
    };

    private const double TaillePoliceMin = 8;
    private const double TaillePoliceMax = 48;

    static EditeurService()
    {
        AppEvents.ThemeChange += _ => ReconfigurerTous();
        AppEvents.DiagnosticsChanges += MettreAJourMarqueursTous;
    }

    /// <summary>Configure un éditeur. chemin vaut null pour un onglet pas encore enregistré.</summary>
    public static void Configurer(TextEditor editeur, string? chemin)
    {
        var etat = _etats.GetValue(editeur, _ => new EtatEditeur());
        etat.Chemin = chemin;
        etat.Marqueurs ??= new MarqueurService(editeur);

        editeur.ShowLineNumbers = true;
        editeur.Options.HighlightCurrentLine = true;
        editeur.Options.IndentationSize = 4;
        editeur.Options.ConvertTabsToSpaces = Extension(chemin) != ".go";
        editeur.Options.EnableHyperlinks = true;
        editeur.Options.EnableEmailHyperlinks = false;

        AppliquerApparence(editeur, etat);
        ConfigurerIndentation(editeur, etat);
        ConfigurerPliage(editeur, etat);

        if (!etat.Branche)
        {
            Brancher(editeur, etat);
            etat.Branche = true;
            lock (_editeurs) _editeurs.Add(new WeakReference<TextEditor>(editeur));
        }

        PlanifierDiagnostic(editeur, etat, immediat: true);
    }

    /// <summary>Réapplique thème, police et coloration à tous les éditeurs ouverts.</summary>
    public static void ReconfigurerTous()
    {
        foreach (var editeur in EditeursVivants())
        {
            if (!_etats.TryGetValue(editeur, out var etat)) continue;

            if (editeur.Dispatcher.CheckAccess())
                AppliquerApparence(editeur, etat);
            else
                editeur.Dispatcher.InvokeAsync(() => AppliquerApparence(editeur, etat));
        }
    }

    private static void MettreAJourMarqueursTous()
    {
        foreach (var editeur in EditeursVivants())
        {
            if (!_etats.TryGetValue(editeur, out var etat) || etat.Marqueurs == null) continue;
            var action = () => AppliquerMarqueurs(editeur, etat);
            if (editeur.Dispatcher.CheckAccess()) action();
            else editeur.Dispatcher.InvokeAsync(action);
        }
    }

    private static void AppliquerMarqueurs(TextEditor editeur, EtatEditeur etat)
    {
        if (etat.Marqueurs == null || editeur.Document == null) return;
        if (string.IsNullOrWhiteSpace(etat.Chemin))
        {
            etat.Marqueurs.Effacer();
            return;
        }

        var diagnostics = DiagnosticService.ObtenirPourFichier(etat.Chemin);
        if (diagnostics.Count == 0)
        {
            etat.Marqueurs.Effacer();
            return;
        }

        var marqueurs = new List<MarqueurDiagnostic>();
        var totalLignes = editeur.Document.LineCount;
        foreach (var diag in diagnostics)
        {
            if (totalLignes <= 0) break;
            var numLigne = Math.Clamp(diag.Ligne, 1, totalLignes);
            var docLigne = editeur.Document.GetLineByNumber(numLigne);
            var colOffset = Math.Clamp(diag.Colonne - 1, 0, Math.Max(0, docLigne.Length - 1));
            var debut = docLigne.Offset + colOffset;
            var longueurMax = Math.Max(1, docLigne.EndOffset - debut);
            var longueur = Math.Clamp(diag.Longueur, 1, longueurMax);

            marqueurs.Add(new MarqueurDiagnostic(debut, longueur, diag.Severite, diag.Message, diag.Source));
        }

        etat.Marqueurs.DefinirMarqueurs(marqueurs);
    }

    private static void AppliquerApparence(TextEditor editeur, EtatEditeur etat)
    {
        editeur.FontFamily = new FontFamily("Consolas");
        editeur.FontSize = Math.Clamp(SettingsService.TaillePolice, TaillePoliceMin, TaillePoliceMax);

        editeur.Background = ThemeManager.Pinceau("ThemeFond");
        editeur.Foreground = ThemeManager.Pinceau("ThemeTexte");
        editeur.LineNumbersForeground = ThemeManager.Pinceau("ThemeNumerosLigne");
        editeur.TextArea.SelectionBrush = ThemeManager.Pinceau("ThemeSelection");
        editeur.TextArea.TextView.CurrentLineBackground = ThemeManager.Pinceau("ThemeLigneCourante");
        editeur.TextArea.TextView.CurrentLineBorder = new Pen(ThemeManager.Pinceau("ThemeBordure"), 0.8);

        if (etat.Chemin != null)
            ColorationService.Appliquer(editeur, etat.Chemin);
        else
            editeur.SyntaxHighlighting = null;
    }

    private static string Extension(string? chemin) =>
        string.IsNullOrEmpty(chemin) ? "" : Path.GetExtension(chemin).ToLowerInvariant();

    private static void ConfigurerIndentation(TextEditor editeur, EtatEditeur etat)
    {
        var extension = Extension(etat.Chemin);

        if (extension is ".py" or ".pyw")
            editeur.TextArea.IndentationStrategy = new IndentationPython(editeur.Options);
        else if (_extensionsAccolades.Contains(extension))
            editeur.TextArea.IndentationStrategy = new CSharpIndentationStrategy(editeur.Options);
        else
            editeur.TextArea.IndentationStrategy = new DefaultIndentationStrategy();
    }

    private sealed class IndentationPython : IIndentationStrategy
    {
        private readonly TextEditorOptions _options;

        public IndentationPython(TextEditorOptions options) => _options = options;

        public void IndentLine(TextDocument document, DocumentLine line)
        {
            var precedente = line.PreviousLine;
            if (precedente == null) return;

            var indentationPrecedente = document.GetText(TextUtilities.GetWhitespaceAfter(document, precedente.Offset));
            var indentation = indentationPrecedente;
            if (document.GetText(precedente).TrimEnd().EndsWith(':'))
                indentation += _options.IndentationString;

            var actuelle = TextUtilities.GetWhitespaceAfter(document, line.Offset);
            document.Replace(actuelle.Offset, actuelle.Length, indentation);
        }

        public void IndentLines(TextDocument document, int beginLine, int endLine)
        {
            for (var numero = beginLine; numero <= endLine; numero++)
                IndentLine(document, document.GetLineByNumber(numero));
        }
    }

    private static void ConfigurerPliage(TextEditor editeur, EtatEditeur etat)
    {
        var extension = Extension(etat.Chemin);
        var type = TypePliage.Aucun;
        if (extension is ".py" or ".pyw") type = TypePliage.Indentation;
        else if (_extensionsAccolades.Contains(extension)) type = TypePliage.Accolades;
        else if (_extensionsXml.Contains(extension)) type = TypePliage.Xml;

        if (type == TypePliage.Aucun)
        {
            SupprimerPliage(etat);
            return;
        }

        if (etat.Pliage == null || !ReferenceEquals(etat.DocumentPliage, editeur.Document))
        {
            SupprimerPliage(etat);
            etat.Pliage = FoldingManager.Install(editeur.TextArea);
            etat.DocumentPliage = editeur.Document;
        }

        etat.TypePliage = type;
        MettreAJourPliage(editeur, etat);
    }

    private static void SupprimerPliage(EtatEditeur etat)
    {
        if (etat.Pliage == null) return;
        try { FoldingManager.Uninstall(etat.Pliage); }
        catch (Exception) { }
        etat.Pliage = null;
        etat.DocumentPliage = null;
    }

    private static void MettreAJourPliage(TextEditor editeur, EtatEditeur etat)
    {
        if (etat.Pliage == null) return;
        try
        {
            switch (etat.TypePliage)
            {
                case TypePliage.Accolades:
                    etat.Pliage.UpdateFoldings(CalculerPliagesAccolades(editeur.Document), -1);
                    break;
                case TypePliage.Indentation:
                    etat.Pliage.UpdateFoldings(CalculerPliagesIndentation(editeur.Document), -1);
                    break;
                case TypePliage.Xml:
                    new XmlFoldingStrategy().UpdateFoldings(etat.Pliage, editeur.Document);
                    break;
            }
        }
        catch (Exception) { }
    }

    private static void PlanifierPliage(TextEditor editeur, EtatEditeur etat)
    {
        if (etat.Pliage == null) return;
        if (etat.MinuteurPliage == null)
        {
            etat.MinuteurPliage = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            etat.MinuteurPliage.Tick += (_, _) =>
            {
                etat.MinuteurPliage!.Stop();
                MettreAJourPliage(editeur, etat);
            };
        }
        etat.MinuteurPliage.Stop();
        etat.MinuteurPliage.Start();
    }

    private static void PlanifierDiagnostic(TextEditor editeur, EtatEditeur etat, bool immediat = false)
    {
        if (etat.MinuteurDiagnostic == null)
        {
            etat.MinuteurDiagnostic = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            etat.MinuteurDiagnostic.Tick += async (_, _) =>
            {
                etat.MinuteurDiagnostic!.Stop();
                await LancerDiagnosticAsync(editeur, etat);
            };
        }

        etat.MinuteurDiagnostic.Stop();
        if (immediat)
            _ = LancerDiagnosticAsync(editeur, etat);
        else
            etat.MinuteurDiagnostic.Start();
    }

    private static async Task LancerDiagnosticAsync(TextEditor editeur, EtatEditeur etat)
    {
        etat.JetonDiagnostic?.Cancel();
        etat.JetonDiagnostic?.Dispose();
        etat.JetonDiagnostic = new CancellationTokenSource();
        var jeton = etat.JetonDiagnostic.Token;

        var texte = editeur.Document?.Text ?? "";
        var chemin = etat.Chemin;

        try
        {
            await DiagnosticService.AnalyserDocumentAsync(chemin, texte, jeton);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private static List<NewFolding> CalculerPliagesAccolades(TextDocument document)
    {
        var resultat = new List<NewFolding>();
        var pile = new Stack<int>();
        var texte = document.Text;
        var i = 0;

        while (i < texte.Length)
        {
            var c = texte[i];
            if (c == '/' && i + 1 < texte.Length && texte[i + 1] == '/')
            {
                while (i < texte.Length && texte[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < texte.Length && texte[i + 1] == '*')
            {
                var fin = texte.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = fin < 0 ? texte.Length : fin + 2;
                continue;
            }
            if (c == '`')
            {
                var fin = texte.IndexOf('`', i + 1);
                i = fin < 0 ? texte.Length : fin + 1;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                var j = i + 1;
                while (j < texte.Length && texte[j] != c && texte[j] != '\n')
                {
                    if (texte[j] == '\\') j++;
                    j++;
                }
                i = j + 1;
                continue;
            }
            if (c == '{') pile.Push(i);
            else if (c == '}' && pile.Count > 0)
            {
                var debut = pile.Pop();
                if (document.GetLineByOffset(debut).LineNumber != document.GetLineByOffset(i).LineNumber)
                    resultat.Add(new NewFolding(debut, i + 1) { Name = "{...}" });
            }
            i++;
        }

        resultat.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return resultat;
    }

    private static List<NewFolding> CalculerPliagesIndentation(TextDocument document)
    {
        var lignes = new List<(int Debut, int Fin, int Indentation)>();
        foreach (var ligne in document.Lines)
        {
            var indentation = 0;
            var vide = true;
            for (var k = ligne.Offset; k < ligne.EndOffset; k++)
            {
                var c = document.GetCharAt(k);
                if (c == ' ') indentation++;
                else if (c == '\t') indentation += 4;
                else { vide = false; break; }
            }
            if (!vide) lignes.Add((ligne.Offset, ligne.EndOffset, indentation));
        }

        var resultat = new List<NewFolding>();
        var pile = new Stack<(int Indentation, int Debut)>();

        for (var i = 0; i < lignes.Count; i++)
        {
            while (pile.Count > 0 && pile.Peek().Indentation >= lignes[i].Indentation)
            {
                var ouvert = pile.Pop();
                var fin = lignes[i - 1].Fin;
                if (fin > ouvert.Debut) resultat.Add(new NewFolding(ouvert.Debut, fin) { Name = "..." });
            }
            if (i + 1 < lignes.Count && lignes[i + 1].Indentation > lignes[i].Indentation)
                pile.Push((lignes[i].Indentation, lignes[i].Fin));
        }

        while (pile.Count > 0 && lignes.Count > 0)
        {
            var ouvert = pile.Pop();
            var fin = lignes[^1].Fin;
            if (fin > ouvert.Debut) resultat.Add(new NewFolding(ouvert.Debut, fin) { Name = "..." });
        }

        resultat.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return resultat;
    }

    private static void Brancher(TextEditor editeur, EtatEditeur etat)
    {
        editeur.TextChanged += (_, _) =>
        {
            PlanifierPliage(editeur, etat);
            PlanifierDiagnostic(editeur, etat);
        };
        editeur.DocumentChanged += (_, _) => ConfigurerPliage(editeur, etat);

        editeur.TextArea.TextEntering += (_, e) => SurTextEntering(editeur, etat, e);
        editeur.TextArea.TextEntered += (_, e) => SurTextEntered(editeur, etat, e);
        editeur.TextArea.LostKeyboardFocus += (_, _) =>
        {
            if (etat.Completion != null && !etat.Completion.IsKeyboardFocusWithin)
                etat.Completion.Close();
        };

        // Raccourcis éditeur VS Code : Ctrl+Espace, Ctrl+/, Ctrl+Maj+K, Alt+Haut/Bas
        editeur.PreviewKeyDown += (_, e) =>
        {
            var mod = Keyboard.Modifiers;
            if (e.Key == Key.Space && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                ProposerCompletion(editeur, etat, forcer: true);
            }
            else if ((e.Key == Key.OemQuestion || e.Key == Key.Divide) && mod == ModifierKeys.Control)
            {
                e.Handled = true;
                BasculerCommentaireLigne(editeur, etat.Chemin);
            }
            else if (e.Key == Key.K && mod == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;
                SupprimerLigneCourante(editeur);
            }
            else if ((e.SystemKey == Key.Up || e.SystemKey == Key.Down) && mod == ModifierKeys.Alt)
            {
                e.Handled = true;
                DeplacerLigne(editeur, e.SystemKey == Key.Up ? -1 : 1);
            }
        };

        editeur.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            ChangerTaille(SettingsService.TaillePolice + (e.Delta > 0 ? 1 : -1));
        }), true);
    }

    private static void BasculerCommentaireLigne(TextEditor editeur, string? chemin)
    {
        var ext = Extension(chemin);
        var prefixe = ext is ".py" or ".pyw" or ".ps1" or ".yml" or ".yaml" ? "# " : "// ";
        var doc = editeur.Document;
        var ligneDebut = doc.GetLineByOffset(editeur.SelectionStart).LineNumber;
        var ligneFin = doc.GetLineByOffset(editeur.SelectionStart + editeur.SelectionLength).LineNumber;

        using (doc.RunUpdate())
        {
            for (var n = ligneDebut; n <= ligneFin; n++)
            {
                var l = doc.GetLineByNumber(n);
                var texte = doc.GetText(l);
                var trim = texte.TrimStart();
                var decalage = texte.Length - trim.Length;
                if (trim.StartsWith(prefixe.TrimEnd(), StringComparison.Ordinal))
                {
                    var len = trim.StartsWith(prefixe, StringComparison.Ordinal) ? prefixe.Length : prefixe.TrimEnd().Length;
                    doc.Remove(l.Offset + decalage, len);
                }
                else
                {
                    doc.Insert(l.Offset + decalage, prefixe);
                }
            }
        }
    }

    private static void SupprimerLigneCourante(TextEditor editeur)
    {
        var doc = editeur.Document;
        var l = doc.GetLineByOffset(editeur.CaretOffset);
        doc.Remove(l.Offset, l.TotalLength);
    }

    private static void DeplacerLigne(TextEditor editeur, int direction)
    {
        var doc = editeur.Document;
        var l = doc.GetLineByOffset(editeur.CaretOffset);
        var cibleNum = l.LineNumber + direction;
        if (cibleNum < 1 || cibleNum > doc.LineCount) return;

        var cible = doc.GetLineByNumber(cibleNum);
        var txtCourant = doc.GetText(l);
        var txtCible = doc.GetText(cible);

        using (doc.RunUpdate())
        {
            if (direction < 0)
            {
                doc.Replace(l.Offset, l.Length, txtCible);
                doc.Replace(cible.Offset, cible.Length, txtCourant);
            }
            else
            {
                doc.Replace(cible.Offset, cible.Length, txtCourant);
                doc.Replace(l.Offset, l.Length, txtCible);
            }
        }
        editeur.CaretOffset = doc.GetLineByNumber(cibleNum).Offset;
    }

    private static void ChangerTaille(double taille)
    {
        SettingsService.TaillePolice = Math.Clamp(taille, TaillePoliceMin, TaillePoliceMax);
        ReconfigurerTous();
    }

    private static List<TextEditor> EditeursVivants()
    {
        var vivants = new List<TextEditor>();
        lock (_editeurs)
        {
            _editeurs.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in _editeurs)
                if (reference.TryGetTarget(out var editeur)) vivants.Add(editeur);
        }
        return vivants;
    }

    private static bool EstCaractereMot(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static void SurTextEntering(TextEditor editeur, EtatEditeur etat, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1) return;
        var c = e.Text[0];

        if (etat.Completion != null && !EstCaractereMot(c))
            etat.Completion.Close();

        if ((c is ')' or ']' or '}' or '"' or '\'') && editeur.SelectionLength == 0)
        {
            var decalage = editeur.CaretOffset;
            if (decalage < editeur.Document.TextLength && editeur.Document.GetCharAt(decalage) == c)
            {
                editeur.CaretOffset = decalage + 1;
                e.Handled = true;
            }
        }
    }

    private static void SurTextEntered(TextEditor editeur, EtatEditeur etat, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1) return;
        var c = e.Text[0];

        var fermante = c switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '"' => '"',
            '\'' => '\'',
            _ => '\0'
        };

        if (fermante != '\0')
        {
            if (FermetureAutorisee(editeur, etat, c))
            {
                var position = editeur.CaretOffset;
                editeur.Document.Insert(position, fermante.ToString());
                editeur.CaretOffset = position;
            }
            return;
        }

        if (EstCaractereMot(c))
            ProposerCompletion(editeur, etat, forcer: false);
    }

    private static bool FermetureAutorisee(TextEditor editeur, EtatEditeur etat, char c)
    {
        if (_extensionsTexte.Contains(Extension(etat.Chemin))) return false;

        var document = editeur.Document;
        var position = editeur.CaretOffset;
        var avant = position >= 2 ? document.GetCharAt(position - 2) : '\0';
        var avantAvant = position >= 3 ? document.GetCharAt(position - 3) : '\0';
        var apres = position < document.TextLength ? document.GetCharAt(position) : '\0';

        if (c is '"' or '\'')
        {
            if (EstCaractereMot(apres)) return false;
            if (c == '\'' && EstCaractereMot(avant)) return false;
            if (avant == c && avantAvant == c) return false;
            return true;
        }

        return apres == '\0' || char.IsWhiteSpace(apres) || apres is ')' or ']' or '}' or ',' or ';' or ':';
    }

    private sealed class DonneeCompletion : ICompletionData
    {
        public DonneeCompletion(string texte) => Text = texte;
        public ImageSource Image => null!;
        public string Text { get; }
        public object Content => Text;
        public object Description => null!;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }

    private static void ProposerCompletion(TextEditor editeur, EtatEditeur etat, bool forcer)
    {
        if (etat.Completion != null) return;

        var document = editeur.Document;
        var position = editeur.CaretOffset;

        var debut = position;
        while (debut > 0 && EstCaractereMot(document.GetCharAt(debut - 1))) debut--;

        var longueur = position - debut;
        if (!forcer && longueur < 2) return;
        if (longueur > 0 && char.IsDigit(document.GetCharAt(debut))) return;

        var prefixe = document.GetText(debut, longueur);
        var candidats = ChercherCandidats(editeur, etat, prefixe);
        if (candidats.Count == 0) return;

        try
        {
            var fenetre = new CompletionWindow(editeur.TextArea) { StartOffset = debut };
            fenetre.Background = ThemeManager.Pinceau("ThemeFondSecondaire");
            fenetre.Foreground = ThemeManager.Pinceau("ThemeTexte");
            fenetre.CompletionList.ListBox.Background = ThemeManager.Pinceau("ThemeFondSecondaire");
            fenetre.CompletionList.ListBox.Foreground = ThemeManager.Pinceau("ThemeTexte");

            foreach (var mot in candidats)
                fenetre.CompletionList.CompletionData.Add(new DonneeCompletion(mot));

            fenetre.CompletionList.SelectItem(prefixe);
            fenetre.Closed += (_, _) => etat.Completion = null;
            etat.Completion = fenetre;
            fenetre.Show();
        }
        catch (Exception)
        {
            etat.Completion = null;
        }
    }

    private static List<string> ChercherCandidats(TextEditor editeur, EtatEditeur etat, string prefixe)
    {
        var mots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var motCle in ColorationService.ObtenirMotsCles(etat.Chemin))
            mots.Add(motCle);

        if (editeur.Document.TextLength <= 300_000)
            foreach (Match correspondance in _regexMot.Matches(editeur.Document.Text))
                mots.Add(correspondance.Value);

        return mots
            .Where(mot => mot.Length > prefixe.Length && mot.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase))
            .OrderBy(mot => mot, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToList();
    }
}
