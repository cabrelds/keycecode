using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>
/// Panneau inférieur complet style VS Code :
/// 1) PROBLÈMES : liste interactive des erreurs/avertissements ;
/// 2) EXÉCUTION : sortie colorée en direct avec liens cliquables fichier:ligne et entrée stdin ;
/// 3) TERMINAL : shell PowerShell interactif persistant.
/// </summary>
public partial class TerminalControl : UserControl
{
    private sealed record ProblemeLigne(DiagnosticItem Diag, string Texte)
    {
        public override string ToString() => Texte;
    }

    private readonly Runner _runner = new();
    private readonly List<string> _historique = new();
    private readonly List<string> _historiqueShell = new();
    private readonly StringBuilder _tamponSortie = new();
    private readonly StringBuilder _journalExecution = new();
    private readonly object _verrouTampon = new();
    private readonly List<(string Texte, bool EstErreur)> _fileAffichage = new();

    private static readonly Regex _rxLienPython = new(@"File ""([^""]+)"", line (\d+)", RegexOptions.Compiled);
    private static readonly Regex _rxLienMsbuild = new(@"([^\(\r\n:]+\.[A-Za-z0-9]+)\((\d+),\d+\)", RegexOptions.Compiled);
    private static readonly Regex _rxLienUnix = new(@"([A-Za-z]:[^:\r\n]+\.[A-Za-z0-9]+|[^\s:]+\.[A-Za-z0-9]+):(\d+)(?::\d+)?", RegexOptions.Compiled);

    private DispatcherTimer? _minuteurFlush;
    private Process? _processusShell;
    private int _indexHistorique;
    private int _indexHistoriqueShell;
    private volatile int _numeroExecution;
    private string? _dernierFichierExecute;
    private bool _abonne;

    public event Action? DemandeMasquage;

    public TerminalControl()
    {
        InitializeComponent();
        _minuteurFlush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _minuteurFlush.Tick += (_, _) => ViderFileAffichage();
    }

    private void TerminalControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_abonne)
        {
            AppEvents.DiagnosticsChanges += SurDiagnosticsChanges;
            AppEvents.DossierOuvert += SurDossierOuvert;
            _abonne = true;
        }
        RafraichirProblemes();
    }

    private void TerminalControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_abonne)
        {
            AppEvents.DiagnosticsChanges -= SurDiagnosticsChanges;
            AppEvents.DossierOuvert -= SurDossierOuvert;
            _abonne = false;
        }
        Arreter();
        ArreterShell();
    }

    // ------------------------------------------------------------------
    // API publique (contrats conservés)
    // ------------------------------------------------------------------

    /// <summary>Lance le programme avec le Runner et affiche l'onglet Exécution.</summary>
    public void Executer(string chemin)
    {
        var numero = ++_numeroExecution;
        _dernierFichierExecute = chemin;

        Arreter();
        EffacerExecution();
        DiagnosticService.EffacerErreursExecution();

        OngletExecution.IsChecked = true;
        BoutonDemanderGemini.Visibility = Visibility.Collapsed;

        AjouterLigneSysteme($"> Exécution : {Path.GetFileName(chemin)}");

        _runner.Demarrer(chemin,
            (texte, estErreur) =>
            {
                if (numero != _numeroExecution) return;
                EnfilerSortie(texte, estErreur);
            },
            code =>
            {
                if (numero != _numeroExecution) return;
                Dispatcher.InvokeAsync(() =>
                {
                    ViderFileAffichage();
                    var contenuTotal = _journalExecution.ToString();
                    DiagnosticService.DernierJournalExecution = contenuTotal.Length > 3500
                        ? contenuTotal[^3500..]
                        : contenuTotal;
                    DiagnosticService.AnalyserSortieExecution(contenuTotal, _dernierFichierExecute);

                    var msgFin = code == 0
                        ? "\n[Programme terminé]\n"
                        : $"\n[Programme terminé avec le code {code}]\n";
                    AjouterSegment(msgFin, code == 0 ? "ThemeSucces" : "ThemeErreur", gras: true);

                    if (code != 0 || DiagnosticService.ObtenirPourFichier(_dernierFichierExecute).Count > 0)
                        BoutonDemanderGemini.Visibility = Visibility.Visible;
                });
            });

        Saisie.Focus();
    }

    /// <summary>Stoppe le programme en cours d'exécution.</summary>
    public void Arreter() => _runner.Arreter();

    /// <summary>Vide la vue active (Exécution ou Terminal shell).</summary>
    public void Effacer()
    {
        if (OngletShell.IsChecked == true)
            SortieShell.Clear();
        else
            EffacerExecution();
    }

    public void AfficherProblemes() => OngletProblemes.IsChecked = true;
    public void AfficherShell()
    {
        OngletShell.IsChecked = true;
        DemarrerShellSiNecessaire();
        SaisieShell.Focus();
    }

    // ------------------------------------------------------------------
    // Sortie colorée + liens cliquables
    // ------------------------------------------------------------------

    private void EffacerExecution()
    {
        lock (_verrouTampon)
        {
            _fileAffichage.Clear();
            _tamponSortie.Clear();
            _journalExecution.Clear();
        }
        DocumentSortie.Blocks.Clear();
        BoutonDemanderGemini.Visibility = Visibility.Collapsed;
    }

    private void EnfilerSortie(string texte, bool estErreur)
    {
        lock (_verrouTampon)
        {
            _fileAffichage.Add((texte, estErreur));
            _journalExecution.Append(texte);
        }
        Dispatcher.InvokeAsync(() =>
        {
            if (_minuteurFlush is { IsEnabled: false })
                _minuteurFlush.Start();
        });
    }

    private void ViderFileAffichage()
    {
        List<(string Texte, bool EstErreur)> lot;
        lock (_verrouTampon)
        {
            if (_fileAffichage.Count == 0)
            {
                _minuteurFlush?.Stop();
                return;
            }
            lot = new List<(string, bool)>(_fileAffichage);
            _fileAffichage.Clear();
        }

        foreach (var (texte, estErreur) in lot)
        {
            var cleCouleur = DeterminerCouleurLigne(texte, estErreur);
            AjouterSegmentAvecLiens(texte, cleCouleur);
        }

        Sortie.ScrollToEnd();
    }

    private static string DeterminerCouleurLigne(string texte, bool estErreur)
    {
        if (estErreur || texte.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            texte.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
            texte.Contains("Traceback", StringComparison.OrdinalIgnoreCase) ||
            texte.Contains("Échec", StringComparison.OrdinalIgnoreCase))
        {
            return "ThemeErreur";
        }
        if (texte.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            texte.Contains("Avertissement", StringComparison.OrdinalIgnoreCase))
        {
            return "ThemeAvertissement";
        }
        return "ThemeTerminalTexte";
    }

    private void AjouterLigneSysteme(string texte)
    {
        AjouterSegment(texte + "\n", "ThemeAccent", gras: true);
    }

    private void AjouterSegment(string texte, string cleCouleur, bool gras = false)
    {
        var paragraphe = ObtenirOuCreerParagraphe();
        var run = new Run(texte)
        {
            FontWeight = gras ? FontWeights.SemiBold : FontWeights.Normal
        };
        run.SetResourceReference(TextElement.ForegroundProperty, cleCouleur);
        paragraphe.Inlines.Add(run);
        Sortie.ScrollToEnd();
    }

    private void AjouterSegmentAvecLiens(string texte, string cleCouleur)
    {
        var paragraphe = ObtenirOuCreerParagraphe();
        var lignes = texte.Split('\n');

        for (var i = 0; i < lignes.Length; i++)
        {
            var morceau = i < lignes.Length - 1 ? lignes[i] + "\n" : lignes[i];
            if (morceau.Length == 0) continue;

            var lien = ExtraireLien(morceau);
            if (lien is { } info && ResoudreCheminLien(info.Chemin) is { } cheminValide)
            {
                var debut = info.Index;
                var fin = info.Index + info.Longueur;
                if (debut > 0)
                {
                    var avant = new Run(morceau[..debut]);
                    avant.SetResourceReference(TextElement.ForegroundProperty, cleCouleur);
                    paragraphe.Inlines.Add(avant);
                }

                var hyper = new Hyperlink(new Run(morceau[debut..fin]))
                {
                    TextDecorations = TextDecorations.Underline,
                    Cursor = Cursors.Hand,
                    ToolTip = $"Cliquer pour ouvrir {Path.GetFileName(cheminValide)} à la ligne {info.Ligne}"
                };
                hyper.SetResourceReference(TextElement.ForegroundProperty, "ThemeAccent");
                var cheminCapture = cheminValide;
                var ligneCapture = info.Ligne;
                hyper.Click += (_, _) => AppEvents.DemanderFichierALaLigne(cheminCapture, ligneCapture);
                paragraphe.Inlines.Add(hyper);

                if (fin < morceau.Length)
                {
                    var apres = new Run(morceau[fin..]);
                    apres.SetResourceReference(TextElement.ForegroundProperty, cleCouleur);
                    paragraphe.Inlines.Add(apres);
                }
            }
            else
            {
                var run = new Run(morceau);
                run.SetResourceReference(TextElement.ForegroundProperty, cleCouleur);
                paragraphe.Inlines.Add(run);
            }
        }
    }

    private Paragraph ObtenirOuCreerParagraphe()
    {
        if (DocumentSortie.Blocks.LastBlock is Paragraph p) return p;
        var nouveau = new Paragraph();
        DocumentSortie.Blocks.Add(nouveau);
        return nouveau;
    }

    private static (int Index, int Longueur, string Chemin, int Ligne)? ExtraireLien(string ligne)
    {
        var mPy = _rxLienPython.Match(ligne);
        if (mPy.Success && int.TryParse(mPy.Groups[2].Value, out var lPy))
            return (mPy.Index, mPy.Length, mPy.Groups[1].Value, lPy);

        var mMs = _rxLienMsbuild.Match(ligne);
        if (mMs.Success && int.TryParse(mMs.Groups[2].Value, out var lMs))
            return (mMs.Index, mMs.Length, mMs.Groups[1].Value, lMs);

        var mUnix = _rxLienUnix.Match(ligne);
        if (mUnix.Success && int.TryParse(mUnix.Groups[2].Value, out var lUnix))
            return (mUnix.Index, mUnix.Length, mUnix.Groups[1].Value, lUnix);

        return null;
    }

    private string? ResoudreCheminLien(string brut)
    {
        try
        {
            if (File.Exists(brut)) return Path.GetFullPath(brut);
            if (!string.IsNullOrWhiteSpace(_dernierFichierExecute))
            {
                var dossier = Path.GetDirectoryName(_dernierFichierExecute);
                if (dossier != null)
                {
                    var cand = Path.Combine(dossier, Path.GetFileName(brut));
                    if (File.Exists(cand)) return Path.GetFullPath(cand);
                }
            }
            if (!string.IsNullOrWhiteSpace(Contexte.DossierRacine))
            {
                var cand = Path.Combine(Contexte.DossierRacine, brut);
                if (File.Exists(cand)) return Path.GetFullPath(cand);
            }
        }
        catch (Exception) { }
        return null;
    }

    // ------------------------------------------------------------------
    // Saisie stdin (Exécution)
    // ------------------------------------------------------------------

    private void EnvoyerSaisie()
    {
        var texte = Saisie.Text;
        Saisie.Clear();
        if (!_runner.EnCours) return;

        ViderFileAffichage();
        AjouterSegment(texte + "\n", "ThemeTexte");
        if (texte.Length > 0) _historique.Add(texte);
        _indexHistorique = _historique.Count;

        _runner.Envoyer(texte);
    }

    private void EnvoyerSaisie_Click(object sender, RoutedEventArgs e) => EnvoyerSaisie();

    private void Sortie_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_runner.EnCours || string.IsNullOrEmpty(e.Text)) return;
        if (e.Text == "\r" || e.Text == "\n")
        {
            EnvoyerSaisie();
            e.Handled = true;
            return;
        }
        Saisie.Focus();
        Saisie.AppendText(e.Text);
        Saisie.CaretIndex = Saisie.Text.Length;
        e.Handled = true;
    }

    private void Sortie_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_runner.EnCours) return;
        if (e.Key == Key.Enter)
        {
            EnvoyerSaisie();
            e.Handled = true;
        }
    }

    private void ParcourirHistorique(int sens)
    {
        var index = _indexHistorique + sens;
        if (index < 0 || index > _historique.Count) return;
        _indexHistorique = index;
        Saisie.Text = index == _historique.Count ? "" : _historique[index];
        Saisie.CaretIndex = Saisie.Text.Length;
    }

    private void Saisie_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: EnvoyerSaisie(); e.Handled = true; break;
            case Key.Up: ParcourirHistorique(-1); e.Handled = true; break;
            case Key.Down: ParcourirHistorique(+1); e.Handled = true; break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && Saisie.SelectionLength == 0:
                Arreter();
                e.Handled = true;
                break;
        }
    }

    // ------------------------------------------------------------------
    // Panneau PROBLÈMES
    // ------------------------------------------------------------------

    private void SurDiagnosticsChanges() => Dispatcher.InvokeAsync(RafraichirProblemes);

    private void RafraichirProblemes()
    {
        var tous = DiagnosticService.ObtenirTous();
        OngletProblemes.Content = $"PROBLÈMES ({tous.Count})";

        ListeProblemes.Items.Clear();
        foreach (var d in tous)
        {
            var icone = d.Severite switch
            {
                SeveriteDiagnostic.Erreur => "⊗",
                SeveriteDiagnostic.Avertissement => "⚠",
                _ => "ℹ"
            };
            var nom = string.IsNullOrWhiteSpace(d.Chemin) ? "Sans titre" : Path.GetFileName(d.Chemin);
            var texte = $"{icone}  [{d.Source}] {d.Message}  —  {nom} (Ln {d.Ligne}, Col {d.Colonne})";
            ListeProblemes.Items.Add(new ProblemeLigne(d, texte));
        }

        TexteAucunProbleme.Visibility = tous.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OuvrirProblemeSelectionne()
    {
        if (ListeProblemes.SelectedItem is not ProblemeLigne p) return;
        if (!string.IsNullOrWhiteSpace(p.Diag.Chemin) && File.Exists(p.Diag.Chemin))
            AppEvents.DemanderFichierALaLigne(p.Diag.Chemin, p.Diag.Ligne);
    }

    private void ListeProblemes_DoubleClick(object sender, MouseButtonEventArgs e) => OuvrirProblemeSelectionne();

    private void ListeProblemes_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OuvrirProblemeSelectionne();
            e.Handled = true;
        }
    }

    private void AllerAuProbleme_Click(object sender, RoutedEventArgs e) => OuvrirProblemeSelectionne();

    private void CorrigerProblemeGemini_Click(object sender, RoutedEventArgs e)
    {
        if (ListeProblemes.SelectedItem is ProblemeLigne p)
            AppEvents.DemanderAideIA(p.Diag.Message, p.Diag.Chemin, p.Diag.Ligne);
    }

    // ------------------------------------------------------------------
    // Terminal interactif PowerShell
    // ------------------------------------------------------------------

    private void SurDossierOuvert(string dossier)
    {
        if (_processusShell is { HasExited: false } && Directory.Exists(dossier))
        {
            try
            {
                _processusShell.StandardInput.WriteLine($"Set-Location -LiteralPath '{dossier.Replace("'", "''")}'");
                _processusShell.StandardInput.Flush();
            }
            catch (Exception) { }
        }
    }

    private void DemarrerShellSiNecessaire()
    {
        if (_processusShell is { HasExited: false }) return;

        var dossier = !string.IsNullOrWhiteSpace(Contexte.DossierRacine) && Directory.Exists(Contexte.DossierRacine)
            ? Contexte.DossierRacine
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var psi = new ProcessStartInfo("powershell", "-NoLogo -NoExit -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\"")
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

        try
        {
            _processusShell = new Process { StartInfo = psi };
            _processusShell.Start();
            SortieShell.AppendText($"PowerShell — {dossier}\n");

            Task.Run(() => LireShell(_processusShell.StandardOutput));
            Task.Run(() => LireShell(_processusShell.StandardError));
        }
        catch (Exception ex)
        {
            SortieShell.AppendText($"Impossible de démarrer le terminal PowerShell : {ex.Message}\n");
        }
    }

    private void LireShell(StreamReader lecteur)
    {
        var tampon = new char[512];
        try
        {
            int n;
            while ((n = lecteur.Read(tampon, 0, tampon.Length)) > 0)
            {
                var texte = new string(tampon, 0, n);
                Dispatcher.InvokeAsync(() =>
                {
                    SortieShell.AppendText(texte);
                    SortieShell.ScrollToEnd();
                });
            }
        }
        catch (Exception) { }
    }

    private void EnvoyerCommandeShell()
    {
        DemarrerShellSiNecessaire();
        var cmd = SaisieShell.Text;
        SaisieShell.Clear();
        if (_processusShell is not { HasExited: false }) return;

        if (cmd.Trim().Equals("cls", StringComparison.OrdinalIgnoreCase) ||
            cmd.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            SortieShell.Clear();
        }
        else
        {
            SortieShell.AppendText($"PS> {cmd}\n");
            SortieShell.ScrollToEnd();
        }

        if (cmd.Length > 0) _historiqueShell.Add(cmd);
        _indexHistoriqueShell = _historiqueShell.Count;

        try
        {
            _processusShell.StandardInput.WriteLine(cmd);
            _processusShell.StandardInput.Flush();
        }
        catch (Exception) { }
    }

    private void SaisieShell_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: EnvoyerCommandeShell(); e.Handled = true; break;
            case Key.Up:
                if (_indexHistoriqueShell > 0)
                {
                    _indexHistoriqueShell--;
                    SaisieShell.Text = _historiqueShell[_indexHistoriqueShell];
                    SaisieShell.CaretIndex = SaisieShell.Text.Length;
                }
                e.Handled = true;
                break;
            case Key.Down:
                if (_indexHistoriqueShell < _historiqueShell.Count)
                {
                    _indexHistoriqueShell++;
                    SaisieShell.Text = _indexHistoriqueShell == _historiqueShell.Count ? "" : _historiqueShell[_indexHistoriqueShell];
                    SaisieShell.CaretIndex = SaisieShell.Text.Length;
                }
                e.Handled = true;
                break;
        }
    }

    private void ArreterShell()
    {
        try
        {
            if (_processusShell is { HasExited: false })
                _processusShell.Kill(entireProcessTree: true);
            _processusShell?.Dispose();
        }
        catch (Exception) { }
        _processusShell = null;
    }

    // ------------------------------------------------------------------
    // Boutons d'en-tête
    // ------------------------------------------------------------------

    private void OngletBas_Checked(object sender, RoutedEventArgs e)
    {
        if (VueProblemes == null || VueExecution == null || VueShell == null) return;

        VueProblemes.Visibility = OngletProblemes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        VueExecution.Visibility = OngletExecution.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        VueShell.Visibility = OngletShell.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (OngletShell.IsChecked == true)
        {
            DemarrerShellSiNecessaire();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SaisieShell.Focus());
        }
        else if (OngletExecution.IsChecked == true)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Saisie.Focus());
        }
    }

    private void Arreter_Click(object sender, RoutedEventArgs e) => Arreter();
    private void Effacer_Click(object sender, RoutedEventArgs e) => Effacer();
    private void FermerPanneau_Click(object sender, RoutedEventArgs e) => DemandeMasquage?.Invoke();

    private void DemanderGemini_Click(object sender, RoutedEventArgs e)
    {
        var contenu = _journalExecution.ToString();
        if (string.IsNullOrWhiteSpace(contenu))
            contenu = "Erreur lors de l'exécution";
        AppEvents.DemanderAideIA(contenu, _dernierFichierExecute, null);
    }
}
