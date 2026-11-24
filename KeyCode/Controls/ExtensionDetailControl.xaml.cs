using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>
/// Page de détail d'une extension (style onglet « Extension : Nom » de VS Code) :
/// en-tête avec boutons Installer / Désactiver / Désinstaller, onglets DÉTAILS / FONCTIONNALITÉS /
/// JOURNAL DES MODIFICATIONS et colonne d'informations (Installation, Marketplace, Catégories, Ressources).
/// </summary>
public partial class ExtensionDetailControl : UserControl
{
    private string _id = "";
    private string _onglet = "details";

    /// <summary>Le bouton ✕ de l'onglet a été cliqué.</summary>
    public event Action? DemandeFermeture;

    public ExtensionDetailControl()
    {
        InitializeComponent();

        IconeOnglet.Content = ExtensionUi.Pictogramme(ExtensionUi.DonneesExtensions, "ThemeTexte", 15, 1.5, false, 24);
        BoutonFermerOnglet.Content = ExtensionUi.Pictogramme(ExtensionUi.DonneesFermer, "ThemeTexte", 14, 1.3);

        AppEvents.ExtensionsChangees += SurExtensionsChangees;
        Unloaded += (_, _) => AppEvents.ExtensionsChangees -= SurExtensionsChangees;
    }

    // ------------------------------------------------------------------
    // API publique
    // ------------------------------------------------------------------

    public void Afficher(string id)
    {
        var info = ExtensionsService.Obtenir(id);
        if (info == null) return;

        _id = id;
        _onglet = "details";

        TitreOnglet.Text = "Extension : " + info.Nom;
        NomExtension.Text = info.Nom;
        DescriptionExtension.Text = info.Description;
        IconeGrande.Content = ExtensionUi.Icone(id, 128);

        ConstruireMeta(info);
        ConstruireNavigation(info);
        RafraichirEtat(info);
        ConstruireContenu(info);

        DefileurContenu.ScrollToTop();
        DefileurLateral.ScrollToTop();
    }

    private void FermerOnglet_Click(object sender, RoutedEventArgs e) => DemandeFermeture?.Invoke();

    private void SurExtensionsChangees() =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible) return;
            var info = ExtensionsService.Obtenir(_id);
            if (info != null) RafraichirEtat(info);
        });

    /// <summary>Reconstruit les parties qui dépendent de l'état (boutons et colonne d'informations).</summary>
    private void RafraichirEtat(ExtensionInfo info)
    {
        ConstruireActions(info);
        ConstruireLateral(info);
    }

    private static Style StyleApp(string cle) => (Style)Application.Current.FindResource(cle);

    private static string UrlMarketplace(string id) =>
        "https://marketplace.visualstudio.com/items?itemName=" + id;

    // ------------------------------------------------------------------
    // En-tête
    // ------------------------------------------------------------------

    private static Border Separateur()
    {
        var barre = new Border
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        return ExtensionUi.Ref(barre, Border.BackgroundProperty, "ThemeBordure");
    }

    private void ConstruireMeta(ExtensionInfo info)
    {
        LigneMeta.Children.Clear();

        LigneMeta.Children.Add(ExtensionUi.Texte(info.Editeur, 15, "ThemeTexte"));
        LigneMeta.Children.Add(Separateur());

        var nuage = ExtensionUi.Pictogramme(ExtensionUi.DonneesNuage, "ThemeTexteSecondaire", 17, 1.1);
        nuage.VerticalAlignment = VerticalAlignment.Center;
        LigneMeta.Children.Add(nuage);
        LigneMeta.Children.Add(ExtensionUi.Texte(
            info.Telechargements.ToString("N0", new CultureInfo("fr-FR")), 14, "ThemeTexte",
            null, new Thickness(5, 0, 0, 0)));

        if (info.NombreAvis > 0)
        {
            LigneMeta.Children.Add(Separateur());
            for (var i = 1; i <= 5; i++)
            {
                var fraction = Math.Round(Math.Clamp(info.Note - (i - 1), 0, 1) * 2) / 2;
                var etoile = ExtensionUi.Etoile(15, fraction);
                etoile.VerticalAlignment = VerticalAlignment.Center;
                LigneMeta.Children.Add(etoile);
            }
            LigneMeta.Children.Add(ExtensionUi.Texte($"({info.NombreAvis})", 14, "ThemeTexte",
                null, new Thickness(4, 0, 0, 0)));
        }

        if (info.Commanditaires > 0)
        {
            LigneMeta.Children.Add(Separateur());
            var coeur = ExtensionUi.PictogrammeFixe(ExtensionUi.DonneesCoeur, ExtensionUi.Couleur("#D86AA3"), 17);
            coeur.VerticalAlignment = VerticalAlignment.Center;
            LigneMeta.Children.Add(coeur);
            var texte = info.Commanditaires + ". COMMANDITAIRE" + (info.Commanditaires > 1 ? "S" : "");
            LigneMeta.Children.Add(ExtensionUi.Texte(texte, 14, "ThemeTexte", null, new Thickness(6, 0, 0, 0)));
        }
    }

    private void ConstruireActions(ExtensionInfo info)
    {
        ZoneActions.Children.Clear();
        var id = info.Id;
        var installee = ExtensionsService.EstInstallee(id);

        if (!installee)
        {
            var enCours = ExtensionsService.EstEnCours(id);
            var installer = new Button
            {
                Style = StyleApp("BoutonInstallerExt"),
                Content = enCours ? "Installation…" : "Installer",
                IsEnabled = !enCours,
                Height = 26,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 13,
                Margin = new Thickness(0, 0, 14, 0)
            };
            installer.Click += async (_, _) => await ExtensionsService.InstallerAsync(id);
            ZoneActions.Children.Add(installer);
        }
        else
        {
            var active = ExtensionsService.EstActive(id);
            ZoneActions.Children.Add(CreerBoutonScinde(active ? "Désactiver" : "Activer", () =>
            {
                if (active) ExtensionsService.Desactiver(id);
                else ExtensionsService.Activer(id);
            }));
            ZoneActions.Children.Add(CreerBoutonScinde("Désinstaller", () => ExtensionsService.Desinstaller(id)));
        }

        var maj = new CheckBox
        {
            Style = StyleApp("CaseExt"),
            Content = "Mise à jour automatique",
            IsChecked = ExtensionsService.MiseAJourAuto(id),
            IsEnabled = installee,
            Margin = new Thickness(6, 0, 0, 0)
        };
        maj.Click += (_, _) => ExtensionsService.DefinirMiseAJourAuto(id, maj.IsChecked == true);
        ZoneActions.Children.Add(maj);

        var engrenage = new Button
        {
            Style = StyleApp("BoutonIconeExt"),
            Width = 26,
            Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Gérer",
            Content = new TextBlock
            {
                Text = "\uE713",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 15
            }
        };
        engrenage.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = engrenage, Placement = PlacementMode.Bottom };
            menu.Items.Add(ExtensionUi.ItemMenu("Copier l'identifiant de l'extension", () => ExtensionUi.CopierTexte(id)));
            menu.Items.Add(ExtensionUi.ItemMenu("Copier le lien de l'extension", () => ExtensionUi.CopierTexte(UrlMarketplace(id))));
            menu.IsOpen = true;
        };
        ZoneActions.Children.Add(engrenage);
    }

    /// <summary>Bouton « Désactiver ▾ » : action principale + flèche ouvrant un menu à une entrée.</summary>
    private FrameworkElement CreerBoutonScinde(string texte, Action action)
    {
        var bordure = new Border
        {
            Height = 26,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        ExtensionUi.Ref(bordure, Border.BackgroundProperty, "ThemeFondTertiaire");
        ExtensionUi.Ref(bordure, Border.BorderBrushProperty, "ThemeBordure");

        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var principal = new Button { Style = StyleApp("BoutonPlatExt"), Content = texte };
        principal.Click += (_, _) => action();

        var separateur = new Border { Margin = new Thickness(0, 5, 0, 5) };
        ExtensionUi.Ref(separateur, Border.BackgroundProperty, "ThemeBordure");
        Grid.SetColumn(separateur, 1);

        var fleche = new Button
        {
            Style = StyleApp("BoutonPlatExt"),
            Padding = new Thickness(6, 0, 6, 0),
            Content = ExtensionUi.Pictogramme(ExtensionUi.DonneesChevronMenu, "ThemeTexte", 12, 1.3)
        };
        fleche.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = bordure, Placement = PlacementMode.Bottom };
            menu.Items.Add(ExtensionUi.ItemMenu(texte, action));
            menu.IsOpen = true;
        };
        Grid.SetColumn(fleche, 2);

        grille.Children.Add(principal);
        grille.Children.Add(separateur);
        grille.Children.Add(fleche);
        bordure.Child = grille;
        return bordure;
    }

    // ------------------------------------------------------------------
    // Navigation et contenu
    // ------------------------------------------------------------------

    private void ConstruireNavigation(ExtensionInfo info)
    {
        Navigation.Children.Clear();
        AjouterOnglet(info, "DÉTAILS", "details");
        AjouterOnglet(info, "FONCTIONNALITÉS", "fonctionnalites");
        AjouterOnglet(info, "JOURNAL DES MODIFICATIONS", "journal");
    }

    private void AjouterOnglet(ExtensionInfo info, string titre, string cle)
    {
        var actif = _onglet == cle;
        var texte = ExtensionUi.Texte(titre, 12, actif ? "ThemeTexte" : "ThemeTexteSecondaire");
        var bordure = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 4, -1),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = texte
        };

        if (actif)
        {
            ExtensionUi.Ref(bordure, Border.BorderBrushProperty, "ThemeTexte");
        }
        else
        {
            bordure.BorderBrush = Brushes.Transparent;
            bordure.MouseEnter += (_, _) => texte.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTexte");
            bordure.MouseLeave += (_, _) => texte.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTexteSecondaire");
        }

        bordure.MouseLeftButtonUp += (_, _) =>
        {
            if (_onglet == cle) return;
            _onglet = cle;
            ConstruireNavigation(info);
            ConstruireContenu(info);
            DefileurContenu.ScrollToTop();
        };
        Navigation.Children.Add(bordure);
    }

    private void ConstruireContenu(ExtensionInfo info)
    {
        Contenu.Children.Clear();
        switch (_onglet)
        {
            case "fonctionnalites": ConstruireFonctionnalites(info); break;
            case "journal": ConstruireJournal(info); break;
            default: ConstruireDetails(info); break;
        }
    }

    private void ConstruireDetails(ExtensionInfo info)
    {
        var code = new List<string>();

        void VidangerCode()
        {
            if (code.Count == 0) return;
            Contenu.Children.Add(BlocCode(string.Join("\n", code)));
            code.Clear();
        }

        foreach (var ligne in info.Details)
        {
            if (ligne.StartsWith("> ", StringComparison.Ordinal))
            {
                code.Add(ligne[2..]);
                continue;
            }

            VidangerCode();
            if (ligne.StartsWith("## ", StringComparison.Ordinal)) Contenu.Children.Add(Titre2(ligne[3..]));
            else if (ligne.StartsWith("# ", StringComparison.Ordinal)) Contenu.Children.Add(Titre1(ligne[2..]));
            else if (ligne.StartsWith("- ", StringComparison.Ordinal)) Contenu.Children.Add(Puce(ligne[2..]));
            else Contenu.Children.Add(Paragraphe(ligne));
        }
        VidangerCode();
    }

    private void ConstruireFonctionnalites(ExtensionInfo info)
    {
        Contenu.Children.Add(Titre2("Fonctionnalités", 0));
        foreach (var f in info.Fonctionnalites)
            Contenu.Children.Add(Puce(f));

        if (info.Commandes.Length == 0) return;
        Contenu.Children.Add(Titre2($"Commandes ({info.Commandes.Length})"));
        Contenu.Children.Add(TableauCommandes(info.Commandes));
    }

    private void ConstruireJournal(ExtensionInfo info)
    {
        foreach (var entree in info.Journal)
        {
            var parties = entree.Split('|', 2);
            Contenu.Children.Add(Titre2(parties[0], Contenu.Children.Count == 0 ? 0 : 22));
            if (parties.Length > 1) Contenu.Children.Add(Puce(parties[1]));
        }
    }

    private static UIElement Titre1(string texte)
    {
        var bordure = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 6),
            Margin = new Thickness(0, 0, 0, 14),
            Child = ExtensionUi.Texte(texte, 26, "ThemeTexte")
        };
        return ExtensionUi.Ref(bordure, Border.BorderBrushProperty, "ThemeBordure");
    }

    private static UIElement Titre2(string texte, double margeHaut = 22) =>
        ExtensionUi.Texte(texte, 20, "ThemeTexte", null, new Thickness(0, margeHaut, 0, 10));

    private static UIElement Puce(string texte)
    {
        var grille = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var puce = ExtensionUi.Texte("•", 14, "ThemeTexte", null, new Thickness(28, 0, 10, 0));
        puce.VerticalAlignment = VerticalAlignment.Top;

        var contenu = ExtensionUi.Texte(texte, 14, "ThemeTexte");
        contenu.TextWrapping = TextWrapping.Wrap;
        contenu.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(contenu, 1);

        grille.Children.Add(puce);
        grille.Children.Add(contenu);
        return grille;
    }

    private static UIElement Paragraphe(string texte)
    {
        var t = ExtensionUi.Texte(texte, 14, "ThemeTexte", null, new Thickness(0, 0, 0, 12));
        t.TextWrapping = TextWrapping.Wrap;
        t.LineHeight = 22;
        t.VerticalAlignment = VerticalAlignment.Top;
        return t;
    }

    private static UIElement BlocCode(string texte)
    {
        var code = ExtensionUi.Texte(texte, 13, "ThemeTexte");
        code.FontFamily = new FontFamily("Consolas");
        code.TextWrapping = TextWrapping.Wrap;

        var bordure = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(3),
            Child = code
        };
        return ExtensionUi.Ref(bordure, Border.BackgroundProperty, "ThemeFondSecondaire");
    }

    private static UIElement TableauCommandes(string[] commandes)
    {
        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lignes = new List<string[]> { new[] { "Commande", "Description", "Raccourci" } };
        foreach (var c in commandes)
        {
            var p = c.Split('|');
            lignes.Add(new[] { p[0], p.Length > 1 ? p[1] : "", p.Length > 2 ? p[2] : "" });
        }

        for (var i = 0; i < lignes.Count; i++)
        {
            grille.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var j = 0; j < 3; j++)
            {
                var texte = ExtensionUi.Texte(lignes[i][j], 13, "ThemeTexte", i == 0 ? FontWeights.Bold : null);
                texte.TextWrapping = TextWrapping.Wrap;

                var cellule = new Border
                {
                    Padding = new Thickness(8, 7, 8, 7),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = texte
                };
                ExtensionUi.Ref(cellule, Border.BorderBrushProperty, i == 0 ? "ThemeTexteSecondaire" : "ThemeBordure");
                Grid.SetRow(cellule, i);
                Grid.SetColumn(cellule, j);
                grille.Children.Add(cellule);
            }
        }
        return grille;
    }

    // ------------------------------------------------------------------
    // Colonne d'informations (droite)
    // ------------------------------------------------------------------

    private void ConstruireLateral(ExtensionInfo info)
    {
        Lateral.Children.Clear();
        var installee = ExtensionsService.EstInstallee(info.Id);
        var premiere = true;

        if (installee)
        {
            Lateral.Children.Add(SectionTitre("Installation", premiere));
            premiere = false;
            Lateral.Children.Add(Tableau(
                ("Identificateur", info.Id, true, false),
                ("Version", info.Version, true, false),
                ("Dernière mise à jour", info.DerniereMiseAJour, false, false),
                ("Taille", info.Taille, false, true)));
        }

        Lateral.Children.Add(SectionTitre("Marketplace", premiere));
        if (installee)
        {
            Lateral.Children.Add(Tableau(
                ("Publié", info.Publie, false, false),
                ("Dernière publication", info.DernierePublication, false, false)));
        }
        else
        {
            Lateral.Children.Add(Tableau(
                ("Identificateur", info.Id, true, false),
                ("Version", info.Version, true, false),
                ("Publié", info.Publie, false, false),
                ("Dernière publication", info.DernierePublication, false, false)));
        }

        Lateral.Children.Add(SectionTitre("Catégories", false));
        var categories = new WrapPanel();
        categories.Children.Add(Pastille(info.Categorie));
        Lateral.Children.Add(categories);

        Lateral.Children.Add(SectionTitre("Ressources", false));
        if (!string.IsNullOrWhiteSpace(info.Depot))
        {
            Lateral.Children.Add(Lien("Dépôt", ExtensionUi.DonneesDepot, info.Depot));
            Lateral.Children.Add(Lien("Problèmes", ExtensionUi.DonneesProblemes, info.Depot + "/issues"));
            Lateral.Children.Add(Lien("Licence", ExtensionUi.DonneesExterne, info.Depot + "/blob/HEAD/LICENSE"));
        }
        Lateral.Children.Add(Lien(info.Editeur, ExtensionUi.DonneesExterne, UrlMarketplace(info.Id)));
        Lateral.Children.Add(Lien("Marketplace", ExtensionUi.DonneesExterne, UrlMarketplace(info.Id)));
    }

    private static UIElement SectionTitre(string titre, bool premiere)
    {
        var pile = new StackPanel();
        if (!premiere)
        {
            var ligne = new Border { Height = 1, Margin = new Thickness(0, 18, 0, 14) };
            ExtensionUi.Ref(ligne, Border.BackgroundProperty, "ThemeBordure");
            pile.Children.Add(ligne);
        }
        pile.Children.Add(ExtensionUi.Texte(titre, 17, "ThemeTexte", null, new Thickness(0, 0, 0, 8)));
        return pile;
    }

    private static UIElement Tableau(params (string Libelle, string Valeur, bool Mono, bool Lien)[] lignes)
    {
        var pile = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < lignes.Length; i++)
        {
            var (libelle, valeur, mono, lien) = lignes[i];

            var ligne = new Border { Padding = new Thickness(6, 4, 6, 4) };
            if (i % 2 == 0) ExtensionUi.Ref(ligne, Border.BackgroundProperty, "ThemeFondSecondaire");

            var grille = new Grid();
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var etiquette = ExtensionUi.Texte(libelle, 13, "ThemeTexte");
            etiquette.TextWrapping = TextWrapping.Wrap;
            etiquette.VerticalAlignment = VerticalAlignment.Top;

            var contenu = ExtensionUi.Texte(valeur, mono ? 11.5 : 12.5, lien ? "ThemeLien" : "ThemeTexte");
            if (mono) contenu.FontFamily = new FontFamily("Consolas");
            contenu.TextWrapping = TextWrapping.Wrap;
            contenu.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(contenu, 1);

            grille.Children.Add(etiquette);
            grille.Children.Add(contenu);
            ligne.Child = grille;
            pile.Children.Add(ligne);
        }
        return pile;
    }

    private static UIElement Pastille(string texte)
    {
        var bordure = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 6),
            Child = ExtensionUi.Texte(texte, 12.5, "ThemeTexte")
        };
        return ExtensionUi.Ref(bordure, Border.BorderBrushProperty, "ThemeBordure");
    }

    private static UIElement Lien(string texte, string icone, string url)
    {
        var ligne = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 3, 0, 3),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent
        };

        var pictogramme = ExtensionUi.Pictogramme(icone, "ThemeTexte", 15, 1.1);
        pictogramme.VerticalAlignment = VerticalAlignment.Center;
        ligne.Children.Add(pictogramme);

        var libelle = ExtensionUi.Texte(texte, 13, "ThemeLien", null, new Thickness(8, 0, 0, 0));
        ligne.Children.Add(libelle);

        ligne.MouseEnter += (_, _) => libelle.TextDecorations = TextDecorations.Underline;
        ligne.MouseLeave += (_, _) => libelle.TextDecorations = null;
        ligne.MouseLeftButtonUp += (_, _) => ExtensionUi.OuvrirUrl(url);
        return ligne;
    }
}