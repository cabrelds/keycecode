using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit;
using KeyCode.Controls;
using KeyCode.Services;
using Microsoft.Win32;

namespace KeyCode;

/// <summary>
/// Fenêtre principale VS Code de KeyCode : assemble barre d'activité, explorateur, recherche globale,
/// onglets d'éditeurs, diagnostics/terminal, assistant IA Gemini et palette de commandes.
/// </summary>
public partial class MainWindow : Window
{
    private enum VueGauche { Explorateur, Recherche, Extensions }

    private VueGauche _vueGauche = VueGauche.Explorateur;
    private TextEditor? _editeurAuDetail;
    private bool _badgeAnime;

    private TextEditor? _editeurSuivi;
    private double _largeurGauche = 260;
    private double _largeurIA = 340;
    private double _hauteurBas = 210;

    public MainWindow()
    {
        SettingsService.Charger();
        ExtensionsService.Initialiser();
        ThemeManager.Appliquer(SettingsService.Theme);

        _largeurGauche = SettingsService.LargeurPanneauGauche;
        _largeurIA = SettingsService.LargeurPanneauIA;
        _hauteurBas = SettingsService.HauteurPanneauBas;

        InitializeComponent();

        // La fenêtre ne doit jamais dépasser la zone utile de l'écran (sinon la barre d'état est coupée)
        var zone = SystemParameters.WorkArea;
        Height = Math.Min(Height, zone.Height);
        Width = Math.Min(Width, zone.Width);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Top = zone.Top + (zone.Height - Height) / 2;
        Left = zone.Left + (zone.Width - Width) / 2;

        ColonneGauche.Width = new GridLength(_largeurGauche);
        LigneBas.Height = new GridLength(_hauteurBas);
        if (SettingsService.PanneauIAVisible)
            AfficherGemini();

        AppEvents.FichierDemandeALaLigne += (chemin, ligne) => Onglets.OuvrirFichierALaLigne(chemin, ligne);
        AppEvents.FichierDemande += AjouterRecent;
        AppEvents.FichierEnregistre += AjouterRecent;
        AppEvents.EditeurActifChange += SuivreEditeur;
        AppEvents.DossierOuvert += _ => MettreAJourTitreEtDossier();
        AppEvents.DiagnosticsChanges += () => Dispatcher.InvokeAsync(MettreAJourCompteurDiagnostics);
        AppEvents.ThemeChange += _ => Dispatcher.InvokeAsync(MettreAJourBoutonTheme);
        AppEvents.AideIADemandee += (_, _, _) => Dispatcher.InvokeAsync(() => AfficherGemini());

        Terminal.DemandeMasquage += () => BasculerTerminal();

        InitialiserPaletteCommandes();
        InitialiserExtensions();

        if (!string.IsNullOrEmpty(SettingsService.DernierDossier)
            && Directory.Exists(SettingsService.DernierDossier))
        {
            Explorateur.OuvrirDossier(SettingsService.DernierDossier);
        }

        Loaded += (_, _) => TraiterArgumentsDemarrage();

        SuivreEditeur();
        ConstruireMenuRecents();
        MettreAJourTitreEtDossier();
        MettreAJourCompteurDiagnostics();
        MettreAJourBoutonTheme();
        MettreAJourZoomStatut();
    }

    private void TraiterArgumentsDemarrage()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            try
            {
                var complet = Path.GetFullPath(arg);
                if (Directory.Exists(complet))
                {
                    SettingsService.DernierDossier = complet;
                    Explorateur.OuvrirDossier(complet);
                }
                else if (File.Exists(complet))
                {
                    AppEvents.DemanderFichier(complet);
                }
            }
            catch (Exception)
            {
                // Ignore un argument de chemin invalide
            }
        }
    }

    private void InitialiserPaletteCommandes()
    {
        Palette.DefinirCommandes(
        [
            new("Fichier : Nouveau fichier", "Ctrl+N", () => Onglets.NouvelOnglet()),
            new("Fichier : Ouvrir un fichier...", "Ctrl+O", () => Ouvrir_Click(this, new RoutedEventArgs())),
            new("Fichier : Ouvrir un dossier...", "", () => OuvrirDossier_Click(this, new RoutedEventArgs())),
            new("Fichier : Enregistrer", "Ctrl+S", () => Onglets.EnregistrerActif()),
            new("Fichier : Tout enregistrer", "Ctrl+Maj+S", () => Onglets.EnregistrerTout()),
            new("Fichier : Fermer l'onglet", "Ctrl+W", () => Onglets.FermerOngletActif()),
            new("Fichier : Rouvrir l'onglet fermé", "Ctrl+Maj+T", () => Onglets.RouvrirDernierOngletFerme()),
            new("Affichage : Thème Sombre+ (VS Code)", "", () => ChangerTheme("Sombre")),
            new("Affichage : Thème Clair+ (VS Code)", "", () => ChangerTheme("Clair")),
            new("Affichage : Afficher / Masquer l'explorateur", "Ctrl+Maj+E", BasculerExplorateur),
            new("Affichage : Afficher / Masquer le terminal", "Ctrl+J", BasculerTerminal),
            new("Affichage : Afficher / Masquer Gemini AI", "Ctrl+Alt+I", BasculerGemini),
            new("Affichage : Zoom +", "", () => ChangerTaille(SettingsService.TaillePolice + 2)),
            new("Affichage : Zoom -", "", () => ChangerTaille(SettingsService.TaillePolice - 2)),
            new("Affichage : Réinitialiser le zoom", "", () => ChangerTaille(14)),
            new("Édition : Rechercher dans le fichier", "Ctrl+F", () => Recherche.Afficher(false)),
            new("Édition : Remplacer dans le fichier", "Ctrl+H", () => Recherche.Afficher(true)),
            new("Édition : Aller à la ligne...", "Ctrl+G", () => { if (Contexte.EditeurActif != null) Recherche.AfficherAllerALigne(); }),
            new("Édition : Rechercher dans les fichiers", "Ctrl+Maj+F", AfficherRechercheFichiers),
            new("Exécuter : Lancer le fichier actif", "F5", ExecuterFichier),
            new("Exécuter : Arrêter l'exécution", "", () => Terminal.Arreter()),
            new("Live Server : Démarrer / arrêter (Go Live)", "", BasculerLiveServer),
            new("Prettier : Formater le document", "Maj+Alt+F", FormaterDocument),
            new("Terminal : Ouvrir PowerShell interactif", "", OuvrirShell),
            new("Problèmes : Afficher la liste des erreurs", "", OuvrirProblemes),
            new("Gemini AI : Expliquer le fichier actif", "", () => ExpliquerIA_Click(this, new RoutedEventArgs())),
            new("Gemini AI : Corriger les erreurs", "", () => CorrigerIA_Click(this, new RoutedEventArgs())),
            new("Préférences : Paramètres et Clé API Gemini...", "", () => Parametres_Click(this, new RoutedEventArgs())),
        ]);
    }

    // ------------------------------------------------------------------
    // Fenêtre personnalisée (WindowChrome)
    // ------------------------------------------------------------------

    private void Fenetre_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            BordureRacine.Padding = new Thickness(7);
            BoutonAgrandir.Content = "❐";
        }
        else
        {
            BordureRacine.Padding = new Thickness(0);
            BoutonAgrandir.Content = "☐";
        }
    }

    private void ReduireFenetre_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void AgrandirFenetre_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

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
        Explorateur.OuvrirDossier(dlg.FolderName);
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e) => Onglets.EnregistrerActif();
    private void ToutEnregistrer_Click(object sender, RoutedEventArgs e) => Onglets.EnregistrerTout();
    private void FermerOnglet_Click(object sender, RoutedEventArgs e) => Onglets.FermerOngletActif();
    private void RouvrirOnglet_Click(object sender, RoutedEventArgs e) => Onglets.RouvrirDernierOngletFerme();
    private void Quitter_Click(object sender, RoutedEventArgs e) => Close();

    private void AjouterRecent(string chemin)
    {
        SettingsService.DerniersFichiers ??= new List<string>();
        var liste = SettingsService.DerniersFichiers;
        liste.RemoveAll(c => string.Equals(c, chemin, StringComparison.OrdinalIgnoreCase));
        liste.Insert(0, chemin);
        if (liste.Count > 10) liste.RemoveRange(10, liste.Count - 10);
        ConstruireMenuRecents();
    }

    private void ConstruireMenuRecents()
    {
        MenuRecents.Items.Clear();
        var liste = SettingsService.DerniersFichiers ?? new List<string>();
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
    // Édition & Palette
    // ------------------------------------------------------------------

    private void Annuler_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Undo();
    private void Retablir_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Redo();
    private void Couper_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Cut();
    private void Copier_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Copy();
    private void Coller_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.Paste();
    private void ToutSelectionner_Click(object sender, RoutedEventArgs e) => Contexte.EditeurActif?.SelectAll();

    private void Rechercher_Click(object sender, RoutedEventArgs e) => Recherche.Afficher(false);
    private void Remplacer_Click(object sender, RoutedEventArgs e) => Recherche.Afficher(true);

    private void AllerALigne_Click(object sender, RoutedEventArgs e)
    {
        if (Contexte.EditeurActif == null) return;
        Recherche.AfficherAllerALigne();
    }

    private void RechercheFichiers_Click(object sender, RoutedEventArgs e) => AfficherRechercheFichiers();
    private void PaletteCommandes_Click(object sender, RoutedEventArgs e) => Palette.Ouvrir(modeCommandes: true);
    private void PaletteFichiers_Click(object sender, RoutedEventArgs e) => Palette.Ouvrir(modeCommandes: false);

    private void AfficherRechercheFichiers()
    {
        _vueGauche = VueGauche.Recherche;
        if (PanneauGauche.Visibility != Visibility.Visible) BasculerExplorateur();
        AppliquerVueGauche();
    }

    // ------------------------------------------------------------------
    // Affichage, barre d'activité, Gemini AI et Paramètres
    // ------------------------------------------------------------------

    private void ActiviteExplorateur_Click(object sender, RoutedEventArgs e)
    {
        if (PanneauGauche.Visibility == Visibility.Visible && _vueGauche == VueGauche.Explorateur)
        {
            BasculerExplorateur();
            return;
        }

        _vueGauche = VueGauche.Explorateur;
        if (PanneauGauche.Visibility != Visibility.Visible) BasculerExplorateur();
        AppliquerVueGauche();
    }

    private void ActiviteRecherche_Click(object sender, RoutedEventArgs e)
    {
        if (PanneauGauche.Visibility == Visibility.Visible && _vueGauche == VueGauche.Recherche)
        {
            _vueGauche = VueGauche.Explorateur;
            AppliquerVueGauche();
        }
        else
        {
            AfficherRechercheFichiers();
        }
    }

    private void ActiviteExtensions_Click(object sender, RoutedEventArgs e)
    {
        if (PanneauGauche.Visibility == Visibility.Visible && _vueGauche == VueGauche.Extensions)
            BasculerExplorateur();
        else
            AfficherExtensions();
    }

    private void Extensions_Click(object sender, RoutedEventArgs e) => AfficherExtensions();

    /// <summary>Affiche la vue choisie dans le panneau gauche et met à jour la barre d'activité.</summary>
    private void AppliquerVueGauche()
    {
        PanneauExtensions.Visibility = _vueGauche == VueGauche.Extensions ? Visibility.Visible : Visibility.Collapsed;

        if (_vueGauche == VueGauche.Recherche)
            RechercheFichiers.Afficher();
        else if (RechercheFichiers.Visibility == Visibility.Visible)
            RechercheFichiers.Masquer();

        MettreAJourBarreActivite();
    }

    private void MettreAJourBarreActivite()
    {
        var visible = PanneauGauche.Visibility == Visibility.Visible;
        ActExplorateur.Tag = visible && _vueGauche == VueGauche.Explorateur ? "Actif" : null;
        ActRecherche.Tag = visible && _vueGauche == VueGauche.Recherche ? "Actif" : null;
        ActExtensions.Tag = visible && _vueGauche == VueGauche.Extensions ? "Actif" : null;
    }

    /// <summary>Ouvre le panneau Extensions ; une requête non nulle remplit le champ de recherche ("" = tout effacer).</summary>
    private void AfficherExtensions(string? requete = null)
    {
        _vueGauche = VueGauche.Extensions;
        if (PanneauGauche.Visibility != Visibility.Visible) BasculerExplorateur();
        AppliquerVueGauche();
        if (requete != null) PanneauExtensions.DefinirRecherche(requete);
        Dispatcher.BeginInvoke(() => PanneauExtensions.FocaliserRecherche());
    }

    private void ThemeClair_Click(object sender, RoutedEventArgs e) => ChangerTheme("Clair");
    private void ThemeSombre_Click(object sender, RoutedEventArgs e) => ChangerTheme("Sombre", "");
    private void ThemeOneDark_Click(object sender, RoutedEventArgs e) => AppliquerOneDarkPro();
    private void BasculerTheme_Click(object sender, RoutedEventArgs e) =>
        ChangerTheme(ThemeManager.ThemeActuel == "Sombre" ? "Clair" : "Sombre");

    /// <summary>Applique One Dark Pro si l'extension est active, sinon ouvre sa fiche dans la Place de marché.</summary>
    private void AppliquerOneDarkPro()
    {
        if (!ExtensionsService.EstActive(ExtensionsService.IdOneDarkPro))
        {
            AfficherExtensions("One Dark Pro");
            return;
        }
        ChangerTheme("Sombre", "OneDarkPro");
    }

    /// <param name="variante">null = conserver la variante actuelle ; "" = Sombre+ ; "OneDarkPro".</param>
    private void ChangerTheme(string theme, string? variante = null)
    {
        if (variante != null) ThemeManager.DefinirVariante(variante);
        ThemeManager.Appliquer(theme);
        SettingsService.Theme = theme;
        SettingsService.VarianteTheme = ThemeManager.Variante;
        MettreAJourBoutonTheme();
    }

    private void MettreAJourBoutonTheme() =>
        BoutonStatutTheme.Content = ThemeManager.ThemeActuel == "Clair"
            ? "☀ Clair"
            : ThemeManager.Variante == "OneDarkPro" ? "🌙 One Dark Pro" : "🌙 Sombre";

    private void ZoomPlus_Click(object sender, RoutedEventArgs e) => ChangerTaille(SettingsService.TaillePolice + 2);
    private void ZoomMoins_Click(object sender, RoutedEventArgs e) => ChangerTaille(SettingsService.TaillePolice - 2);
    private void ZoomNormal_Click(object sender, RoutedEventArgs e) => ChangerTaille(14);

    private void ChangerTaille(double taille)
    {
        taille = Math.Clamp(taille, 8, 48);
        SettingsService.TaillePolice = taille;
        if (Contexte.EditeurActif != null) Contexte.EditeurActif.FontSize = taille;
        EditeurService.ReconfigurerTous();
        MettreAJourZoomStatut();
    }

    private void MettreAJourZoomStatut()
    {
        var pct = (int)Math.Round(SettingsService.TaillePolice / 14.0 * 100);
        BoutonStatutZoom.Content = $"{pct}%";
    }

    private void BasculerExplorateur_Click(object sender, RoutedEventArgs e) => BasculerExplorateur();

    private void BasculerExplorateur()
    {
        if (PanneauGauche.Visibility == Visibility.Visible)
        {
            if (ColonneGauche.ActualWidth > 40) _largeurGauche = ColonneGauche.ActualWidth;
            PanneauGauche.Visibility = Visibility.Collapsed;
            SeparateurGauche.Visibility = Visibility.Collapsed;
            ColonneGauche.Width = new GridLength(0);
            ColonneSeparateurGauche.Width = new GridLength(0);
        }
        else
        {
            PanneauGauche.Visibility = Visibility.Visible;
            SeparateurGauche.Visibility = Visibility.Visible;
            ColonneGauche.Width = new GridLength(_largeurGauche);
            ColonneSeparateurGauche.Width = new GridLength(4);
        }

        MettreAJourBarreActivite();
    }

    private void BasculerTerminal_Click(object sender, RoutedEventArgs e) => BasculerTerminal();

    private void BasculerTerminal()
    {
        if (Terminal.Visibility == Visibility.Visible)
        {
            if (LigneBas.ActualHeight > 40) _hauteurBas = LigneBas.ActualHeight;
            Terminal.Visibility = Visibility.Collapsed;
            SeparateurBas.Visibility = Visibility.Collapsed;
            LigneBas.Height = new GridLength(0);
            LigneSeparateurBas.Height = new GridLength(0);
            ActProblemes.Tag = null;
        }
        else
        {
            Terminal.Visibility = Visibility.Visible;
            SeparateurBas.Visibility = Visibility.Visible;
            LigneBas.Height = new GridLength(_hauteurBas);
            LigneSeparateurBas.Height = new GridLength(4);
            ActProblemes.Tag = "Actif";
        }
    }

    private void BasculerIA_Click(object sender, RoutedEventArgs e) => BasculerGemini();

    private void BasculerGemini()
    {
        if (AssistantIA.Visibility == Visibility.Visible)
            MasquerGemini();
        else
            AfficherGemini();
    }

    /// <summary>Affiche Gemini dans le panneau de droite, à côté de l'éditeur.</summary>
    private void AfficherGemini()
    {
        AssistantIA.Visibility = Visibility.Visible;
        SeparateurIA.Visibility = Visibility.Visible;
        ColonneIA.Width = new GridLength(_largeurIA);
        ColonneSeparateurIA.Width = new GridLength(4);
        ActGemini.Tag = "Actif";
        SettingsService.PanneauIAVisible = true;
        Dispatcher.BeginInvoke(() => AssistantIA.FocaliserPrompt());
    }

    private void MasquerGemini()
    {
        if (ColonneIA.ActualWidth > 100) _largeurIA = ColonneIA.ActualWidth;
        AssistantIA.Visibility = Visibility.Collapsed;
        SeparateurIA.Visibility = Visibility.Collapsed;
        ColonneIA.Width = new GridLength(0);
        ColonneSeparateurIA.Width = new GridLength(0);
        ActGemini.Tag = null;
        SettingsService.PanneauIAVisible = false;
    }

    private void ExpliquerIA_Click(object sender, RoutedEventArgs e)
    {
        AfficherGemini();
        AppEvents.DemanderAideIA("Explique clairement ce fichier et son rôle.", Contexte.CheminActif, null);
    }

    private void CorrigerIA_Click(object sender, RoutedEventArgs e)
    {
        AfficherGemini();
        AppEvents.DemanderAideIA("Analyse les erreurs et propose une correction complète.", Contexte.CheminActif, null);
    }

    private void Parametres_Click(object sender, RoutedEventArgs e) => OuvrirFenetreParametres();

    private void OuvrirFenetreParametres()
    {
        var panel = new StackPanel { Margin = new Thickness(18) };

        panel.Children.Add(new TextBlock { Text = "Thème de l'interface :", FontWeight = FontWeights.SemiBold, Foreground = ThemeManager.Pinceau("ThemeTexte"), Margin = new Thickness(0, 0, 0, 4) });
        var comboTheme = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        comboTheme.Items.Add("Sombre");
        comboTheme.Items.Add("Clair");
        comboTheme.SelectedItem = ThemeManager.ThemeActuel;
        panel.Children.Add(comboTheme);

        panel.Children.Add(new TextBlock { Text = "Taille de la police (8 à 48) :", FontWeight = FontWeights.SemiBold, Foreground = ThemeManager.Pinceau("ThemeTexte"), Margin = new Thickness(0, 0, 0, 4) });
        var champPolice = new TextBox { Text = SettingsService.TaillePolice.ToString("0"), Style = (Style)Application.Current.FindResource("ChampVsCode"), Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(champPolice);

        panel.Children.Add(new TextBlock { Text = "Clé API Google Gemini :", FontWeight = FontWeights.SemiBold, Foreground = ThemeManager.Pinceau("ThemeTexte"), Margin = new Thickness(0, 0, 0, 4) });
        var champCle = new TextBox { Text = SettingsService.CleApiGemini, Style = (Style)Application.Current.FindResource("ChampVsCode"), Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(champCle);

        panel.Children.Add(new TextBlock { Text = "Modèle Gemini :", FontWeight = FontWeights.SemiBold, Foreground = ThemeManager.Pinceau("ThemeTexte"), Margin = new Thickness(0, 0, 0, 4) });
        var comboModele = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
        foreach (var m in GeminiService.ModelesParDefaut)
            comboModele.Items.Add(m);
        if (!string.IsNullOrWhiteSpace(SettingsService.ModeleGemini) &&
            !GeminiService.ModelesParDefaut.Contains(SettingsService.ModeleGemini, StringComparer.OrdinalIgnoreCase))
        {
            comboModele.Items.Insert(0, SettingsService.ModeleGemini);
        }
        comboModele.SelectedItem = SettingsService.ModeleGemini;
        if (comboModele.SelectedIndex < 0) comboModele.SelectedIndex = 0;
        panel.Children.Add(comboModele);

        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Button { Content = "Enregistrer", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("BoutonPrimaireVsCode") };
        var btnAnnuler = new Button { Content = "Annuler", IsCancel = true, Style = (Style)Application.Current.FindResource("BoutonSecondaireVsCode") };
        boutons.Children.Add(btnOk);
        boutons.Children.Add(btnAnnuler);
        panel.Children.Add(boutons);

        var fen = new Window
        {
            Title = "Paramètres - KeyCode",
            Content = panel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Background = ThemeManager.Pinceau("ThemeFondSecondaire"),
            Foreground = ThemeManager.Pinceau("ThemeTexte"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        btnOk.Click += (_, _) =>
        {
            if (comboTheme.SelectedItem is string th) ChangerTheme(th);
            if (double.TryParse(champPolice.Text, out var taille)) ChangerTaille(taille);
            SettingsService.CleApiGemini = champCle.Text.Trim();
            if (comboModele.SelectedItem is string mod) SettingsService.ModeleGemini = mod;
            SettingsService.Enregistrer();
            fen.DialogResult = true;
        };

        fen.ShowDialog();
    }

    // ------------------------------------------------------------------
    // Extensions : page de détail, badge de la barre d'activité, Code Runner
    // ------------------------------------------------------------------

    private void InitialiserExtensions()
    {
        AppEvents.ExtensionDemandee += AfficherDetailExtension;
        AppEvents.ExtensionsChangees += () => Dispatcher.InvokeAsync(() =>
        {
            MettreAJourBadgeExtensions();
            MettreAJourMenuCodeRunner();
        });

        // La page de détail se ferme quand on ouvre un fichier ou qu'on change d'onglet
        AppEvents.FichierDemande += _ => FermerDetailExtension();
        AppEvents.FichierDemandeALaLigne += (_, _) => FermerDetailExtension();
        AppEvents.EditeurActifChange += () =>
        {
            if (DetailExtension.Visibility == Visibility.Visible
                && !ReferenceEquals(Contexte.EditeurActif, _editeurAuDetail))
                FermerDetailExtension();
        };
        DetailExtension.DemandeFermeture += FermerDetailExtension;
        LiveServerService.EtatChange += () => Dispatcher.InvokeAsync(MettreAJourMenuCodeRunner);

        MettreAJourBadgeExtensions();
        MettreAJourMenuCodeRunner();
    }

    private void AfficherDetailExtension(string id)
    {
        DetailExtension.Afficher(id);
        _editeurAuDetail = Contexte.EditeurActif;
        DetailExtension.Visibility = Visibility.Visible;
    }

    private void FermerDetailExtension() => DetailExtension.Visibility = Visibility.Collapsed;

    private void MettreAJourBadgeExtensions()
    {
        var enCours = ExtensionsService.OperationEnCours;
        BadgeExtensions.Visibility = enCours ? Visibility.Visible : Visibility.Collapsed;

        if (enCours == _badgeAnime) return;
        _badgeAnime = enCours;

        if (enCours)
        {
            RotationBadge.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            RotationBadge.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    /// <summary>
    /// Met à jour les éléments qui dépendent d'une extension : menu Code Runner, menu Prettier, bouton Go Live.
    /// Arrête Live Server si son extension est désactivée. (Nom historique de la méthode.)
    /// </summary>
    private void MettreAJourMenuCodeRunner()
    {
        MenuCodeRunner.Visibility = ExtensionsService.EstActive(ExtensionsService.IdCodeRunner)
            ? Visibility.Visible : Visibility.Collapsed;
        MenuPrettier.Visibility = ExtensionsService.EstActive(ExtensionsService.IdPrettier)
            ? Visibility.Visible : Visibility.Collapsed;

        var live = ExtensionsService.EstActive(ExtensionsService.IdLiveServer);
        if (!live && LiveServerService.EnCours) LiveServerService.Arreter();

        BoutonGoLive.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        BoutonGoLive.Content = LiveServerService.EnCours ? $"⏻ Port : {LiveServerService.Port}" : "◉ Go Live";
        BoutonGoLive.ToolTip = LiveServerService.EnCours ? "Arrêter Live Server" : "Démarrer Live Server";
    }

    private void Formater_Click(object sender, RoutedEventArgs e) => FormaterDocument();
    private void GoLive_Click(object sender, RoutedEventArgs e) => BasculerLiveServer();

    /// <summary>Prettier : formate le document actif (JSON ou famille XML). Sans l'extension, ouvre sa fiche.</summary>
    private void FormaterDocument()
    {
        if (!ExtensionsService.EstActive(ExtensionsService.IdPrettier))
        {
            AfficherExtensions("Prettier");
            return;
        }

        var editeur = Contexte.EditeurActif;
        if (editeur == null) return;

        var extension = Contexte.CheminActif == null ? "" : Path.GetExtension(Contexte.CheminActif);
        string? resultat;
        try
        {
            resultat = FormateurService.Formater(editeur.Text, extension);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Formatage impossible : " + ex.Message, "Prettier");
            return;
        }

        if (resultat == null)
        {
            MessageBox.Show("Prettier prend en charge les fichiers enregistrés en .json, .xml, .xaml, .csproj, .svg...",
                "Prettier");
            return;
        }

        if (resultat == editeur.Text) return;
        var position = editeur.CaretOffset;
        editeur.Document.Replace(0, editeur.Document.TextLength, resultat);
        editeur.CaretOffset = Math.Min(position, editeur.Document.TextLength);
    }

    /// <summary>Live Server : démarre ou arrête le serveur local. Sans l'extension, ouvre sa fiche.</summary>
    private void BasculerLiveServer()
    {
        if (!ExtensionsService.EstActive(ExtensionsService.IdLiveServer))
        {
            AfficherExtensions("Live Server");
            return;
        }

        if (LiveServerService.EnCours)
        {
            LiveServerService.Arreter();
            return;
        }

        var racine = Contexte.DossierRacine;
        var courant = Contexte.CheminActif;

        // Fichier ouvert hors du dossier de l'explorateur : on sert le dossier du fichier
        if (courant != null && !string.IsNullOrEmpty(racine))
        {
            var prefixe = Path.GetFullPath(racine).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(courant).StartsWith(prefixe, StringComparison.OrdinalIgnoreCase))
                racine = Path.GetDirectoryName(courant);
        }
        racine ??= courant != null ? Path.GetDirectoryName(courant) : null;
        if (string.IsNullOrEmpty(racine))
        {
            MessageBox.Show("Ouvrez un dossier (ou enregistrez un fichier HTML) avant de lancer Live Server.", "Live Server");
            return;
        }

        if (!LiveServerService.Demarrer(racine))
        {
            MessageBox.Show("Aucun port libre entre 5500 et 5519.", "Live Server");
            return;
        }

        var actif = Contexte.CheminActif;
        var estHtml = actif != null
                      && (actif.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                          || actif.EndsWith(".htm", StringComparison.OrdinalIgnoreCase));
        ExtensionUi.OuvrirUrl(LiveServerService.Url(estHtml ? actif : null));
    }

    private void CodeRunner_Click(object sender, RoutedEventArgs e) => ExecuterCode();

    /// <summary>
    /// Code Runner : exécute la sélection (copiée dans un fichier temporaire de même extension),
    /// ou le fichier actif si rien n'est sélectionné. Sans l'extension active, ouvre sa fiche.
    /// </summary>
    private void ExecuterCode()
    {
        if (!ExtensionsService.EstActive(ExtensionsService.IdCodeRunner))
        {
            AfficherExtensions("Code Runner");
            return;
        }

        var editeur = Contexte.EditeurActif;
        var selection = editeur?.SelectedText;
        if (editeur == null || string.IsNullOrWhiteSpace(selection))
        {
            ExecuterFichier();
            return;
        }

        var extension = Contexte.CheminActif == null ? "" : Path.GetExtension(Contexte.CheminActif);
        if (extension.Length == 0 || LangagesService.Obtenir(extension) == null)
        {
            MessageBox.Show("Enregistrez d'abord le fichier avec une extension de langage pris en charge.",
                "Code Runner");
            return;
        }

        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".java", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Pour C# et Java, la sélection seule n'est pas prise en charge : exécutez le fichier complet (F5).",
                "Code Runner");
            return;
        }

        try
        {
            var dossier = Path.Combine(Path.GetTempPath(), "keycode_coderunner");
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, "selection" + extension.ToLowerInvariant());
            File.WriteAllText(fichier, selection, new System.Text.UTF8Encoding(false));

            if (Terminal.Visibility != Visibility.Visible) BasculerTerminal();
            Terminal.Executer(fichier);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Code Runner");
        }
    }

    // ------------------------------------------------------------------
    // Exécuter / Terminal / Problèmes
    // ------------------------------------------------------------------

    private void Executer_Click(object sender, RoutedEventArgs e) => ExecuterFichier();

    private void ExecuterFichier()
    {
        if (!Onglets.EnregistrerActif()) return;
        if (Contexte.CheminActif == null) return;

        if (Terminal.Visibility != Visibility.Visible) BasculerTerminal();
        Terminal.Executer(Contexte.CheminActif);
    }

    private void Arreter_Click(object sender, RoutedEventArgs e) => Terminal.Arreter();
    private void EffacerTerminal_Click(object sender, RoutedEventArgs e) => Terminal.Effacer();

    private void OuvrirProblemes_Click(object sender, RoutedEventArgs e) => OuvrirProblemes();
    private void OuvrirShell_Click(object sender, RoutedEventArgs e) => OuvrirShell();

    private void OuvrirProblemes()
    {
        if (Terminal.Visibility != Visibility.Visible) BasculerTerminal();
        Terminal.AfficherProblemes();
    }

    private void OuvrirShell()
    {
        if (Terminal.Visibility != Visibility.Visible) BasculerTerminal();
        Terminal.AfficherShell();
    }

    // ------------------------------------------------------------------
    // Aide
    // ------------------------------------------------------------------

    private void Raccourcis_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Ctrl+N : nouvel onglet\n" +
            "Ctrl+O : ouvrir un fichier\n" +
            "Ctrl+S / Ctrl+Maj+S : enregistrer / tout enregistrer\n" +
            "Ctrl+W / Ctrl+Maj+T : fermer / rouvrir l'onglet\n" +
            "Ctrl+Tab / Ctrl+1..9 : naviguer entre les onglets\n" +
            "Ctrl+P / Ctrl+Maj+P : accès rapide / palette de commandes\n" +
            "Ctrl+F / Ctrl+H / Ctrl+G : rechercher / remplacer / aller à la ligne\n" +
            "Ctrl+Maj+F : rechercher dans tout le projet\n" +
            "Ctrl+/ : commenter / décommenter   |   Alt+↑/↓ : déplacer ligne\n" +
            "Ctrl+Espace : auto-complétion   |   Ctrl+Molette : zoom\n" +
            "Ctrl+Maj+E : explorateur   |   Ctrl+J : panneau bas   |   Ctrl+Alt+I : Gemini AI\n" +
            "F5 : exécuter le fichier actif",
            "Raccourcis clavier - KeyCode");
    }

    private void APropos_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "KeyCode IDE v2.0\nÉditeur de code en C# (WPF, .NET 10, AvalonEdit) avec assistant IA Gemini.\n\n" +
            "Équipe : cabrelds, erwan-rk, DeboraQueen237, morelbrad27-lab, nyangono.",
            "À propos de KeyCode");
    }

    // ------------------------------------------------------------------
    // Raccourcis globaux, glisser-déposer et fermeture
    // ------------------------------------------------------------------

    private void Fenetre_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            ExecuterFichier();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F1)
        {
            Palette.Ouvrir(modeCommandes: true);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Shift | ModifierKeys.Alt)
            && (e.Key == Key.System ? e.SystemKey : e.Key) == Key.F
            && ExtensionsService.EstActive(ExtensionsService.IdPrettier))
        {
            FormaterDocument();
            e.Handled = true;
            return;
        }

        var mod = Keyboard.Modifiers;
        if ((mod & ModifierKeys.Control) == 0) return;

        var maj = (mod & ModifierKeys.Shift) != 0;
        var alt = (mod & ModifierKeys.Alt) != 0;
        var touche = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (touche)
        {
            case Key.N when !maj && !alt: Onglets.NouvelOnglet(); e.Handled = true; break;
            case Key.O when !maj && !alt: Ouvrir_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.S when !maj && !alt: Onglets.EnregistrerActif(); e.Handled = true; break;
            case Key.S when maj && !alt: Onglets.EnregistrerTout(); e.Handled = true; break;
            case Key.W when !maj && !alt: Onglets.FermerOngletActif(); e.Handled = true; break;
            case Key.P when !maj && !alt: Palette.Ouvrir(modeCommandes: false); e.Handled = true; break;
            case Key.P when maj && !alt: Palette.Ouvrir(modeCommandes: true); e.Handled = true; break;
            case Key.E when maj && !alt: ActiviteExplorateur_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F when maj && !alt: AfficherRechercheFichiers(); e.Handled = true; break;
            case Key.X when maj && !alt: AfficherExtensions(); e.Handled = true; break;
            case Key.J when !maj && !alt: BasculerTerminal(); e.Handled = true; break;
            case Key.I when alt && !maj: BasculerGemini(); e.Handled = true; break;
            case Key.N when alt && !maj && ExtensionsService.EstActive(ExtensionsService.IdCodeRunner):
                ExecuterCode(); e.Handled = true; break;
        }
    }

    private void Fenetre_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] chemins) return;
        foreach (var chemin in chemins)
        {
            if (File.Exists(chemin))
            {
                AppEvents.DemanderFichier(chemin);
            }
            else if (Directory.Exists(chemin))
            {
                SettingsService.DernierDossier = chemin;
                Explorateur.OuvrirDossier(chemin);
            }
        }
    }

    private void Fenetre_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!Onglets.PeutFermer())
        {
            e.Cancel = true;
            return;
        }

        if (PanneauGauche.Visibility == Visibility.Visible && ColonneGauche.ActualWidth > 40)
            SettingsService.LargeurPanneauGauche = ColonneGauche.ActualWidth;
        if (Terminal.Visibility == Visibility.Visible && LigneBas.ActualHeight > 40)
            SettingsService.HauteurPanneauBas = LigneBas.ActualHeight;
        if (AssistantIA.Visibility == Visibility.Visible && ColonneIA.ActualWidth > 100)
            SettingsService.LargeurPanneauIA = ColonneIA.ActualWidth;

        LiveServerService.Arreter();
        Terminal.Arreter();
        SettingsService.Enregistrer();
    }

    // ------------------------------------------------------------------
    // Titre et barre de statut
    // ------------------------------------------------------------------

    private void MettreAJourTitreEtDossier()
    {
        var nom = Contexte.CheminActif == null ? "Sans titre" : Path.GetFileName(Contexte.CheminActif);
        Title = Contexte.DossierRacine == null
            ? $"{nom} - KeyCode"
            : $"{nom} - {Path.GetFileName(Contexte.DossierRacine)} - KeyCode";

        StatutDossier.Text = string.IsNullOrWhiteSpace(Contexte.DossierRacine)
            ? "Aucun dossier"
            : $"📁 {Path.GetFileName(Contexte.DossierRacine.TrimEnd(Path.DirectorySeparatorChar))}";
    }

    private void MettreAJourCompteurDiagnostics()
    {
        var (err, warn) = DiagnosticService.Compter();
        BoutonStatutProblemes.Content = $"⊗ {err}   ⚠ {warn}";
    }

    private void SuivreEditeur()
    {
        if (_editeurSuivi != null)
            _editeurSuivi.TextArea.Caret.PositionChanged -= Caret_PositionChanged;

        _editeurSuivi = Contexte.EditeurActif;

        if (_editeurSuivi != null)
            _editeurSuivi.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        MettreAJourStatut();
        MettreAJourTitreEtDossier();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e) => MettreAJourStatut();

    private void MettreAJourStatut()
    {
        var editeur = Contexte.EditeurActif;
        if (editeur == null)
        {
            StatutPosition.Text = "";
            StatutLangage.Text = Contexte.CheminActif != null ? Path.GetExtension(Contexte.CheminActif).ToUpperInvariant() : "";
            StatutEncodage.Text = "";
            StatutIndentation.Text = "";
            return;
        }

        var caret = editeur.TextArea.Caret;
        StatutPosition.Text = $"Ln {caret.Line}, Col {caret.Column}";

        StatutIndentation.Text = editeur.Options.ConvertTabsToSpaces
            ? $"Espaces: {editeur.Options.IndentationSize}"
            : $"Tab: {editeur.Options.IndentationSize}";

        var extension = Contexte.CheminActif == null ? "" : Path.GetExtension(Contexte.CheminActif);
        StatutLangage.Text = LangagesService.Obtenir(extension)?.Nom
                             ?? ColorationService.ObtenirNomLangage(Contexte.CheminActif)
                             ?? editeur.SyntaxHighlighting?.Name
                             ?? "Texte brut";

        StatutEncodage.Text = (editeur.Encoding ?? System.Text.Encoding.UTF8).WebName.ToUpperInvariant();
        MettreAJourZoomStatut();
    }
}
