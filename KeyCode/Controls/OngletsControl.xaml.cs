using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using KeyCode.Services;
using Microsoft.Win32;

namespace KeyCode.Controls;

/// <summary>
/// Contrôle de gestion des onglets d'éditeurs (ou visionneurs image/binaire) et de la restauration de session.
/// Propriétaire : DeboraQueen237.
/// </summary>
public partial class OngletsControl : UserControl
{
    private sealed class EtatOnglet
    {
        public TextEditor? Editeur { get; init; }
        public string? Chemin { get; set; }
        public required string TitreBase { get; set; }
        public required Border BadgeBordure { get; init; }
        public required TextBlock BadgeTexte { get; init; }
        public required TextBlock TitreTextBlock { get; init; }
        public required StackPanel PanneauEntete { get; init; }
        public EventHandler? ModifieHandler { get; set; }
        public DateTime? DerniereModifDisqueUtc { get; set; }
        public bool ExisteSurDisque { get; set; }
    }

    private const int MaxHistoriqueFermetures = 20;
    private const long TailleMaxTexte = 15 * 1024 * 1024;

    private static readonly HashSet<string> _extensionsImages = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico"
    };

    private static readonly DependencyPropertyDescriptor? DescripteurModifie =
        DependencyPropertyDescriptor.FromProperty(TextEditor.IsModifiedProperty, typeof(TextEditor));

    private readonly List<string> _historiqueFermetures = new();

    private Window? _fenetre;
    private bool _abonne;
    private bool _sessionRestauree;
    private bool _reorganisationEnCours;
    private bool _verificationExterneEnCours;
    private Point _pointDepartGlisser;
    private TabItem? _ongletGlisse;

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
        AppEvents.FichierRenomme += SurFichierRenomme;
        AppEvents.FichierSupprime += SurFichierSupprime;
        _abonne = true;
    }

    private void DesabonnerEvenements()
    {
        if (!_abonne) return;
        AppEvents.FichierDemande -= SurFichierDemande;
        AppEvents.FichierRenomme -= SurFichierRenomme;
        AppEvents.FichierSupprime -= SurFichierSupprime;
        _abonne = false;
    }

    private void OngletsControl_Loaded(object sender, RoutedEventArgs e)
    {
        AbonnerEvenements();

        if (_fenetre == null)
        {
            _fenetre = Window.GetWindow(this);
            if (_fenetre != null)
            {
                _fenetre.PreviewKeyDown += Fenetre_PreviewKeyDown;
                _fenetre.Activated += Fenetre_Activated;
            }
        }

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
            _fenetre.Activated -= Fenetre_Activated;
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

    private void SurFichierRenomme(string ancienChemin, string nouveauChemin)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var normAncien = NormaliserChemin(ancienChemin);
            var normNouveau = NormaliserChemin(nouveauChemin);
            if (normAncien == null || normNouveau == null) return;

            var onglet = TrouverOngletParChemin(normAncien);
            if (onglet?.Tag is not EtatOnglet etat) return;

            etat.Chemin = normNouveau;
            etat.TitreBase = Path.GetFileName(normNouveau);
            if (etat.Editeur != null)
                EditeurService.Configurer(etat.Editeur, normNouveau);
            CapturerEtatDisque(etat);
            MettreAJourEntete(onglet, etat);
            DiagnosticService.SupprimerFichier(normAncien);

            if (ReferenceEquals(Onglets.SelectedItem, onglet))
            {
                Contexte.CheminActif = normNouveau;
                MettreAJourFilAriane(normNouveau);
                AppEvents.SignalerEditeurActifChange();
            }
            SauvegarderEtatSession();
        });
    }

    private void SurFichierSupprime(string chemin)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var norm = NormaliserChemin(chemin);
            if (norm == null) return;
            DiagnosticService.SupprimerFichier(norm);

            var onglet = TrouverOngletParChemin(norm);
            if (onglet?.Tag is not EtatOnglet etat) return;

            if (etat.Editeur != null && etat.Editeur.IsModified)
            {
                etat.ExisteSurDisque = false;
                MettreAJourEntete(onglet, etat);
            }
            else
            {
                RetirerOngletSansConfirmation(onglet, memoriserFermeture: false);
            }
        });
    }

    // ------------------------------------------------------------------
    // API publique (contrats conservés)
    // ------------------------------------------------------------------

    public void OuvrirFichier(string chemin)
    {
        OuvrirFichierInterne(chemin, ajouterAuxRecents: true, selectionner: true, silencieux: false);
    }

    public void OuvrirFichierALaLigne(string chemin, int ligne)
    {
        var onglet = OuvrirFichierInterne(chemin, ajouterAuxRecents: true, selectionner: true, silencieux: false);
        if (onglet?.Tag is not EtatOnglet etat || etat.Editeur == null) return;

        var editeur = etat.Editeur;
        if (editeur.Document == null || editeur.Document.LineCount == 0) return;

        var ligneCible = Math.Clamp(ligne, 1, editeur.Document.LineCount);
        var ligneDoc = editeur.Document.GetLineByNumber(ligneCible);

        editeur.Select(ligneDoc.Offset, 0);
        editeur.TextArea.Caret.Offset = ligneDoc.Offset;
        editeur.ScrollToLine(ligneCible);

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(Onglets.SelectedItem, onglet)) return;
            editeur.ScrollToLine(ligneCible);
            editeur.TextArea.Caret.BringCaretToView();
            editeur.TextArea.Focus();
        });
    }

    public void NouvelOnglet()
    {
        var editeur = CreerEditeur(null);
        var titreBase = ProchainTitreSansTitre();
        var onglet = CreerOnglet(editeur, editeur, null, titreBase);

        Onglets.Items.Add(onglet);
        Onglets.SelectedItem = onglet;
        onglet.BringIntoView();

        MettreAJourPanneauVide();
        SynchroniserContexte();
        SauvegarderEtatSession();
        DonnerFocusEditeur(editeur);
    }

    public void FermerOngletActif()
    {
        if (Onglets.SelectedItem is TabItem onglet)
            FermerOnglet(onglet);
    }

    public bool EnregistrerActif()
    {
        if (Onglets.SelectedItem is not TabItem onglet) return false;
        return EnregistrerOnglet(onglet, forcerDialogue: false);
    }

    public void EnregistrerTout()
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (onglet.Tag is EtatOnglet etat && etat.Editeur is { IsModified: true })
            {
                if (!EnregistrerOnglet(onglet, forcerDialogue: false)) return;
            }
        }
    }

    public bool PeutFermer()
    {
        SauvegarderEtatSession();

        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (onglet.Tag is not EtatOnglet etat || etat.Editeur is not { IsModified: true })
                continue;

            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();

            var reponse = MessageBox.Show(
                $"Enregistrer les modifications de « {etat.TitreBase} » ?",
                "KeyCode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (reponse == MessageBoxResult.Cancel) return false;
            if (reponse == MessageBoxResult.Yes && !EnregistrerOnglet(onglet, forcerDialogue: false)) return false;
        }

        return true;
    }

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

    public void RouvrirDernierOngletFerme()
    {
        while (_historiqueFermetures.Count > 0)
        {
            var dernier = _historiqueFermetures[^1];
            _historiqueFermetures.RemoveAt(_historiqueFermetures.Count - 1);
            if (!File.Exists(dernier)) continue;
            OuvrirFichier(dernier);
            return;
        }
    }

    // ------------------------------------------------------------------
    // Restauration de session et création des éditeurs / visionneurs
    // ------------------------------------------------------------------

    private void RestaurerSession()
    {
        if (Onglets.Items.Count > 0)
        {
            MettreAJourPanneauVide();
            SynchroniserContexte();
            return;
        }

        var source = SettingsService.OngletsOuverts is { Count: > 0 }
            ? SettingsService.OngletsOuverts
            : SettingsService.DerniersFichiers;

        var fichiers = (source ?? new List<string>()).ToList();
        var restaures = new List<TabItem>();

        foreach (var chemin in fichiers)
        {
            if (string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin)) continue;
            var onglet = OuvrirFichierInterne(chemin, ajouterAuxRecents: false, selectionner: false, silencieux: true);
            if (onglet != null) restaures.Add(onglet);
        }

        if (restaures.Count > 0)
        {
            var idx = Math.Clamp(SettingsService.OngletActifIndex, 0, restaures.Count - 1);
            var cible = restaures[idx];
            Onglets.SelectedItem = cible;
            cible.BringIntoView();
            MettreAJourPanneauVide();
            SynchroniserContexte();
            if (cible.Tag is EtatOnglet etat && etat.Editeur != null)
                DonnerFocusEditeur(etat.Editeur);
        }
        else
        {
            NouvelOnglet();
        }
    }

    private TextEditor CreerEditeur(string? chemin)
    {
        var editeur = new TextEditor();
        EditeurService.Configurer(editeur, chemin);
        editeur.IsModified = false;

        var menu = new ContextMenu();
        var itemCouper = new MenuItem { Header = "Couper", InputGestureText = "Ctrl+X" };
        var itemCopier = new MenuItem { Header = "Copier", InputGestureText = "Ctrl+C" };
        var itemColler = new MenuItem { Header = "Coller", InputGestureText = "Ctrl+V" };
        var itemTout = new MenuItem { Header = "Tout sélectionner", InputGestureText = "Ctrl+A" };
        var itemExpliquer = new MenuItem { Header = "✦ Expliquer avec Gemini" };
        var itemCorriger = new MenuItem { Header = "✦ Corriger avec Gemini" };

        itemCouper.Click += (_, _) => editeur.Cut();
        itemCopier.Click += (_, _) => editeur.Copy();
        itemColler.Click += (_, _) => editeur.Paste();
        itemTout.Click += (_, _) => editeur.SelectAll();
        itemExpliquer.Click += (_, _) => AppEvents.DemanderAideIA("Explique clairement ce code et son fonctionnement.", Contexte.CheminActif, editeur.TextArea.Caret.Line);
        itemCorriger.Click += (_, _) => AppEvents.DemanderAideIA("Analyse ce code, détecte les erreurs potentielles et propose une version corrigée.", Contexte.CheminActif, editeur.TextArea.Caret.Line);

        menu.Items.Add(itemCouper);
        menu.Items.Add(itemCopier);
        menu.Items.Add(itemColler);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemTout);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemExpliquer);
        menu.Items.Add(itemCorriger);
        editeur.ContextMenu = menu;

        return editeur;
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
                if (existant.Tag is EtatOnglet etatExistant && etatExistant.Editeur != null)
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

        var ext = Path.GetExtension(cheminNormalise);
        TabItem onglet;

        if (_extensionsImages.Contains(ext))
        {
            var contenuImage = CreerVisionneurImage(cheminNormalise);
            onglet = CreerOnglet(contenuImage, null, cheminNormalise, Path.GetFileName(cheminNormalise));
        }
        else if (EstFichierBinaireOuTropGros(cheminNormalise, out var raison))
        {
            var contenuBinaire = CreerPanneauBinaire(cheminNormalise, raison);
            onglet = CreerOnglet(contenuBinaire, null, cheminNormalise, Path.GetFileName(cheminNormalise));
        }
        else
        {
            var editeur = CreerEditeur(null);
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
            onglet = CreerOnglet(editeur, editeur, cheminNormalise, Path.GetFileName(cheminNormalise));
        }

        if (onglet.Tag is EtatOnglet etat)
            CapturerEtatDisque(etat);

        Onglets.Items.Add(onglet);
        _historiqueFermetures.RemoveAll(c => string.Equals(c, cheminNormalise, StringComparison.OrdinalIgnoreCase));

        if (ajouterAuxRecents)
            AjouterAuxRecents(cheminNormalise);

        MettreAJourPanneauVide();

        if (selectionner)
        {
            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();
            SynchroniserContexte();
            if (onglet.Tag is EtatOnglet etatSel && etatSel.Editeur != null)
                DonnerFocusEditeur(etatSel.Editeur);
        }

        SauvegarderEtatSession();
        return onglet;
    }

    private static UIElement CreerVisionneurImage(string chemin)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(chemin, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            var pile = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24)
            };
            pile.Children.Add(new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxWidth = 900,
                MaxHeight = 600
            });
            pile.Children.Add(new TextBlock
            {
                Text = $"{Path.GetFileName(chemin)}  —  {bmp.PixelWidth} × {bmp.PixelHeight} px",
                Foreground = ThemeManager.Pinceau("ThemeTexteSecondaire"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 12
            });

            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = pile
            };
        }
        catch (Exception ex)
        {
            return CreerPanneauBinaire(chemin, $"Impossible de décoder l'image : {ex.Message}");
        }
    }

    private static UIElement CreerPanneauBinaire(string chemin, string raison)
    {
        var pile = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        };
        pile.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(chemin),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeManager.Pinceau("ThemeTexte"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        });
        pile.Children.Add(new TextBlock
        {
            Text = raison,
            FontSize = 12,
            Foreground = ThemeManager.Pinceau("ThemeTexteSecondaire"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var bouton = new Button
        {
            Content = "Ouvrir avec l'application par défaut",
            Style = (Style)Application.Current.FindResource("BoutonPrimaireVsCode"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        bouton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
            }
            catch (Exception) { }
        };
        pile.Children.Add(bouton);
        return pile;
    }

    private static bool EstFichierBinaireOuTropGros(string chemin, out string raison)
    {
        raison = "";
        try
        {
            var info = new FileInfo(chemin);
            if (info.Length > TailleMaxTexte)
            {
                raison = $"Fichier volumineux ({info.Length / (1024 * 1024)} Mo) non chargé dans l'éditeur.";
                return true;
            }

            var ext = info.Extension.ToLowerInvariant();
            if (ext is ".exe" or ".dll" or ".pdb" or ".zip" or ".7z" or ".rar" or ".pdf" or ".bin" or ".obj" or ".lib" or ".so")
            {
                raison = $"Fichier binaire ({ext}) : aperçu texte indisponible.";
                return true;
            }

            using var flux = File.OpenRead(chemin);
            var tampon = new byte[4096];
            var lus = flux.Read(tampon, 0, tampon.Length);
            for (var i = 0; i < lus; i++)
            {
                if (tampon[i] == 0)
                {
                    raison = "Ce fichier contient des données binaires et ne peut pas être édité comme du texte.";
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            raison = $"Lecture impossible : {ex.Message}";
            return true;
        }
    }

    private TabItem CreerOnglet(UIElement contenu, TextEditor? editeur, string? chemin, string titreBase)
    {
        var badgeTexte = new TextBlock
        {
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        var badgeBordure = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badgeTexte
        };

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

        var panneauEntete = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = Brushes.Transparent
        };
        panneauEntete.Children.Add(badgeBordure);
        panneauEntete.Children.Add(titreTextBlock);
        panneauEntete.Children.Add(boutonFermer);

        var menuEnregistrer = new MenuItem { Header = "Enregistrer" };
        var menuEnregistrerSous = new MenuItem { Header = "Enregistrer sous..." };
        var menuFermer = new MenuItem { Header = "Fermer" };
        var menuFermerAutres = new MenuItem { Header = "Fermer les autres" };
        var menuFermerEnregistres = new MenuItem { Header = "Fermer les onglets enregistrés" };
        var menuFermerTout = new MenuItem { Header = "Fermer tout" };
        var menuRouvrir = new MenuItem { Header = "Rouvrir l'onglet fermé (Ctrl+Maj+T)" };
        var menuCopierChemin = new MenuItem { Header = "Copier le chemin" };
        var menuOuvrirDossier = new MenuItem { Header = "Ouvrir le dossier contenant" };

        var menuContextuel = new ContextMenu();
        menuContextuel.Items.Add(menuEnregistrer);
        menuContextuel.Items.Add(menuEnregistrerSous);
        menuContextuel.Items.Add(new Separator());
        menuContextuel.Items.Add(menuFermer);
        menuContextuel.Items.Add(menuFermerAutres);
        menuContextuel.Items.Add(menuFermerEnregistres);
        menuContextuel.Items.Add(menuFermerTout);
        menuContextuel.Items.Add(new Separator());
        menuContextuel.Items.Add(menuRouvrir);
        menuContextuel.Items.Add(menuCopierChemin);
        menuContextuel.Items.Add(menuOuvrirDossier);
        panneauEntete.ContextMenu = menuContextuel;

        var etat = new EtatOnglet
        {
            Editeur = editeur,
            Chemin = chemin,
            TitreBase = titreBase,
            BadgeBordure = badgeBordure,
            BadgeTexte = badgeTexte,
            TitreTextBlock = titreTextBlock,
            PanneauEntete = panneauEntete,
            ExisteSurDisque = chemin != null && File.Exists(chemin)
        };

        var onglet = new TabItem
        {
            Header = panneauEntete,
            Content = contenu,
            Tag = etat,
            AllowDrop = true
        };

        if (editeur != null)
        {
            etat.ModifieHandler = (_, _) => MettreAJourEntete(onglet, etat);
            DescripteurModifie?.AddValueChanged(editeur, etat.ModifieHandler);
        }

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

        panneauEntete.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (EstDansBouton(e.OriginalSource as DependencyObject))
            {
                _ongletGlisse = null;
                return;
            }
            _pointDepartGlisser = e.GetPosition(null);
            _ongletGlisse = onglet;
        };

        panneauEntete.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _ongletGlisse == null) return;
            var delta = e.GetPosition(null) - _pointDepartGlisser;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var ongletADeplacer = _ongletGlisse;
            _ongletGlisse = null;
            DragDrop.DoDragDrop(ongletADeplacer, ongletADeplacer, DragDropEffects.Move);
        };

        panneauEntete.PreviewMouseLeftButtonUp += (_, _) => _ongletGlisse = null;

        onglet.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(typeof(TabItem))) return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };

        onglet.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(typeof(TabItem))) return;
            if (e.Data.GetData(typeof(TabItem)) is TabItem source)
                DeplacerOnglet(source, onglet);
            e.Handled = true;
        };

        menuContextuel.Opened += (_, _) =>
        {
            var estTexte = etat.Editeur != null;
            var aChemin = !string.IsNullOrWhiteSpace(etat.Chemin);
            menuEnregistrer.IsEnabled = estTexte;
            menuEnregistrerSous.IsEnabled = estTexte;
            menuFermerAutres.IsEnabled = Onglets.Items.Count > 1;
            menuFermerEnregistres.IsEnabled = Onglets.Items.OfType<TabItem>()
                .Any(t => t.Tag is EtatOnglet et && et.Editeur is not { IsModified: true });
            menuRouvrir.IsEnabled = _historiqueFermetures.Count > 0;
            menuCopierChemin.IsEnabled = aChemin;
            menuOuvrirDossier.IsEnabled = aChemin && File.Exists(etat.Chemin);
        };

        menuEnregistrer.Click += (_, _) => EnregistrerOnglet(onglet, forcerDialogue: false);
        menuEnregistrerSous.Click += (_, _) => EnregistrerOnglet(onglet, forcerDialogue: true);
        menuFermer.Click += (_, _) => FermerOnglet(onglet);
        menuFermerAutres.Click += (_, _) => FermerAutresOnglets(onglet);
        menuFermerEnregistres.Click += (_, _) => FermerOngletsEnregistres();
        menuFermerTout.Click += (_, _) => FermerTousOnglets();
        menuRouvrir.Click += (_, _) => RouvrirDernierOngletFerme();
        menuCopierChemin.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(etat.Chemin))
                Clipboard.SetText(etat.Chemin);
        };
        menuOuvrirDossier.Click += (_, _) => OuvrirDossierContenant(etat.Chemin);

        MettreAJourEntete(onglet, etat);
        return onglet;
    }

    private void DeplacerOnglet(TabItem source, TabItem cible)
    {
        if (ReferenceEquals(source, cible)) return;

        var indexSource = Onglets.Items.IndexOf(source);
        var indexCible = Onglets.Items.IndexOf(cible);
        if (indexSource < 0 || indexCible < 0) return;

        _reorganisationEnCours = true;
        try
        {
            Onglets.Items.RemoveAt(indexSource);
            Onglets.Items.Insert(indexCible, source);
            Onglets.SelectedItem = source;
            source.BringIntoView();
            SauvegarderEtatSession();
        }
        finally
        {
            _reorganisationEnCours = false;
        }
    }

    private bool EnregistrerOnglet(TabItem onglet, bool forcerDialogue)
    {
        if (onglet.Tag is not EtatOnglet etat) return false;
        if (etat.Editeur == null) return true; // Visionneur image/binaire : rien à sauvegarder

        var nouveauCheminAssigne = false;
        if (etat.Chemin == null || forcerDialogue)
        {
            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();

            var dialogue = new SaveFileDialog
            {
                Title = forcerDialogue ? "Enregistrer sous" : "Enregistrer le fichier",
                Filter = "Tous les fichiers (*.*)|*.*|Python (*.py)|*.py|C# (*.cs)|*.cs|JavaScript (*.js)|*.js|TypeScript (*.ts)|*.ts|HTML (*.html)|*.html|CSS (*.css)|*.css|Java (*.java)|*.java|C/C++ (*.c;*.cpp;*.h)|*.c;*.cpp;*.h|Texte (*.txt)|*.txt",
                FileName = etat.Chemin != null ? Path.GetFileName(etat.Chemin) : etat.TitreBase
            };
            if (etat.Chemin != null && Path.GetDirectoryName(etat.Chemin) is { } dossierActuel &&
                Directory.Exists(dossierActuel))
            {
                dialogue.InitialDirectory = dossierActuel;
            }
            else if (!string.IsNullOrWhiteSpace(Contexte.DossierRacine) && Directory.Exists(Contexte.DossierRacine))
            {
                dialogue.InitialDirectory = Contexte.DossierRacine;
            }

            if (dialogue.ShowDialog() != true) return false;

            var cheminChoisi = NormaliserChemin(dialogue.FileName) ?? dialogue.FileName;

            var doublon = TrouverOngletParChemin(cheminChoisi);
            if (doublon != null && !ReferenceEquals(doublon, onglet) &&
                doublon.Tag is EtatOnglet etatDoublon && etatDoublon.Editeur is not { IsModified: true })
            {
                RetirerOngletSansConfirmation(doublon, memoriserFermeture: false);
            }

            nouveauCheminAssigne = !string.Equals(etat.Chemin, cheminChoisi, StringComparison.OrdinalIgnoreCase);
            etat.Chemin = cheminChoisi;
            etat.TitreBase = Path.GetFileName(cheminChoisi);
            EditeurService.Configurer(etat.Editeur, etat.Chemin);
        }

        try
        {
            etat.Editeur.Save(etat.Chemin);
            etat.Editeur.IsModified = false;
            CapturerEtatDisque(etat);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'enregistrer le fichier :\n{ex.Message}", "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        MettreAJourEntete(onglet, etat);
        AjouterAuxRecents(etat.Chemin);
        SauvegarderEtatSession();

        if (ReferenceEquals(Onglets.SelectedItem, onglet))
        {
            Contexte.CheminActif = etat.Chemin;
            MettreAJourFilAriane(etat.Chemin);
            if (nouveauCheminAssigne)
                AppEvents.SignalerEditeurActifChange();
        }

        AppEvents.SignalerFichierEnregistre(etat.Chemin);
        return true;
    }

    private bool FermerOnglet(TabItem onglet)
    {
        if (onglet.Tag is EtatOnglet etat && etat.Editeur is { IsModified: true })
        {
            Onglets.SelectedItem = onglet;
            onglet.BringIntoView();

            var reponse = MessageBox.Show(
                $"Enregistrer les modifications de « {etat.TitreBase} » ?",
                "KeyCode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (reponse == MessageBoxResult.Cancel) return false;
            if (reponse == MessageBoxResult.Yes && !EnregistrerOnglet(onglet, forcerDialogue: false)) return false;
        }

        RetirerOngletSansConfirmation(onglet, memoriserFermeture: true);
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

    private void FermerOngletsEnregistres()
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (onglet.Tag is EtatOnglet etat && etat.Editeur is not { IsModified: true })
                RetirerOngletSansConfirmation(onglet, memoriserFermeture: true);
        }
    }

    private void FermerTousOnglets()
    {
        foreach (var onglet in Onglets.Items.OfType<TabItem>().ToList())
        {
            if (!FermerOnglet(onglet)) break;
        }
    }

    private void RetirerOngletSansConfirmation(TabItem onglet, bool memoriserFermeture)
    {
        if (onglet.Tag is EtatOnglet etat)
        {
            if (memoriserFermeture && !string.IsNullOrWhiteSpace(etat.Chemin))
                EnregistrerDansHistoriqueFermeture(etat.Chemin);

            if (etat.Editeur != null && etat.ModifieHandler != null)
            {
                DescripteurModifie?.RemoveValueChanged(etat.Editeur, etat.ModifieHandler);
                etat.ModifieHandler = null;
            }
        }

        Onglets.Items.Remove(onglet);
        MettreAJourPanneauVide();
        SynchroniserContexte();
        SauvegarderEtatSession();

        if (Onglets.SelectedItem is TabItem actif && actif.Tag is EtatOnglet etatActif && etatActif.Editeur != null)
            DonnerFocusEditeur(etatActif.Editeur);
    }

    private void EnregistrerDansHistoriqueFermeture(string chemin)
    {
        _historiqueFermetures.RemoveAll(c => string.Equals(c, chemin, StringComparison.OrdinalIgnoreCase));
        _historiqueFermetures.Add(chemin);
        if (_historiqueFermetures.Count > MaxHistoriqueFermetures)
            _historiqueFermetures.RemoveAt(0);
    }

    private void SauvegarderEtatSession()
    {
        SettingsService.OngletsOuverts = ObtenirCheminsOuverts();
        SettingsService.OngletActifIndex = Math.Max(0, Onglets.SelectedIndex);
    }

    // ------------------------------------------------------------------
    // Détection des modifications externes
    // ------------------------------------------------------------------

    private static void CapturerEtatDisque(EtatOnglet etat)
    {
        if (string.IsNullOrWhiteSpace(etat.Chemin) || !File.Exists(etat.Chemin))
        {
            etat.ExisteSurDisque = false;
            etat.DerniereModifDisqueUtc = null;
            return;
        }

        etat.ExisteSurDisque = true;
        etat.DerniereModifDisqueUtc = File.GetLastWriteTimeUtc(etat.Chemin);
    }

    private void VerifierModificationExterneOngletActif()
    {
        if (_verificationExterneEnCours) return;
        if (Onglets.SelectedItem is not TabItem onglet || onglet.Tag is not EtatOnglet etat) return;
        if (string.IsNullOrWhiteSpace(etat.Chemin) || etat.Editeur == null) return;

        _verificationExterneEnCours = true;
        try
        {
            if (!File.Exists(etat.Chemin))
            {
                if (!etat.ExisteSurDisque) return;
                etat.ExisteSurDisque = false;
                etat.DerniereModifDisqueUtc = null;
                etat.Editeur.IsModified = true;
                MettreAJourEntete(onglet, etat);
                return;
            }

            etat.ExisteSurDisque = true;
            var dateDisqueUtc = File.GetLastWriteTimeUtc(etat.Chemin);
            if (etat.DerniereModifDisqueUtc == null ||
                Math.Abs((dateDisqueUtc - etat.DerniereModifDisqueUtc.Value).TotalMilliseconds) < 150)
            {
                return;
            }

            etat.DerniereModifDisqueUtc = dateDisqueUtc;

            if (!etat.Editeur.IsModified)
            {
                RechargerDepuisDisque(onglet, etat);
                return;
            }

            var reponse = MessageBox.Show(
                $"Le fichier « {etat.TitreBase} » a été modifié en dehors de KeyCode.\n" +
                "Voulez-vous recharger la version du disque (vos modifications locales non enregistrées seront perdues) ?",
                "KeyCode",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (reponse == MessageBoxResult.Yes)
                RechargerDepuisDisque(onglet, etat);
        }
        finally
        {
            _verificationExterneEnCours = false;
        }
    }

    private void RechargerDepuisDisque(TabItem onglet, EtatOnglet etat)
    {
        if (etat.Editeur == null || string.IsNullOrWhiteSpace(etat.Chemin) || !File.Exists(etat.Chemin)) return;

        var lignePrecedente = etat.Editeur.TextArea.Caret.Line;
        try { etat.Editeur.Load(etat.Chemin); }
        catch (Exception) { return; }

        EditeurService.Configurer(etat.Editeur, etat.Chemin);
        etat.Editeur.IsModified = false;
        CapturerEtatDisque(etat);
        MettreAJourEntete(onglet, etat);

        if (etat.Editeur.Document.LineCount > 0)
        {
            var ligne = Math.Clamp(lignePrecedente, 1, etat.Editeur.Document.LineCount);
            var docLigne = etat.Editeur.Document.GetLineByNumber(ligne);
            etat.Editeur.TextArea.Caret.Offset = docLigne.Offset;
            etat.Editeur.ScrollToLine(ligne);
        }
    }

    // ------------------------------------------------------------------
    // Helpers d'état, fil d'Ariane, contexte et badges
    // ------------------------------------------------------------------

    private void SynchroniserContexte()
    {
        var etat = (Onglets.SelectedItem as TabItem)?.Tag as EtatOnglet;
        var editeur = etat?.Editeur;
        var chemin = etat?.Chemin;

        MettreAJourFilAriane(chemin);

        if (ReferenceEquals(Contexte.EditeurActif, editeur) &&
            string.Equals(Contexte.CheminActif, chemin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Contexte.CheminActif = chemin;
        Contexte.EditeurActif = editeur;
    }

    private void MettreAJourFilAriane(string? chemin)
    {
        Onglets.ApplyTemplate();
        var barre = Onglets.Template?.FindName("BarreFilAriane", Onglets) as Border;
        var texte = Onglets.Template?.FindName("TexteFilAriane", Onglets) as TextBlock;
        if (barre == null || texte == null) return;

        if (Onglets.Items.Count == 0)
        {
            barre.Visibility = Visibility.Collapsed;
            return;
        }

        barre.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(chemin))
        {
            texte.Text = "Sans titre";
            return;
        }

        if (!string.IsNullOrWhiteSpace(Contexte.DossierRacine) &&
            chemin.StartsWith(Contexte.DossierRacine, StringComparison.OrdinalIgnoreCase))
        {
            var relatif = Path.GetRelativePath(Contexte.DossierRacine, chemin);
            var racineNom = Path.GetFileName(Contexte.DossierRacine.TrimEnd(Path.DirectorySeparatorChar));
            texte.Text = $"{racineNom}  ›  {relatif.Replace(Path.DirectorySeparatorChar.ToString(), "  ›  ")}";
        }
        else
        {
            texte.Text = chemin.Replace(Path.DirectorySeparatorChar.ToString(), "  ›  ");
        }
    }

    private void MettreAJourEntete(TabItem onglet, EtatOnglet etat)
    {
        var (sigle, couleurHex) = ObtenirBadgeLangage(etat.Chemin);
        etat.BadgeTexte.Text = sigle;
        var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(couleurHex));
        pinceau.Freeze();
        etat.BadgeBordure.Background = pinceau;

        var modifie = etat.Editeur is { IsModified: true };
        etat.TitreTextBlock.Text = modifie ? $"{etat.TitreBase} *" : etat.TitreBase;
        etat.PanneauEntete.ToolTip = etat.Chemin ?? "Nouveau fichier non enregistré";
        onglet.ToolTip = null;
    }

    private static (string Sigle, string CouleurHex) ObtenirBadgeLangage(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return ("TXT", "#6E7681");

        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        return ext switch
        {
            ".py" or ".pyw" => ("PY", "#3572A5"),
            ".cs" => ("C#", "#68217A"),
            ".js" or ".mjs" or ".cjs" or ".jsx" => ("JS", "#C99A14"),
            ".ts" or ".tsx" => ("TS", "#3178C6"),
            ".java" => ("JV", "#B07219"),
            ".c" or ".h" => ("C", "#555555"),
            ".cpp" or ".cc" or ".cxx" or ".hpp" => ("C++", "#E04A7A"),
            ".php" => ("PHP", "#4F5D95"),
            ".go" => ("GO", "#00ADD8"),
            ".rs" => ("RS", "#B7410E"),
            ".ps1" => ("PS", "#25548F"),
            ".bat" or ".cmd" => ("BAT", "#4D4D4D"),
            ".md" => ("MD", "#1F6FEB"),
            ".json" => ("{}", "#2E8B57"),
            ".xml" or ".xaml" or ".csproj" => ("<>", "#D96523"),
            ".html" or ".htm" => ("HTM", "#E34C26"),
            ".css" or ".scss" => ("CSS", "#563D7C"),
            ".sql" => ("SQL", "#00758F"),
            ".yml" or ".yaml" => ("YML", "#CB171E"),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".svg" => ("IMG", "#008B8B"),
            _ when ext.Length > 1 => (ext[1..].ToUpperInvariant()[..Math.Min(3, ext.Length - 1)], "#6E7681"),
            _ => ("TXT", "#6E7681")
        };
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
        try { return Path.GetFullPath(chemin); }
        catch (Exception) { return null; }
    }

    private static bool EstDansBouton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static void OuvrirDossierContenant(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{chemin}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception) { }
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

    // ------------------------------------------------------------------
    // Gestionnaires d'événements UI et clavier
    // ------------------------------------------------------------------

    private void Onglets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_reorganisationEnCours) return;
        if (!ReferenceEquals(e.OriginalSource, Onglets)) return;

        MettreAJourPanneauVide();
        SynchroniserContexte();
        SauvegarderEtatSession();

        if (Onglets.SelectedItem is TabItem onglet)
        {
            onglet.BringIntoView();
            VerifierModificationExterneOngletActif();
            if (onglet.Tag is EtatOnglet etat && etat.Editeur != null)
                DonnerFocusEditeur(etat.Editeur);
        }
    }

    private void Fenetre_Activated(object? sender, EventArgs e)
    {
        VerifierModificationExterneOngletActif();
    }

    private void Fenetre_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mod = Keyboard.Modifiers;
        if ((mod & ModifierKeys.Control) == 0 || (mod & ModifierKeys.Alt) != 0) return;

        var touche = e.Key == Key.System ? e.SystemKey : e.Key;
        var maj = (mod & ModifierKeys.Shift) != 0;

        if (touche == Key.Tab)
        {
            if (Onglets.Items.Count > 1)
            {
                var decalage = maj ? -1 : 1;
                var total = Onglets.Items.Count;
                var index = Onglets.SelectedIndex < 0 ? 0 : Onglets.SelectedIndex;
                Onglets.SelectedIndex = (index + decalage + total) % total;
            }
            e.Handled = true;
            return;
        }

        if (touche == Key.T && maj)
        {
            RouvrirDernierOngletFerme();
            e.Handled = true;
            return;
        }

        var numero = ObtenirNumeroOnglet(touche);
        if (numero > 0 && !maj && Onglets.Items.Count > 0)
        {
            var indexCible = numero == 9 ? Onglets.Items.Count - 1 : numero - 1;
            if (indexCible < Onglets.Items.Count)
                Onglets.SelectedIndex = indexCible;
            e.Handled = true;
        }
    }

    private static int ObtenirNumeroOnglet(Key touche) => touche switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => 0
    };

    private void BoutonNouveau_Click(object sender, RoutedEventArgs e) => NouvelOnglet();
    private void BoutonRouvrir_Click(object sender, RoutedEventArgs e) => RouvrirDernierOngletFerme();

    private void BoutonListeOnglets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button bouton) return;

        var menu = new ContextMenu();
        if (Onglets.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(Aucun onglet ouvert)", IsEnabled = false });
        }
        else
        {
            foreach (var onglet in Onglets.Items.OfType<TabItem>())
            {
                if (onglet.Tag is not EtatOnglet etat) continue;
                var nom = etat.Editeur is { IsModified: true } ? $"{etat.TitreBase} *" : etat.TitreBase;
                var item = new MenuItem
                {
                    Header = nom.Replace("_", "__"),
                    IsChecked = ReferenceEquals(Onglets.SelectedItem, onglet),
                    ToolTip = etat.Chemin ?? "Nouveau fichier non enregistré"
                };
                var ongletCapture = onglet;
                item.Click += (_, _) =>
                {
                    Onglets.SelectedItem = ongletCapture;
                    ongletCapture.BringIntoView();
                };
                menu.Items.Add(item);
            }
        }

        menu.PlacementTarget = bouton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BarreOnglets_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;

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
