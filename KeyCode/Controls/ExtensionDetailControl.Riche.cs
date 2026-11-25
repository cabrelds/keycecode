using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>Partie « fidélité VS Code » de la page de détail : boutons, onglets, texte riche, badges, pack.</summary>
public partial class ExtensionDetailControl
{
    private static readonly Regex _inline = new(
        @"\*\*(?<b>.+?)\*\*|`(?<c>[^`]+)`|\[(?<t>[^\]]+)\]\((?<u>[^)]+)\)", RegexOptions.Compiled);

    private static readonly Regex _numerotee = new(@"^(\d+)\.\s+(.*)$", RegexOptions.Compiled);

    private static string LibelleOnglet(string cle) => cle switch
    {
        "fonctionnalites" => "FONCTIONNALITÉS",
        "journal" => "JOURNAL DES MODIFICATIONS",
        "dependances" => "DÉPENDANCES",
        "pack" => "PACK D'EXTENSIONS",
        _ => "DÉTAILS"
    };

    // ------------------------------------------------------------------
    // Boutons de l'en-tête
    // ------------------------------------------------------------------

    private void ConstruireActionsRiche(ExtensionInfo info)
    {
        ZoneActions.Children.Clear();
        var id = info.Id;
        var installee = ExtensionsService.EstInstallee(id);

        if (!installee)
        {
            ZoneActions.Children.Add(CreerBoutonInstaller(info));
        }
        else
        {
            var active = ExtensionsService.EstActive(id);

            if (info.Preliminaire)
            {
                ZoneActions.Children.Add(CreerBoutonBleu("Passer à la version préliminaire", () =>
                    MessageBox.Show("Les versions préliminaires ne sont pas disponibles dans KeyCode.", "KeyCode")));
            }

            if (info.Integree)
            {
                ZoneActions.Children.Add(CreerBoutonSimple(
                    active ? "Désactiver les fonctionnalités IA" : "Activer les fonctionnalités IA", () =>
                    {
                        if (active) ExtensionsService.Desactiver(id);
                        else ExtensionsService.Activer(id);
                    }));
            }
            else
            {
                ZoneActions.Children.Add(CreerBoutonSimple(active ? "Désactiver" : "Activer", () =>
                {
                    if (active) ExtensionsService.Desactiver(id);
                    else ExtensionsService.Activer(id);
                }));
                ZoneActions.Children.Add(CreerBoutonScinde("Désinstaller", () => ExtensionsService.Desinstaller(id)));
            }
        }

        if (!info.Integree)
        {
            var maj = new CheckBox
            {
                Style = StyleApp("CaseExt"),
                Content = "Mise à jour automatique",
                IsChecked = ExtensionsService.MiseAJourAuto(id),
                IsEnabled = installee,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            maj.Click += (_, _) => ExtensionsService.DefinirMiseAJourAuto(id, maj.IsChecked == true);
            ZoneActions.Children.Add(maj);
        }

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

    private FrameworkElement CreerBoutonInstaller(ExtensionInfo info)
    {
        var id = info.Id;
        var enCours = ExtensionsService.EstEnCours(id);
        var texte = enCours ? "Installation…" : "Installer";

        if (!info.MenuInstaller)
        {
            var simple = new Button
            {
                Style = StyleApp("BoutonInstallerExt"),
                Content = texte,
                IsEnabled = !enCours,
                Height = 26,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 13,
                Margin = new Thickness(0, 0, 14, 8)
            };
            simple.Click += async (_, _) => await ExtensionsService.InstallerAsync(id);
            return simple;
        }

        // « Installer ▾ » : bouton scindé bleu
        var bordure = new Border
        {
            Height = 26,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 14, 8),
            IsEnabled = !enCours
        };
        ExtensionUi.Ref(bordure, Border.BackgroundProperty, "ThemeBoutonExt");

        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var principal = new Button
        {
            Style = StyleApp("BoutonInstallerExt"),
            Background = Brushes.Transparent,
            Content = texte,
            Height = 26,
            Padding = new Thickness(12, 0, 10, 0),
            FontSize = 13
        };
        principal.Click += async (_, _) => await ExtensionsService.InstallerAsync(id);

        var separateur = new Border { Margin = new Thickness(0, 5, 0, 5), Background = ExtensionUi.Couleur("#66FFFFFF") };
        Grid.SetColumn(separateur, 1);

        var fleche = new Button
        {
            Style = StyleApp("BoutonInstallerExt"),
            Background = Brushes.Transparent,
            Height = 26,
            Padding = new Thickness(6, 0, 6, 0),
            Content = new TextBlock
            {
                Text = "\uE70D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9,
                Foreground = Brushes.White
            }
        };
        fleche.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = bordure, Placement = PlacementMode.Bottom };
            menu.Items.Add(ExtensionUi.ItemMenu("Installer", () => { _ = ExtensionsService.InstallerAsync(id); }));
            menu.Items.Add(ExtensionUi.ItemMenu("Installer la version préliminaire", () => { _ = ExtensionsService.InstallerAsync(id); }));
            menu.IsOpen = true;
        };
        Grid.SetColumn(fleche, 2);

        grille.Children.Add(principal);
        grille.Children.Add(separateur);
        grille.Children.Add(fleche);
        bordure.Child = grille;
        return bordure;
    }

    private static FrameworkElement CreerBoutonBleu(string texte, Action action)
    {
        var bouton = new Button
        {
            Style = StyleApp("BoutonInstallerExt"),
            Content = texte,
            Height = 26,
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 8, 8)
        };
        bouton.Click += (_, _) => action();
        return bouton;
    }

    private static FrameworkElement CreerBoutonSimple(string texte, Action action)
    {
        var bordure = new Border
        {
            Height = 26,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 8)
        };
        ExtensionUi.Ref(bordure, Border.BorderBrushProperty, "ThemeBordure");

        var bouton = new Button { Style = StyleApp("BoutonPlatExt"), Content = texte };
        bouton.Click += (_, _) => action();
        bordure.Child = bouton;
        return bordure;
    }

    // ------------------------------------------------------------------
    // Éditeur vérifié, lien texte
    // ------------------------------------------------------------------

    private static UIElement LienTexte(string texte, string url, double taille, Thickness marge)
    {
        var t = ExtensionUi.Texte(texte, taille, "ThemeLien", null, marge);
        t.Cursor = Cursors.Hand;
        t.MouseEnter += (_, _) => t.TextDecorations = TextDecorations.Underline;
        t.MouseLeave += (_, _) => t.TextDecorations = null;
        t.MouseLeftButtonUp += (_, _) => ExtensionUi.OuvrirUrl(url);
        return t;
    }

    // ------------------------------------------------------------------
    // Contenu de l'onglet DÉTAILS (texte riche)
    // ------------------------------------------------------------------

    /// <summary>
    /// Préfixes : "# " titre, "## " sous-titre, "### " petit titre, "- " puce, "1. " liste numérotée,
    /// "> " code, "~ " ligne centrée en italique, "[sep]" filet, "[badge] gauche|droite|#couleur;..." badges.
    /// Dans le texte : **gras**, `code`, [libellé](adresse).
    /// </summary>
    private void ConstruireDetailsRiche(ExtensionInfo info)
    {
        if (info.PackEnDetails && info.Pack.Length > 0)
            Contenu.Children.Add(BlocPack(info));

        var code = new List<string>();

        void Vider()
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

            Vider();

            if (ligne == "[sep]")
            {
                var filet = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 14) };
                Contenu.Children.Add(ExtensionUi.Ref(filet, Border.BackgroundProperty, "ThemeBordure"));
            }
            else if (ligne.StartsWith("[badge] ", StringComparison.Ordinal)) Contenu.Children.Add(RangeeBadges(ligne[8..]));
            else if (ligne.StartsWith("~ ", StringComparison.Ordinal))
            {
                var centre = Riche(ligne[2..], 14, italique: true);
                centre.TextAlignment = TextAlignment.Center;
                centre.Margin = new Thickness(0, 0, 0, 4);
                Contenu.Children.Add(centre);
            }
            else if (ligne.StartsWith("### ", StringComparison.Ordinal))
                Contenu.Children.Add(ExtensionUi.Texte(ligne[4..], 16, "ThemeTexte", null, new Thickness(0, 14, 0, 8)));
            else if (ligne.StartsWith("## ", StringComparison.Ordinal)) Contenu.Children.Add(Titre2(ligne[3..]));
            else if (ligne.StartsWith("# ", StringComparison.Ordinal)) Contenu.Children.Add(Titre1(ligne[2..]));
            else if (ligne.StartsWith("- ", StringComparison.Ordinal)) Contenu.Children.Add(PuceRiche(ligne[2..], "•"));
            else if (_numerotee.Match(ligne) is { Success: true } m)
                Contenu.Children.Add(PuceRiche(m.Groups[2].Value, m.Groups[1].Value + "."));
            else
            {
                var p = Riche(ligne, 14);
                p.Margin = new Thickness(0, 0, 0, 12);
                Contenu.Children.Add(p);
            }
        }
        Vider();
    }

    private static TextBlock Riche(string texte, double taille, bool italique = false)
    {
        var tb = ExtensionUi.Texte("", taille, "ThemeTexte");
        tb.TextWrapping = TextWrapping.Wrap;
        tb.LineHeight = 22;
        tb.VerticalAlignment = VerticalAlignment.Top;
        if (italique) tb.FontStyle = FontStyles.Italic;

        var position = 0;
        foreach (Match m in _inline.Matches(texte))
        {
            if (m.Index > position) tb.Inlines.Add(new Run(texte[position..m.Index]));

            if (m.Groups["b"].Success)
            {
                tb.Inlines.Add(new Run(m.Groups["b"].Value) { FontWeight = FontWeights.Bold });
            }
            else if (m.Groups["c"].Success)
            {
                var run = new Run(m.Groups["c"].Value) { FontFamily = new FontFamily("Consolas"), FontSize = taille - 1 };
                run.SetResourceReference(TextElement.BackgroundProperty, "ThemeFondTertiaire");
                tb.Inlines.Add(run);
            }
            else
            {
                var url = m.Groups["u"].Value;
                var lien = new Hyperlink(new Run(m.Groups["t"].Value)) { TextDecorations = null };
                lien.SetResourceReference(TextElement.ForegroundProperty, "ThemeLien");
                lien.Click += (_, _) => ExtensionUi.OuvrirUrl(url);
                tb.Inlines.Add(lien);
            }
            position = m.Index + m.Length;
        }
        if (position < texte.Length) tb.Inlines.Add(new Run(texte[position..]));
        return tb;
    }

    private static UIElement PuceRiche(string texte, string marqueur)
    {
        var grille = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var puce = ExtensionUi.Texte(marqueur, 14, "ThemeTexte", null, new Thickness(28, 0, 10, 0));
        puce.VerticalAlignment = VerticalAlignment.Top;

        var contenu = Riche(texte, 14);
        Grid.SetColumn(contenu, 1);

        grille.Children.Add(puce);
        grille.Children.Add(contenu);
        return grille;
    }

    private static UIElement RangeeBadges(string specification)
    {
        var rangee = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var partie in specification.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = partie.Split('|');
            var gauche = p[0].Trim();
            var droite = p.Length > 1 ? p[1].Trim() : "";
            var couleur = p.Length > 2 ? p[2].Trim() : "#9F9F9F";

            var contenu = new StackPanel { Orientation = Orientation.Horizontal };
            contenu.Children.Add(new Border
            {
                Background = ExtensionUi.Couleur("#555555"),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock { Text = gauche, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }
            });
            if (droite.Length > 0)
            {
                contenu.Children.Add(new Border
                {
                    Background = ExtensionUi.Couleur(couleur),
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = new TextBlock { Text = droite, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }
                });
            }

            rangee.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 4, 4),
                Child = contenu
            });
        }
        return rangee;
    }

    // ------------------------------------------------------------------
    // Pack d'extensions (Jupyter, Python, C# Dev Kit) et dépendances
    // ------------------------------------------------------------------

    private static UIElement BlocPack(ExtensionInfo info)
    {
        var pile = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        if (info.Pack.Length == 0)
        {
            pile.Children.Add(ExtensionUi.Texte("Aucune extension.", 14, "ThemeTexteSecondaire"));
            return pile;
        }

        var entete = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Child = ExtensionUi.Texte($"Pack d'extensions ({info.Pack.Length})", 16, "ThemeTexte", FontWeights.SemiBold)
        };
        pile.Children.Add(ExtensionUi.Ref(entete, Border.BorderBrushProperty, "ThemeBordure"));

        foreach (var entree in info.Pack)
        {
            var p = entree.Split('|');
            var ligne = new Grid { Margin = new Thickness(24, 10, 10, 10) };
            ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icone = ExtensionUi.Icone(p[0], 42);
            icone.VerticalAlignment = VerticalAlignment.Top;
            icone.Margin = new Thickness(0, 0, 14, 0);
            ligne.Children.Add(icone);

            var texte = new StackPanel();
            texte.Children.Add(ExtensionUi.Texte(p[0], 14, "ThemeTexte", FontWeights.SemiBold));
            var description = ExtensionUi.Texte(p.Length > 1 ? p[1] : "", 13, "ThemeTexteSecondaire");
            description.TextTrimming = TextTrimming.CharacterEllipsis;
            texte.Children.Add(description);

            var editeur = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            editeur.Children.Add(ExtensionUi.Verifie(14));
            editeur.Children.Add(ExtensionUi.Texte(p.Length > 2 ? p[2] : "", 12, "ThemeTexte", FontWeights.SemiBold,
                new Thickness(5, 0, 0, 0)));
            texte.Children.Add(editeur);
            Grid.SetColumn(texte, 1);
            ligne.Children.Add(texte);

            var installer = new Button
            {
                Style = StyleApp("BoutonInstallerExt"),
                Content = "Installer",
                VerticalAlignment = VerticalAlignment.Bottom,
                IsEnabled = false
            };
            Grid.SetColumn(installer, 2);
            ligne.Children.Add(installer);

            pile.Children.Add(ligne);
        }
        return pile;
    }
}