using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>
/// Panneau latéral « Extensions » (Place de marché) : sections Installé / Recommandées,
/// recherche avec filtres, installation, activation et désinstallation.
/// </summary>
public partial class ExtensionsControl : UserControl
{
    private bool _installeeOuverte = true;
    private bool _recommandeesOuverte = true;
    private string? _idSelection;
    private readonly Dictionary<string, Border> _lignes = new(StringComparer.OrdinalIgnoreCase);

    public ExtensionsControl()
    {
        InitializeComponent();

        BoutonActualiser.Content = ExtensionUi.Pictogramme(ExtensionUi.DonneesActualiser, "ThemeTexte", 16, 1.3);
        BoutonPlus.Content = ExtensionUi.Pictogramme(ExtensionUi.DonneesPlus, "ThemeTexte", 16, 1, true);
        BoutonEffacer.Content = ExtensionUi.Pictogramme(ExtensionUi.DonneesEffacer, "ThemeTexteSecondaire", 16, 1.1);
        BoutonFiltre.Content = ExtensionUi.Pictogramme(ExtensionUi.DonneesFiltre, "ThemeTexteSecondaire", 16, 1.1);

        AppEvents.ExtensionsChangees += SurExtensionsChangees;
        AppEvents.ExtensionDemandee += SurExtensionDemandee;
        Unloaded += (_, _) =>
        {
            AppEvents.ExtensionsChangees -= SurExtensionsChangees;
            AppEvents.ExtensionDemandee -= SurExtensionDemandee;
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Reconstruire();
        };
    }

    // ------------------------------------------------------------------
    // API publique
    // ------------------------------------------------------------------

    public void DefinirRecherche(string texte)
    {
        ChampRecherche.Text = texte;
        ChampRecherche.CaretIndex = texte.Length;
    }

    public void FocaliserRecherche()
    {
        ChampRecherche.Focus();
        Keyboard.Focus(ChampRecherche);
    }

    // ------------------------------------------------------------------
    // Événements
    // ------------------------------------------------------------------

    private void SurExtensionsChangees() =>
        Dispatcher.InvokeAsync(() =>
        {
            if (IsVisible) Reconstruire();
        });

    private void SurExtensionDemandee(string id)
    {
        _idSelection = id;
        MettreAJourFonds();
    }

    private void ChampRecherche_TextChanged(object sender, TextChangedEventArgs e) => Reconstruire();

    private void ChampRecherche_GotFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ExtensionUi.Ref(BordureRecherche, Border.BorderBrushProperty, "ThemeAccent");

    private void ChampRecherche_LostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ExtensionUi.Ref(BordureRecherche, Border.BorderBrushProperty, "ThemeSaisieBordure");

    private void ChampRecherche_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ChampRecherche.Clear();
        e.Handled = true;
    }

    private void Effacer_Click(object sender, RoutedEventArgs e)
    {
        ChampRecherche.Clear();
        FocaliserRecherche();
    }

    private async void Actualiser_Click(object sender, RoutedEventArgs e) =>
        await ExtensionsService.ActualiserAsync();

    private void Plus_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = BoutonPlus, Placement = PlacementMode.Bottom };
        menu.Items.Add(ExtensionUi.ItemMenu("Afficher les extensions installées", () => DefinirRecherche("@installed")));
        menu.Items.Add(ExtensionUi.ItemMenu("Afficher les extensions activées", () => DefinirRecherche("@enabled")));
        menu.Items.Add(ExtensionUi.ItemMenu("Afficher les extensions désactivées", () => DefinirRecherche("@disabled")));
        menu.Items.Add(ExtensionUi.ItemMenu("Afficher les extensions recommandées", () => DefinirRecherche("@recommended")));
        menu.Items.Add(new Separator());
        menu.Items.Add(ExtensionUi.ItemMenu("Effacer les résultats de la recherche d'extensions", () => ChampRecherche.Clear()));
        menu.Items.Add(new Separator());
        menu.Items.Add(ExtensionUi.ItemMenu("Désactiver toutes les extensions installées", ExtensionsService.ToutDesactiver));
        menu.Items.Add(ExtensionUi.ItemMenu("Activer toutes les extensions", ExtensionsService.ToutActiver));
        menu.IsOpen = true;
    }

    private void Filtre_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = BoutonFiltre, Placement = PlacementMode.Bottom };
        menu.Items.Add(ExtensionUi.ItemMenu("Installé", () => DefinirRecherche("@installed")));
        menu.Items.Add(ExtensionUi.ItemMenu("Activé", () => DefinirRecherche("@enabled")));
        menu.Items.Add(ExtensionUi.ItemMenu("Désactivé", () => DefinirRecherche("@disabled")));
        menu.Items.Add(ExtensionUi.ItemMenu("Recommandées", () => DefinirRecherche("@recommended")));
        menu.Items.Add(new Separator());

        var categories = new MenuItem { Header = "Catégories" };
        foreach (var categorie in new[] { "Themes", "Programming Languages", "Other" })
        {
            var nom = categorie;
            categories.Items.Add(ExtensionUi.ItemMenu(nom, () => DefinirRecherche($"@category:\"{nom.ToLowerInvariant()}\"")));
        }
        menu.Items.Add(categories);

        var tri = new MenuItem { Header = "Trier par" };
        tri.Items.Add(ItemTri("Installations", "installs"));
        tri.Items.Add(ItemTri("Note", "rating"));
        tri.Items.Add(ItemTri("Nom", "name"));
        menu.Items.Add(tri);

        menu.IsOpen = true;
    }

    private MenuItem ItemTri(string entete, string valeur)
    {
        var actif = Regex.IsMatch(ChampRecherche.Text, "@sort:" + valeur + @"(\s|$)");
        return ExtensionUi.ItemMenu(entete, () => DefinirTri(valeur), actif);
    }

    private void DefinirTri(string valeur)
    {
        var base_ = Regex.Replace(ChampRecherche.Text, @"\s*@sort:\S+", "").Trim();
        DefinirRecherche((base_ + " @sort:" + valeur).Trim());
    }

    // ------------------------------------------------------------------
    // Construction de la liste
    // ------------------------------------------------------------------

    private void Reconstruire()
    {
        var requete = ChampRecherche.Text.Trim();
        TitrePanneau.Text = requete.Length == 0 ? "Extensions" : "Extensions: Marketplace";
        Indication.Visibility = ChampRecherche.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var decalage = Defileur.VerticalOffset;
        Liste.Children.Clear();
        _lignes.Clear();

        if (requete.Length == 0)
        {
            AjouterSection("Installé", ExtensionsService.Rechercher("@installed"), _installeeOuverte,
                () => { _installeeOuverte = !_installeeOuverte; Reconstruire(); }, true);
            AjouterSection("Recommandées", ExtensionsService.Rechercher("@recommended"), _recommandeesOuverte,
                () => { _recommandeesOuverte = !_recommandeesOuverte; Reconstruire(); }, false);
        }
        else
        {
            var resultats = ExtensionsService.Rechercher(requete);
            if (resultats.Count == 0)
            {
                Liste.Children.Add(ExtensionUi.Texte("Aucune extension trouvée.", 12.5, "ThemeTexteSecondaire",
                    null, new Thickness(20, 14, 12, 0)));
            }
            foreach (var info in resultats)
                Liste.Children.Add(CreerLigne(info));
        }

        Defileur.UpdateLayout();
        Defileur.ScrollToVerticalOffset(decalage);
    }

    private void AjouterSection(string titre, List<ExtensionInfo> elements, bool ouverte, Action basculer, bool premiere)
    {
        var entete = new Border
        {
            Height = 24,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0, premiere ? 0 : 1, 0, 0)
        };
        ExtensionUi.Ref(entete, Border.BorderBrushProperty, "ThemeBordure");

        var pile = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var chevron = ExtensionUi.Pictogramme(
            ouverte ? ExtensionUi.DonneesChevronBas : ExtensionUi.DonneesChevronDroite, "ThemeTexte", 16, 1.5);
        chevron.Margin = new Thickness(3, 0, 2, 0);
        pile.Children.Add(chevron);

        pile.Children.Add(ExtensionUi.Texte(titre, 12, "ThemeTexte", FontWeights.Bold));

        var pastille = new Border
        {
            MinWidth = 18,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = elements.Count.ToString(CultureInfo.InvariantCulture),
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        ExtensionUi.Ref(pastille, Border.BackgroundProperty, "ThemeAccent");
        pile.Children.Add(pastille);

        entete.Child = pile;
        entete.MouseLeftButtonUp += (_, _) => basculer();
        Liste.Children.Add(entete);

        if (!ouverte) return;
        foreach (var info in elements)
            Liste.Children.Add(CreerLigne(info));
    }

    private Border CreerLigne(ExtensionInfo info)
    {
        var id = info.Id;
        var installee = ExtensionsService.EstInstallee(id);
        var active = ExtensionsService.EstActive(id);
        var enCours = ExtensionsService.EstEnCours(id);

        var racine = new Border
        {
            Height = 72,
            Background = Brushes.Transparent,
            Opacity = installee && !active ? 0.6 : 1
        };

        var grille = new Grid { Margin = new Thickness(15, 0, 10, 0) };
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icone = ExtensionUi.Icone(id, 42);
        icone.VerticalAlignment = VerticalAlignment.Top;
        icone.Margin = new Thickness(0, 10, 12, 0);
        Grid.SetColumn(icone, 0);
        grille.Children.Add(icone);

        var details = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        Grid.SetColumn(details, 1);

        // Ligne 1 : nom à gauche, durée d'activation (installée) ou téléchargements + note à droite
        var ligneNom = new DockPanel();
        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(meta, Dock.Right);

        if (installee)
        {
            var ms = ExtensionsService.TempsActivationMs(id);
            if (active && ms > 0)
            {
                meta.Children.Add(ExtensionUi.Pictogramme(ExtensionUi.DonneesHorloge, "ThemeTexteSecondaire", 14, 1.1));
                meta.Children.Add(ExtensionUi.Texte(ms.ToString(CultureInfo.InvariantCulture) + "ms", 12,
                    "ThemeTexteSecondaire", null, new Thickness(3, 0, 0, 0)));
            }
        }
        else
        {
            meta.Children.Add(ExtensionUi.Pictogramme(ExtensionUi.DonneesNuage, "ThemeTexteSecondaire", 14, 1.1));
            meta.Children.Add(ExtensionUi.Texte(FormaterNombre(info.Telechargements), 12,
                "ThemeTexteSecondaire", null, new Thickness(3, 0, 0, 0)));
            if (info.Note > 0)
            {
                var etoile = ExtensionUi.Etoile(13, 1);
                etoile.Margin = new Thickness(8, 0, 0, 0);
                etoile.VerticalAlignment = VerticalAlignment.Center;
                meta.Children.Add(etoile);
                meta.Children.Add(ExtensionUi.Texte(info.Note.ToString("0.#", CultureInfo.InvariantCulture), 12,
                    "ThemeTexteSecondaire", null, new Thickness(3, 0, 0, 0)));
            }
        }

        var nom = ExtensionUi.Texte(info.Nom, 13, "ThemeTexte", FontWeights.Bold);
        nom.TextTrimming = TextTrimming.CharacterEllipsis;
        ligneNom.Children.Add(meta);
        ligneNom.Children.Add(nom);
        Grid.SetRow(ligneNom, 0);

        // Ligne 2 : description
        var description = ExtensionUi.Texte(info.Description, 12.5, "ThemeTexteSecondaire");
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(description, 1);

        // Ligne 3 : éditeur à gauche, bouton Installer ou engrenage à droite
        var pied = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement action;
        if (installee)
        {
            var engrenage = new Button
            {
                Style = (Style)Application.Current.FindResource("BoutonIconeExt"),
                Width = 20,
                Height = 20,
                ToolTip = "Gérer",
                Content = new TextBlock
                {
                    Text = "\uE713",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13
                }
            };
            engrenage.Click += (_, _) => OuvrirMenuExtension(id, engrenage, PlacementMode.Bottom);
            action = engrenage;
        }
        else
        {
            var installer = new Button
            {
                Style = (Style)Application.Current.FindResource("BoutonInstallerExt"),
                Content = enCours ? "Installation…" : "Installer",
                IsEnabled = !enCours,
                VerticalAlignment = VerticalAlignment.Center
            };
            installer.Click += async (_, _) => await ExtensionsService.InstallerAsync(id);
            action = installer;
        }
        DockPanel.SetDock(action, Dock.Right);
        pied.Children.Add(action);
        pied.Children.Add(ExtensionUi.Texte(info.Editeur, 12, "ThemeTexteSecondaire", FontWeights.SemiBold));
        Grid.SetRow(pied, 2);

        details.Children.Add(ligneNom);
        details.Children.Add(description);
        details.Children.Add(pied);
        grille.Children.Add(details);
        racine.Child = grille;

        racine.MouseEnter += (_, _) => AppliquerFond(racine, id, true);
        racine.MouseLeave += (_, _) => AppliquerFond(racine, id, false);
        racine.MouseLeftButtonUp += (_, _) => Selectionner(id);
        racine.MouseRightButtonUp += (_, ev) =>
        {
            if (!installee) return;
            Selectionner(id);
            OuvrirMenuExtension(id, racine, PlacementMode.MousePoint);
            ev.Handled = true;
        };

        _lignes[id] = racine;
        AppliquerFond(racine, id, false);
        return racine;
    }

    private void Selectionner(string id)
    {
        _idSelection = id;
        MettreAJourFonds();
        AppEvents.DemanderExtension(id);
    }

    private void MettreAJourFonds()
    {
        foreach (var (id, bordure) in _lignes)
            AppliquerFond(bordure, id, bordure.IsMouseOver);
    }

    private void AppliquerFond(Border bordure, string id, bool survol)
    {
        if (string.Equals(id, _idSelection, StringComparison.OrdinalIgnoreCase))
            bordure.SetResourceReference(Border.BackgroundProperty, "ThemeListeSelection");
        else if (survol)
            bordure.SetResourceReference(Border.BackgroundProperty, "ThemeSurvol");
        else
            bordure.Background = Brushes.Transparent;
    }

        private void OuvrirMenuExtension(string id, UIElement cible, PlacementMode placement)
    {
        var active = ExtensionsService.EstActive(id);
        var menu = new ContextMenu { PlacementTarget = cible, Placement = placement };

        menu.Items.Add(ExtensionUi.ItemMenu(active ? "Désactiver" : "Activer", () =>
        {
            if (active) ExtensionsService.Desactiver(id);
            else ExtensionsService.Activer(id);
        }));
        menu.Items.Add(ExtensionUi.ItemMenu("Désinstaller", () => ExtensionsService.Desinstaller(id)));
        menu.Items.Add(new Separator());

        var maj = new MenuItem
        {
            Header = "Mise à jour automatique",
            IsCheckable = true,
            IsChecked = ExtensionsService.MiseAJourAuto(id)
        };
        maj.Click += (_, _) => ExtensionsService.DefinirMiseAJourAuto(id, maj.IsChecked);
        menu.Items.Add(maj);

        menu.Items.Add(new Separator());
        menu.Items.Add(ExtensionUi.ItemMenu("Copier l'identifiant de l'extension", () => ExtensionUi.CopierTexte(id)));
        menu.IsOpen = true;
    }

    private static string FormaterNombre(long n)
    {
        if (n >= 1_000_000) return (n / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (n >= 1_000) return Math.Round(n / 1_000.0).ToString("0", CultureInfo.InvariantCulture) + "K";
        return n.ToString(CultureInfo.InvariantCulture);
    }
}