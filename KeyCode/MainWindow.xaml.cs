using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using KeyCode.Services;

namespace KeyCode;

/// <summary>
/// Fenêtre principale (version provisoire : un seul éditeur, terminal intégré).
/// Propriétaire : cabrelds.
/// </summary>
public partial class MainWindow : Window
{
    private string? _chemin;
    private readonly Runner _runner = new();
    private TextEditor? _editeurSuivi;

    public MainWindow()
    {
        InitializeComponent();

        SettingsService.Charger();
        ThemeManager.Appliquer(SettingsService.Theme);

        EditeurService.Configurer(Editeur, null);
        Contexte.EditeurActif = Editeur;
        Contexte.CheminActif = null;

        // Tant que OngletsControl n'est pas fusionné, la fenêtre écoute elle-même les demandes d'ouverture
        AppEvents.FichierDemande += OuvrirFichier;
        AppEvents.EditeurActifChange += SuivreEditeur;

        SuivreEditeur();
        ConstruireMenuRecents();
        MettreAJourTitre();
    }

    // ------------------------------------------------------------------
    // Fichier
    // ------------------------------------------------------------------

    private void Nouveau_Click(object sender, RoutedEventArgs e) => NouveauFichier();

    private void NouveauFichier()
    {
        if (!DemanderEnregistrement()) return;
        Editeur.Clear();
        Editeur.IsModified = false;
        _chemin = null;
        Contexte.CheminActif = null;
        MettreAJourTitre();
        MettreAJourStatut();
    }

    private void Ouvrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog();
        if (dlg.ShowDialog() == true) OuvrirFichier(dlg.FileName);
    }

    private void OuvrirDossier_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        // Sera remplacé par Explorateur.OuvrirDossier(...) après la fusion de l'explorateur
        Contexte.DossierRacine = dlg.FolderName;
        SettingsService.DernierDossier = dlg.FolderName;
        AppEvents.SignalerDossierOuvert(dlg.FolderName);
        MettreAJourTitre();
    }

    private void OuvrirFichier(string chemin)
    {
        if (!DemanderEnregistrement()) return;
        try
        {
            Editeur.Load(chemin);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'ouvrir le fichier :\n{ex.Message}", "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        EditeurService.Configurer(Editeur, chemin);
        Editeur.IsModified = false;
        _chemin = chemin;
        Contexte.CheminActif = chemin;
        AjouterRecent(chemin);
        MettreAJourTitre();
        MettreAJourStatut();
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e) => Enregistrer();

    private void ToutEnregistrer_Click(object sender, RoutedEventArgs e) => Enregistrer();

    /// <summary>Enregistre le fichier courant. Retourne false si l'utilisateur annule.</summary>
    private bool Enregistrer()
    {
        if (_chemin == null)
        {
            var dlg = new SaveFileDialog();
            if (dlg.ShowDialog() != true) return false;
            _chemin = dlg.FileName;
            Contexte.CheminActif = _chemin;
            EditeurService.Configurer(Editeur, _chemin);
        }
        Editeur.Save(_chemin);
        Editeur.IsModified = false;
        AjouterRecent(_chemin);
        AppEvents.SignalerFichierEnregistre(_chemin);
        MettreAJourTitre();
        MettreAJourStatut();
        return true;
    }

    /// <summary>Si le texte est modifié, propose d'enregistrer. Retourne false si l'utilisateur annule.</summary>
    private bool DemanderEnregistrement()
    {
        if (!Editeur.IsModified) return true;
        var nom = _chemin == null ? "Sans titre" : System.IO.Path.GetFileName(_chemin);
        var rep = MessageBox.Show($"Enregistrer les modifications de « {nom} » ?", "KeyCode",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (rep == MessageBoxResult.Cancel) return false;
        if (rep == MessageBoxResult.Yes) return Enregistrer();
        return true;
    }

    private void Quitter_Click(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------
    // Fichiers récents (SettingsService.DerniersFichiers)
    // ------------------------------------------------------------------

    private void AjouterRecent(string chemin)
    {
        var liste = SettingsService.DerniersFichiers;
        liste.RemoveAll(c => string.Equals(c, chemin, StringComparison.OrdinalIgnoreCase));
        liste.Insert(0, chemin);
        if (liste.Count > 10) liste.RemoveRange(10, liste.Count - 10);
        ConstruireMenuRecents();
    }

    private void ConstruireMenuRecents()
    {
        MenuRecents.Items.Clear();
        var liste = SettingsService.DerniersFichiers;
        if (liste.Count == 0)
        {
            MenuRecents.Items.Add(new MenuItem { Header = "(aucun)", IsEnabled = false });
            return;
        }
        foreach (var chemin in liste.ToList())
        {
            var item = new MenuItem { Header = chemin.Replace("_", "__") };
            item.Click += (_, _) => OuvrirFichier(chemin);
            MenuRecents.Items.Add(item);
        }
    }

    // ------------------------------------------------------------------
    // Édition
    // ------------------------------------------------------------------

    private void Annuler_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Undo();
    private void Retablir_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Redo();
    private void Couper_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Cut();
    private void Copier_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Copy();
    private void Coller_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Paste();
    private void ToutSelectionner_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.SelectAll();

    // ------------------------------------------------------------------
    // Affichage
    // ------------------------------------------------------------------

    private void ThemeClair_Click(object sender, RoutedEventArgs e) => ChangerTheme("Clair");
    private void ThemeSombre_Click(object sender, RoutedEventArgs e) => ChangerTheme("Sombre");

    private void ChangerTheme(string theme)
    {
        ThemeManager.Appliquer(theme);
        SettingsService.Theme = theme;
    }

    private void ZoomPlus_Click(object sender, RoutedEventArgs e) => ChangerTaille(SettingsService.TaillePolice + 2);
    private void ZoomMoins_Click(object sender, RoutedEventArgs e) => ChangerTaille(SettingsService.TaillePolice - 2);
    private void ZoomNormal_Click(object sender, RoutedEventArgs e) => ChangerTaille(14);

    private void ChangerTaille(double taille)
    {
        taille = Math.Clamp(taille, 8, 48);
        SettingsService.TaillePolice = taille;
        if (Contexte.EditeurActif != null) Contexte.EditeurActif.FontSize = taille;
        EditeurService.ReconfigurerTous();
    }

    // ------------------------------------------------------------------
    // Exécuter (terminal provisoire)
    // ------------------------------------------------------------------

    private void Executer_Click(object sender, RoutedEventArgs e) => ExecuterFichier();

    private void ExecuterFichier()
    {
        if (_runner.EnCours) _runner.Arreter();
        if (!Enregistrer()) return;
        if (_chemin == null) return;
        Sortie.Text = "";
        Saisie.Focus();
        _runner.Demarrer(_chemin,
            texte => Dispatcher.Invoke(() => { Sortie.AppendText(texte); Sortie.ScrollToEnd(); }),
            () => Dispatcher.Invoke(() => { Sortie.AppendText("\n[Programme terminé]\n"); Sortie.ScrollToEnd(); }));
    }

    private void Arreter_Click(object sender, RoutedEventArgs e) => _runner.Arreter();

    private void Saisie_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Sortie.AppendText(Saisie.Text + "\n");
        _runner.Envoyer(Saisie.Text);
        Saisie.Clear();
    }

    // ------------------------------------------------------------------
    // Aide
    // ------------------------------------------------------------------

    private void Raccourcis_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Ctrl+N : nouveau\nCtrl+O : ouvrir\nCtrl+S : enregistrer\nCtrl+W : fermer\n" +
            "Ctrl+F : rechercher\nCtrl+H : remplacer\nCtrl+Maj+F : rechercher dans les fichiers\n" +
            "F5 : exécuter",
            "Raccourcis clavier");
    }

    private void APropos_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("KeyCode\nÉditeur de code en C# (WPF, .NET 10, AvalonEdit).\n" +
                        "Projet de groupe : cabrelds, erwan-rk, DeboraQueen237, morelbrad27-lab, nyangono.",
            "À propos de KeyCode");
    }

    // ------------------------------------------------------------------
    // Raccourcis, glisser-déposer, fermeture
    // ------------------------------------------------------------------

    private void Fenetre_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            ExecuterFichier();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var maj = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.N when !maj: NouveauFichier(); e.Handled = true; break;
            case Key.O when !maj: Ouvrir_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.S when !maj: Enregistrer(); e.Handled = true; break;
            case Key.W when !maj: NouveauFichier(); e.Handled = true; break;
            // Ctrl+F, Ctrl+H, Ctrl+Maj+F : ajoutés après la fusion des recherches
        }
    }

    private void Fenetre_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] chemins) return;
        foreach (var chemin in chemins)
        {
            if (System.IO.File.Exists(chemin)) OuvrirFichier(chemin);
        }
    }

    private void Fenetre_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!DemanderEnregistrement())
        {
            e.Cancel = true;
            return;
        }
        _runner.Arreter();
        SettingsService.Enregistrer();
    }

    // ------------------------------------------------------------------
    // Titre et barre de statut (lit Contexte.EditeurActif.TextArea.Caret)
    // ------------------------------------------------------------------

    private void MettreAJourTitre()
    {
        var nom = _chemin == null ? "Sans titre" : System.IO.Path.GetFileName(_chemin);
        Title = Contexte.DossierRacine == null
            ? $"{nom} - KeyCode"
            : $"{nom} - {System.IO.Path.GetFileName(Contexte.DossierRacine)} - KeyCode";
    }

    private void SuivreEditeur()
    {
        if (_editeurSuivi != null)
            _editeurSuivi.TextArea.Caret.PositionChanged -= Caret_PositionChanged;

        _editeurSuivi = Contexte.EditeurActif;

        if (_editeurSuivi != null)
            _editeurSuivi.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        MettreAJourStatut();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e) => MettreAJourStatut();

    private void MettreAJourStatut()
    {
        var editeur = Contexte.EditeurActif;
        if (editeur == null)
        {
            StatutPosition.Text = "";
            StatutLangage.Text = "";
            StatutEncodage.Text = "";
            return;
        }

        var caret = editeur.TextArea.Caret;
        StatutPosition.Text = $"Ln {caret.Line}, Col {caret.Column}";

        var extension = Contexte.CheminActif == null ? "" : System.IO.Path.GetExtension(Contexte.CheminActif);
        StatutLangage.Text = LangagesService.Obtenir(extension)?.Nom
                             ?? editeur.SyntaxHighlighting?.Name
                             ?? "Texte brut";

        StatutEncodage.Text = (editeur.Encoding ?? System.Text.Encoding.UTF8).WebName.ToUpperInvariant();
    }
}
