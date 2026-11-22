using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using KeyCode.Services;
using Microsoft.Win32;

namespace KeyCode.Controls;

/// <summary>
/// Contrôle de gestion des onglets d'éditeurs et de la restauration de session.
/// Propriétaire : DeboraQueen237.
/// </summary>
public partial class OngletsControl : UserControl
{
    private sealed class EtatOnglet
    {
        public required TextEditor Editeur { get; init; }
        public string? Chemin { get; set; }
        public required string TitreBase { get; set; }
        public required TextBlock TitreTextBlock { get; init; }
        public required StackPanel PanneauEntete { get; init; }
        public required MenuItem MenuCopierChemin { get; init; }
        public EventHandler? ModifieHandler { get; set; }
    }

    private static readonly DependencyPropertyDescriptor? DescripteurModifie =
        DependencyPropertyDescriptor.FromProperty(TextEditor.IsModifiedProperty, typeof(TextEditor));

    private Window? _fenetre;
    private bool _abonne;
    private bool _sessionRestauree;

    public OngletsControl()
    {
        InitializeComponent();
        AbonnerEvenements();
    }

    // ------------------------------------------------------------------
    // Cycle de vie et abonnements
    // ------------------------------------------------------------------

    private void AbonnerEvenements()
    {
        if (_abonne) return;
        AppEvents.FichierDemande += SurFichierDemande;
        AppEvents.ThemeChange += SurThemeChange;
        _abonne = true;
    }

    private void DesabonnerEvenements()
    {
        if (!_abonne) return;
        AppEvents.FichierDemande -= SurFichierDemande;
        AppEvents.ThemeChange -= SurThemeChange;
        _abonne = false;
    }

    private void OngletsControl_Loaded(object sender, RoutedEventArgs e)
    {
        AbonnerEvenements();

        if (_fenetre == null)
        {
            _fenetre = Window.GetWindow(this);
            if (_fenetre != null)
                _fenetre.PreviewKeyDown += Fenetre_PreviewKeyDown;
        }

        AppliquerTheme(ThemeManager.ThemeActuel);

        if (!_sessionRestauree)
        {
            _sessionRestauree = true;
            RestaurerSession();
        }
    }

    private void OngletsControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_fenetre != null)
        {
            _fenetre.PreviewKeyDown -= Fenetre_PreviewKeyDown;
            _fenetre = null;
        }

        DesabonnerEvenements();
    }

    private void SurFichierDemande(string chemin)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OuvrirFichier(chemin));
            return;
        }

        OuvrirFichier(chemin);
    }

    private void SurThemeChange(string theme)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => AppliquerTheme(theme));
            return;
        }

        AppliquerTheme(theme);
    }

    // ------------------------------------------------------------------
    // API publique (contrats)
    // ------------------------------------------------------------------

    /// <summary>Ouvre un fichier dans un nouvel onglet, ou active son onglet s'il est déjà ouvert.</summary>
    public void OuvrirFichier(string chemin)
    {
        OuvrirFichierInterne(chemin, ajouterAuxRecents: true, selectionner: true, silencieux: false);
    }

    /// <summary>Ouvre un fichier et positionne le curseur à la ligne indiquée (à partir de 1).</summary>
    public void OuvrirFichierALaLigne(string chemin, int ligne)
    {
        var onglet = OuvrirFichierInterne(chemin, ajouterAuxRecents: true, selectionner: true, silencieux: false);
        if (onglet?.Tag is not EtatOnglet etat) return;

        var editeur = etat.Editeur;
        if (editeur.Document == null || editeur.Document.LineCount == 0) return;

        var ligneCible = Math.Clamp(ligne, 1, editeur.Document.LineCount);
        var ligneDoc = editeur.Document.GetLineByNumber(ligneCible);

        editeur.Select(ligneDoc.Offset, 0);
        editeur.TextArea.Caret.Offset = ligneDoc.Offset;
        editeur.ScrollToLine(ligneCible);

        // Diffère le défilement après la mise en page si l'éditeur vient juste d'être créé
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(Onglets.SelectedItem, onglet)) return;
            editeur.ScrollToLine(ligneCible);
            editeur.TextArea.Caret.BringCaretToView();
            editeur.TextArea.Focus();
        });
    }

    /// <summary>Crée et active un nouvel onglet vide.</summary>
    public void NouvelOnglet()
    {
        var editeur = new TextEditor();
        EditeurService.Configurer(editeur, null);
        editeur.IsModified = false;

        var titreBase = ProchainTitreSansTitre();
        var onglet = CreerOnglet(editeur, null, titreBase);

        Onglets.Items.Add(onglet);
        Onglets.SelectedItem = onglet;
        onglet.BringIntoView();

        MettreAJourPanneauVide();
        SynchroniserContexte();
        DonnerFocusEditeur(editeur);
    }

    /// <summary>Ferme l'onglet actif après confirmation d'enregistrement si nécessaire.</summary>
    public void FermerOngletActif()
    {
        if (Onglets.SelectedItem is TabItem onglet)
            FermerOnglet(onglet);
    }

    /// <summary>Enregistre l'onglet actif. Retourne false si aucun onglet ou si l'utilisateur annule.</summary>
    public bool EnregistrerActif()
    {
        if (Onglets.SelectedItem is not TabItem onglet) return false;
        return EnregistrerOnglet(onglet);
    }

    /// <summary>Enregistre tous les onglets modifiés.</summary>
    public void EnregistrerTout()
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (onglet.Tag is EtatOnglet etat && etat.Editeur.IsModified)
            {
                if (!EnregistrerOnglet(onglet)) return;
            }
        }
    }

    /// <summary>
    /// Vérifie si les onglets modifiés peuvent être fermés en proposant l'enregistrement.
    /// Retourne false si l'utilisateur annule.
    /// </summary>
    public bool PeutFermer()
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (onglet.Tag is not EtatOnglet etat || !etat.Editeur.IsModified)
                continue;

            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();

            var reponse = MessageBox.Show(
                $"Enregistrer les modifications de « {etat.TitreBase} » ?",
                "KeyCode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (reponse == MessageBoxResult.Cancel) return false;
            if (reponse == MessageBoxResult.Yes && !EnregistrerOnglet(onglet)) return false;
        }

        return true;
    }

    /// <summary>Retourne la liste des chemins des fichiers actuellement ouverts dans les onglets.</summary>
    public List<string> ObtenirCheminsOuverts()
    {
        var chemins = new List<string>();
        foreach (var onglet in Onglets.Items.OfType<TabItem>())
        {
            if (onglet.Tag is EtatOnglet etat && !string.IsNullOrWhiteSpace(etat.Chemin))
                chemins.Add(etat.Chemin);
        }

        return chemins;
    }

    // ------------------------------------------------------------------
    // Restauration de session et gestion interne des onglets
    // ------------------------------------------------------------------

    private void RestaurerSession()
    {
        // Si des onglets ont déjà été ajoutés avant Loaded, on les conserve
        if (Onglets.Items.Count > 0)
        {
            MettreAJourPanneauVide();
            SynchroniserContexte();
            return;
        }

        var fichiers = (SettingsService.DerniersFichiers ?? new List<string>()).ToList();
        TabItem? premierRestaure = null;

        foreach (var chemin in fichiers)
        {
            if (string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin)) continue;
            var onglet = OuvrirFichierInterne(chemin, ajouterAuxRecents: false, selectionner: false, silencieux: true);
            premierRestaure ??= onglet;
        }

        if (premierRestaure != null)
        {
            Onglets.SelectedItem = premierRestaure;
            premierRestaure.BringIntoView();
            MettreAJourPanneauVide();
            SynchroniserContexte();
            if (premierRestaure.Tag is EtatOnglet etat)
                DonnerFocusEditeur(etat.Editeur);
        }
        else
        {
            NouvelOnglet();
        }
    }

    private TabItem? OuvrirFichierInterne(string chemin, bool ajouterAuxRecents, bool selectionner, bool silencieux)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return null;

        var cheminNormalise = NormaliserChemin(chemin);
        if (cheminNormalise == null)
        {
            if (!silencieux)
            {
                MessageBox.Show($"Chemin de fichier invalide :\n{chemin}", "KeyCode",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return null;
        }

        var existant = TrouverOngletParChemin(cheminNormalise);
        if (existant != null)
        {
            if (selectionner)
            {
                Onglets.SelectedItem = existant;
                existant.BringIntoView();
                SynchroniserContexte();
                if (existant.Tag is EtatOnglet etatExistant)
                    DonnerFocusEditeur(etatExistant.Editeur);
            }
            return existant;
        }

        if (!File.Exists(cheminNormalise))
        {
            if (!silencieux)
            {
                MessageBox.Show($"Fichier introuvable :\n{cheminNormalise}", "KeyCode",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return null;
        }

        var editeur = new TextEditor();
        try
        {
            editeur.Load(cheminNormalise);
        }
        catch (Exception ex)
        {
            if (!silencieux)
            {
                MessageBox.Show($"Impossible d'ouvrir le fichier :\n{ex.Message}", "KeyCode",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
        }

        EditeurService.Configurer(editeur, cheminNormalise);
        editeur.IsModified = false;

        var onglet = CreerOnglet(editeur, cheminNormalise, Path.GetFileName(cheminNormalise));
        Onglets.Items.Add(onglet);

        if (ajouterAuxRecents)
            AjouterAuxRecents(cheminNormalise);

        MettreAJourPanneauVide();

        if (selectionner)
        {
            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();
            SynchroniserContexte();
            DonnerFocusEditeur(editeur);
        }

        return onglet;
    }

    private TabItem CreerOnglet(TextEditor editeur, string? chemin, string titreBase)
    {
        var titreTextBlock = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var boutonFermer = new Button
        {
            Content = "✕",
            ToolTip = "Fermer l'onglet (Ctrl+W)",
            Style = (Style)FindResource("BoutonFermerOnglet")
        };

        // Fond transparent pour capter le clic milieu sur toute la surface de l'en-tête
        var panneauEntete = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = Brushes.Transparent
        };
        panneauEntete.Children.Add(titreTextBlock);
        panneauEntete.Children.Add(boutonFermer);

        var menuFermer = new MenuItem { Header = "Fermer" };
        var menuFermerAutres = new MenuItem { Header = "Fermer les autres" };
        var menuFermerTout = new MenuItem { Header = "Fermer tout" };
        var menuCopierChemin = new MenuItem { Header = "Copier le chemin" };

        var menuContextuel = new ContextMenu();
        menuContextuel.Items.Add(menuFermer);
        menuContextuel.Items.Add(menuFermerAutres);
        menuContextuel.Items.Add(menuFermerTout);
        menuContextuel.Items.Add(new Separator());
        menuContextuel.Items.Add(menuCopierChemin);
        panneauEntete.ContextMenu = menuContextuel;

        var etat = new EtatOnglet
        {
            Editeur = editeur,
            Chemin = chemin,
            TitreBase = titreBase,
            TitreTextBlock = titreTextBlock,
            PanneauEntete = panneauEntete,
            MenuCopierChemin = menuCopierChemin
        };

        var onglet = new TabItem
        {
            Header = panneauEntete,
            Content = editeur,
            Tag = etat
        };

        etat.ModifieHandler = (_, _) => MettreAJourEntete(onglet, etat);
        DescripteurModifie?.AddValueChanged(editeur, etat.ModifieHandler);

        boutonFermer.Click += (_, e) =>
        {
            e.Handled = true;
            FermerOnglet(onglet);
        };

        panneauEntete.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                FermerOnglet(onglet);
            }
        };

        menuContextuel.Opened += (_, _) =>
        {
            menuFermerAutres.IsEnabled = Onglets.Items.Count > 1;
            menuCopierChemin.IsEnabled = !string.IsNullOrWhiteSpace(etat.Chemin);
        };
        menuFermer.Click += (_, _) => FermerOnglet(onglet);
        menuFermerAutres.Click += (_, _) => FermerAutresOnglets(onglet);
        menuFermerTout.Click += (_, _) => FermerTousOnglets();
        menuCopierChemin.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(etat.Chemin))
                Clipboard.SetText(etat.Chemin);
        };

        MettreAJourEntete(onglet, etat);
        return onglet;
    }

    private bool EnregistrerOnglet(TabItem onglet)
    {
        if (onglet.Tag is not EtatOnglet etat) return false;

        var nouveauCheminAssigne = false;
        if (etat.Chemin == null)
        {
            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();

            var dialogue = new SaveFileDialog
            {
                Title = "Enregistrer le fichier",
                Filter = "Tous les fichiers (*.*)|*.*|Python (*.py)|*.py|C# (*.cs)|*.cs|JavaScript (*.js)|*.js|Java (*.java)|*.java|C/C++ (*.c;*.cpp;*.h)|*.c;*.cpp;*.h|Texte (*.txt)|*.txt",
                FileName = etat.TitreBase
            };
            if (!string.IsNullOrWhiteSpace(Contexte.DossierRacine) && Directory.Exists(Contexte.DossierRacine))
                dialogue.InitialDirectory = Contexte.DossierRacine;

            if (dialogue.ShowDialog() != true) return false;

            var cheminChoisi = NormaliserChemin(dialogue.FileName) ?? dialogue.FileName;

            // Si un autre onglet non modifié pointe déjà vers ce même chemin, on évite le doublon
            var doublon = TrouverOngletParChemin(cheminChoisi);
            if (doublon != null && !ReferenceEquals(doublon, onglet) &&
                doublon.Tag is EtatOnglet etatDoublon && !etatDoublon.Editeur.IsModified)
            {
                RetirerOngletSansConfirmation(doublon);
            }

            etat.Chemin = cheminChoisi;
            etat.TitreBase = Path.GetFileName(cheminChoisi);
            EditeurService.Configurer(etat.Editeur, etat.Chemin);
            nouveauCheminAssigne = true;
        }

        try
        {
            etat.Editeur.Save(etat.Chemin);
            etat.Editeur.IsModified = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'enregistrer le fichier :\n{ex.Message}", "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        MettreAJourEntete(onglet, etat);
        AjouterAuxRecents(etat.Chemin);

        if (ReferenceEquals(Onglets.SelectedItem, onglet))
        {
            Contexte.CheminActif = etat.Chemin;
            if (nouveauCheminAssigne)
                AppEvents.SignalerEditeurActifChange();
        }

        AppEvents.SignalerFichierEnregistre(etat.Chemin);
        return true;
    }

    private bool FermerOnglet(TabItem onglet)
    {
        if (onglet.Tag is EtatOnglet etat && etat.Editeur.IsModified)
        {
            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();

            var reponse = MessageBox.Show(
                $"Enregistrer les modifications de « {etat.TitreBase} » ?",
                "KeyCode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (reponse == MessageBoxResult.Cancel) return false;
            if (reponse == MessageBoxResult.Yes && !EnregistrerOnglet(onglet)) return false;
        }

        RetirerOngletSansConfirmation(onglet);
        return true;
    }

    private void FermerAutresOnglets(TabItem ongletAConserver)
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (ReferenceEquals(onglet, ongletAConserver)) continue;
            if (!FermerOnglet(onglet)) break;
        }
    }

    private void FermerTousOnglets()
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (!FermerOnglet(onglet)) break;
        }
    }

    private void RetirerOngletSansConfirmation(TabItem onglet)
    {
        if (onglet.Tag is EtatOnglet etat && etat.ModifieHandler != null)
        {
            DescripteurModifie?.RemoveValueChanged(etat.Editeur, etat.ModifieHandler);
            etat.ModifieHandler = null;
        }

        Onglets.Items.Remove(onglet);
        MettreAJourPanneauVide();
        SynchroniserContexte();

        if (Onglets.SelectedItem is TabItem actif && actif.Tag is EtatOnglet etatActif)
            DonnerFocusEditeur(etatActif.Editeur);
    }

    // ------------------------------------------------------------------
    // Helpers d'état, de contexte et de thème
    // ------------------------------------------------------------------

    private void SynchroniserContexte()
    {
        var etat = (Onglets.SelectedItem as TabItem)?.Tag as EtatOnglet;
        var editeur = etat?.Editeur;
        var chemin = etat?.Chemin;

        if (ReferenceEquals(Contexte.EditeurActif, editeur) &&
            string.Equals(Contexte.CheminActif, chemin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Important : CheminActif doit être mis à jour AVANT EditeurActif (qui déclenche EditeurActifChange)
        Contexte.CheminActif = chemin;
        Contexte.EditeurActif = editeur;
    }

    private void MettreAJourEntete(TabItem onglet, EtatOnglet etat)
    {
        var modifie = etat.Editeur.IsModified;
        etat.TitreTextBlock.Text = modifie ? $"{etat.TitreBase} *" : etat.TitreBase;
        etat.PanneauEntete.ToolTip = etat.Chemin ?? "Nouveau fichier non enregistré";
        onglet.ToolTip = null;
    }

    private void MettreAJourPanneauVide()
    {
        PanneauVide.Visibility = Onglets.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TabItem? TrouverOngletParChemin(string cheminNormalise)
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>())
        {
            if (onglet.Tag is EtatOnglet etat && etat.Chemin != null &&
                string.Equals(NormaliserChemin(etat.Chemin), cheminNormalise, StringComparison.OrdinalIgnoreCase))
            {
                return onglet;
            }
        }

        return null;
    }

    private string ProchainTitreSansTitre()
    {
        var utilises = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var onglet in Onglets.Items.OfType<TabItem>())
        {
            if (onglet.Tag is EtatOnglet etat && etat.Chemin == null)
                utilises.Add(etat.TitreBase);
        }

        if (!utilises.Contains("Sans titre")) return "Sans titre";

        var numero = 2;
        while (utilises.Contains($"Sans titre {numero}"))
            numero++;

        return $"Sans titre {numero}";
    }

    private static string? NormaliserChemin(string chemin)
    {
        try
        {
            return Path.GetFullPath(chemin);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AjouterAuxRecents(string chemin)
    {
        SettingsService.DerniersFichiers ??= new List<string>();
        var liste = SettingsService.DerniersFichiers;
        liste.RemoveAll(c => string.Equals(c, chemin, StringComparison.OrdinalIgnoreCase));
        liste.Insert(0, chemin);
        if (liste.Count > 10)
            liste.RemoveRange(10, liste.Count - 10);
    }

    private void DonnerFocusEditeur(TextEditor editeur)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (Onglets.SelectedItem is TabItem actif && actif.Tag is EtatOnglet etat &&
                ReferenceEquals(etat.Editeur, editeur))
            {
                editeur.TextArea.Focus();
            }
        });
    }

    private void AppliquerTheme(string theme)
    {
        var sombre = string.Equals(theme, "Sombre", StringComparison.OrdinalIgnoreCase);

        DefinirCouleur("CouleurFondRacine", sombre ? "#1E1E1E" : "#FFFFFF");
        DefinirCouleur("CouleurFondBarreOnglets", sombre ? "#252526" : "#F3F3F3");
        DefinirCouleur("CouleurBordureOnglets", sombre ? "#3C3C3C" : "#DDDDDD");
        DefinirCouleur("CouleurOngletInactif", sombre ? "#2D2D2D" : "#ECECEC");
        DefinirCouleur("CouleurOngletSurvol", sombre ? "#383838" : "#E2E2E2");
        DefinirCouleur("CouleurOngletActif", sombre ? "#1E1E1E" : "#FFFFFF");
        DefinirCouleur("CouleurAccentOnglet", sombre ? "#007FD4" : "#007ACC");
        DefinirCouleur("CouleurTexteOnglet", sombre ? "#CCCCCC" : "#444444");
        DefinirCouleur("CouleurTexteActif", sombre ? "#FFFFFF" : "#111111");
        DefinirCouleur("CouleurTexteSecondaire", sombre ? "#969696" : "#666666");
        DefinirCouleur("CouleurSurvolBouton", sombre ? "#3C3C3C" : "#D6D6D6");
    }

    private void DefinirCouleur(string cle, string hex)
    {
        var couleur = (Color)ColorConverter.ConvertFromString(hex);
        var pinceau = new SolidColorBrush(couleur);
        pinceau.Freeze();
        Resources[cle] = pinceau;
    }

    // ------------------------------------------------------------------
    // Gestionnaires d'événements UI et clavier
    // ------------------------------------------------------------------

    private void Onglets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ignore les événements SelectionChanged remontés depuis un contrôle interne de l'éditeur
        if (!ReferenceEquals(e.OriginalSource, Onglets)) return;

        MettreAJourPanneauVide();
        SynchroniserContexte();

        if (Onglets.SelectedItem is TabItem onglet)
        {
            onglet.BringIntoView();
            if (onglet.Tag is EtatOnglet etat)
                DonnerFocusEditeur(etat.Editeur);
        }
    }

    private void Fenetre_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;

        var mod = Keyboard.Modifiers;
        if ((mod & ModifierKeys.Control) == 0 || (mod & ModifierKeys.Alt) != 0) return;

        if (Onglets.Items.Count > 1)
        {
            var decalage = (mod & ModifierKeys.Shift) != 0 ? -1 : 1;
            var total = Onglets.Items.Count;
            var index = Onglets.SelectedIndex < 0 ? 0 : Onglets.SelectedIndex;
            Onglets.SelectedIndex = (index + decalage + total) % total;
        }

        e.Handled = true;
    }

    private void BoutonNouveau_Click(object sender, RoutedEventArgs e) => NouvelOnglet();

    private void BarreOnglets_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;

        // Ouvre un nouvel onglet uniquement si le double-clic est sur la zone libre de la barre
        if (e.OriginalSource is DependencyObject source)
        {
            var parent = source;
            while (parent != null && parent != sender)
            {
                if (parent is TabItem || parent is ButtonBase || parent is ScrollBar) return;
                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        NouvelOnglet();
        e.Handled = true;
    }

    private void DefileurOnglets_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer defileur) return;
        defileur.ScrollToHorizontalOffset(defileur.HorizontalOffset - e.Delta / 3.0);
        e.Handled = true;
    }
}
