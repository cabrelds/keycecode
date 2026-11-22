using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using KeyCode.Services;

namespace KeyCode;

/// <summary>
/// Fenêtre principale : assemble l'explorateur, les onglets, les recherches et le terminal.
/// Propriétaire : cabrelds.
/// </summary>
public partial class MainWindow : Window
{
    private TextEditor? _editeurSuivi;
    private double _largeurGauche = 260;

    public MainWindow()
    {
        InitializeComponent();

        SettingsService.Charger();
        ThemeManager.Appliquer(SettingsService.Theme);

        // OngletsControl écoute lui-même AppEvents.FichierDemande et rouvre la session au chargement.
        // La fenêtre ne gère que l'ouverture à une ligne précise (venant de la recherche dans les fichiers).
        AppEvents.FichierDemandeALaLigne += (chemin, ligne) => Onglets.OuvrirFichierALaLigne(chemin, ligne);

        // Mise à jour du menu des fichiers récents
        AppEvents.FichierDemande += AjouterRecent;
        AppEvents.FichierEnregistre += AjouterRecent;

        // Barre de statut et titre
        AppEvents.EditeurActifChange += SuivreEditeur;
        AppEvents.DossierOuvert += _ => MettreAJourTitre();

        // Réouvre le dernier dossier s'il existe encore
        if (!string.IsNullOrEmpty(SettingsService.DernierDossier)
            && System.IO.Directory.Exists(SettingsService.DernierDossier))
        {
            Explorateur.OuvrirDossier(SettingsService.DernierDossier);
        }

        SuivreEditeur();
        ConstruireMenuRecents();
        MettreAJourTitre();
    }

    // ------------------------------------------------------------------
    // Fichier
    // ------------------------------------------------------------------

    private void Nouveau_Click(object sender, RoutedEventArgs e) => Onglets.NouvelOnglet();

    private void Ouvrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog();
        if (dlg.ShowDialog() == true) AppEvents.DemanderFichier(dlg.FileName);
    }

    private void OuvrirDossier_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        SettingsService.DernierDossier = dlg.FolderName;
        // OuvrirDossier met à jour Contexte.DossierRacine et signale DossierOuvert
        Explorateur.OuvrirDossier(dlg.FolderName);
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e) => Onglets.EnregistrerActif();
    private void ToutEnregistrer_Click(object sender, RoutedEventArgs e) => Onglets.EnregistrerTout();
    private void FermerOnglet_Click(object sender, RoutedEventArgs e) => Onglets.FermerOngletActif();
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
            item.Click += (_, _) => AppEvents.DemanderFichier(chemin);
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

    // Ctrl+F, Ctrl+H et Ctrl+G sont captés directement par RechercheControl
    private void Rechercher_Click(object sender, RoutedEventArgs e) => Recherche.Afficher(false);
    private void Remplacer_Click(object sender, RoutedEventArgs e) => Recherche.Afficher(true);
    private void RechercheFichiers_Click(object sender, RoutedEventArgs e) => AfficherRechercheFichiers();

    private void AfficherRechercheFichiers()
    {
        // Si l'explorateur est masqué, on le réaffiche pour voir la recherche
        if (PanneauGauche.Visibility != Visibility.Visible) BasculerExplorateur();
        RechercheFichiers.Afficher();
    }

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
        // L'onglet actif change tout de suite ; ReconfigurerTous (nyangono) met à jour les autres onglets
        if (Contexte.EditeurActif != null) Contexte.EditeurActif.FontSize = taille;
        EditeurService.ReconfigurerTous();
    }

    private void BasculerExplorateur_Click(object sender, RoutedEventArgs e) => BasculerExplorateur();

    private void BasculerExplorateur()
    {
        if (PanneauGauche.Visibility == Visibility.Visible)
        {
            _largeurGauche = ColonneGauche.ActualWidth;
            PanneauGauche.Visibility = Visibility.Collapsed;
            SeparateurGauche.Visibility = Visibility.Collapsed;
            ColonneGauche.Width = new GridLength(0);
            ColonneSeparateur.Width = new GridLength(0);
        }
        else
        {
            PanneauGauche.Visibility = Visibility.Visible;
            SeparateurGauche.Visibility = Visibility.Visible;
            ColonneGauche.Width = new GridLength(_largeurGauche);
            ColonneSeparateur.Width = new GridLength(5);
        }
    }

    private void BasculerTerminal_Click(object sender, RoutedEventArgs e)
    {
        Terminal.Visibility = Terminal.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // Exécuter
    // ------------------------------------------------------------------

    private void Executer_Click(object sender, RoutedEventArgs e) => ExecuterFichier();

    private void ExecuterFichier()
    {
        // Enregistrer d'abord ; EnregistrerActif retourne false si aucun onglet ou si l'utilisateur annule
        if (!Onglets.EnregistrerActif()) return;
        if (Contexte.CheminActif == null) return;

        if (Terminal.Visibility != Visibility.Visible) Terminal.Visibility = Visibility.Visible;
        Terminal.Executer(Contexte.CheminActif);
    }

    private void Arreter_Click(object sender, RoutedEventArgs e) => Terminal.Arreter();
    private void EffacerTerminal_Click(object sender, RoutedEventArgs e) => Terminal.Effacer();

    // ------------------------------------------------------------------
    // Aide
    // ------------------------------------------------------------------

    private void Raccourcis_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Ctrl+N : nouvel onglet\nCtrl+O : ouvrir\nCtrl+S : enregistrer\nCtrl+W : fermer l'onglet\n" +
            "Ctrl+Tab : onglet suivant\nCtrl+F : rechercher\nCtrl+H : remplacer\nCtrl+G : aller à la ligne\n" +
            "Ctrl+Maj+F : rechercher dans les fichiers\nF5 : exécuter",
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
            case Key.N when !maj: Onglets.NouvelOnglet(); e.Handled = true; break;
            case Key.O when !maj: Ouvrir_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.S when !maj: Onglets.EnregistrerActif(); e.Handled = true; break;
            case Key.W when !maj: Onglets.FermerOngletActif(); e.Handled = true; break;
            case Key.F when maj: AfficherRechercheFichiers(); e.Handled = true; break;
            // Ctrl+F, Ctrl+H, Ctrl+G : gérés par RechercheControl ; Ctrl+Tab : géré par OngletsControl
        }
    }

    private void Fenetre_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] chemins) return;
        foreach (var chemin in chemins)
        {
            if (System.IO.File.Exists(chemin))
            {
                AppEvents.DemanderFichier(chemin);
            }
            else if (System.IO.Directory.Exists(chemin))
            {
                SettingsService.DernierDossier = chemin;
                Explorateur.OuvrirDossier(chemin);
            }
        }
    }

    private void Fenetre_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // PeutFermer demande d'enregistrer les onglets modifiés ; false = l'utilisateur a annulé
        if (!Onglets.PeutFermer())
        {
            e.Cancel = true;
            return;
        }
        Terminal.Arreter();
        SettingsService.Enregistrer();
    }

    // ------------------------------------------------------------------
    // Titre et barre de statut (lit Contexte.EditeurActif.TextArea.Caret)
    // ------------------------------------------------------------------

    private void MettreAJourTitre()
    {
        var nom = Contexte.CheminActif == null ? "Sans titre" : System.IO.Path.GetFileName(Contexte.CheminActif);
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
        MettreAJourTitre();
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