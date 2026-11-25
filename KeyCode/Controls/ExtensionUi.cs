using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KeyCode.Services;
using Chemin = System.Windows.Shapes.Path;

namespace KeyCode.Controls;

/// <summary>
/// Aides visuelles du panneau Extensions : icônes vectorielles des extensions, pictogrammes,
/// étoiles, menus. Les couleurs passent par SetResourceReference pour suivre les changements de thème.
/// </summary>
public static class ExtensionUi
{
    // Pictogrammes (zone 16x16 sauf mention contraire)
    public const string DonneesActualiser = "M13,8 A5,5 0 1 1 11.2,4.2 M11.6,1.8 V4.6 H8.8";
    public const string DonneesPlus = "M2.5,8 a1,1 0 1 0 2,0 a1,1 0 1 0 -2,0 M7,8 a1,1 0 1 0 2,0 a1,1 0 1 0 -2,0 M11.5,8 a1,1 0 1 0 2,0 a1,1 0 1 0 -2,0";
    public const string DonneesEffacer = "M2,3.5 H11 M2,7.5 H8 M2,11.5 H11 M10,8.5 L14,12.5 M14,8.5 L10,12.5";
    public const string DonneesFiltre = "M2,3 H14 L9.6,8.4 V13 L6.4,11.4 V8.4 Z";
    public const string DonneesChevronDroite = "M5,3 L10,8 L5,13";
    public const string DonneesChevronBas = "M3,5 L8,10 L13,5";
    public const string DonneesNuage = "M4,12.2 A3,3 0 0 1 3.7,6.2 A4.2,4.2 0 0 1 11.7,5.6 A3.3,3.3 0 0 1 12,12.2 Z M8,6.8 V11 M6.2,9.3 L8,11.1 L9.8,9.3";
    public const string DonneesHorloge = "M8,2 A6,6 0 1 0 8,14 A6,6 0 1 0 8,2 Z M8,4.8 V8.2 L10.4,9.6";
    public const string DonneesCoeur = "M8,13.6 C3.2,10.2 1.9,8 1.9,5.7 A3.1,3.1 0 0 1 8,4.5 A3.1,3.1 0 0 1 14.1,5.7 C14.1,8 12.8,10.2 8,13.6 Z";
    public const string DonneesFermer = "M4,4 L12,12 M12,4 L4,12";
    public const string DonneesDepot = "M3.5,2.5 H12.5 V13.5 H5 A1.5,1.5 0 0 1 3.5,12 Z M3.5,12 A1.5,1.5 0 0 1 5,10.5 H12.5";
    public const string DonneesProblemes = "M8,2 A6,6 0 1 0 8,14 A6,6 0 1 0 8,2 Z M8,7 H8.01";
    public const string DonneesExterne = "M7,3 H3.5 V12.5 H13 V9 M9,2.5 H13.5 V7 M13.5,2.5 L7.5,8.5";
    public const string DonneesChevronMenu = "M3,6 L8,11 L13,6";

    /// <summary>Icône « extensions » (4 carrés dont un décalé) : zone 24x24.</summary>
    public const string DonneesExtensions = "M3,13.5 H10 V20.5 H3 Z M12,13.5 H19 V20.5 H12 Z M3,4.5 H10 V11.5 H3 Z M13.5,2.5 H20.5 V9.5 H13.5 Z";

    private static Brush Hex(string hex)
    {
        var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        pinceau.Freeze();
        return pinceau;
    }

    /// <summary>Lie une propriété à une ressource de thème (suit les changements de thème).</summary>
    public static T Ref<T>(T element, DependencyProperty propriete, string cle) where T : FrameworkElement
    {
        element.SetResourceReference(propriete, cle);
        return element;
    }

    private static Chemin Trace(string donnees, Brush? remplissage, Brush? trait, double epaisseur) => new()
    {
        Data = Geometry.Parse(donnees),
        Fill = remplissage,
        Stroke = trait,
        StrokeThickness = epaisseur,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
    };

    public static FrameworkElement Pictogramme(string donnees, string cleCouleur, double taille = 16,
        double epaisseur = 1.2, bool rempli = false, double zone = 16)
    {
        var contenu = new Grid { Width = zone, Height = zone };
        var forme = Trace(donnees, null, null, rempli ? 0 : epaisseur);
        Ref(forme, rempli ? Shape.FillProperty : Shape.StrokeProperty, cleCouleur);
        contenu.Children.Add(forme);
        return new Viewbox { Width = taille, Height = taille, Child = contenu };
    }

    public static FrameworkElement PictogrammeFixe(string donnees, Brush couleur, double taille = 16)
    {
        var contenu = new Grid { Width = 16, Height = 16 };
        contenu.Children.Add(Trace(donnees, couleur, null, 0));
        return new Viewbox { Width = taille, Height = taille, Child = contenu };
    }

    public static Brush Couleur(string hex) => Hex(hex);

    /// <summary>Étoile pleine, vide ou à moitié remplie (fraction de 0 à 1).</summary>
    public static FrameworkElement Etoile(double taille, double fraction)
    {
        const string etoile = "M8,1.2 L9.9,5.8 L14.8,6.2 L11.1,9.4 L12.3,14.2 L8,11.6 L3.7,14.2 L4.9,9.4 L1.2,6.2 L6.1,5.8 Z";
        var contenu = new Grid { Width = 16, Height = 16 };

        var contour = Trace(etoile, null, null, 1);
        Ref(contour, Shape.StrokeProperty, "ThemeEtoile");
        contenu.Children.Add(contour);

        if (fraction > 0)
        {
            var plein = Trace(etoile, null, null, 1);
            Ref(plein, Shape.FillProperty, "ThemeEtoile");
            Ref(plein, Shape.StrokeProperty, "ThemeEtoile");
            if (fraction < 1) plein.Clip = new RectangleGeometry(new Rect(0, 0, 16 * fraction, 16));
            contenu.Children.Add(plein);
        }

        return new Viewbox { Width = taille, Height = taille, Child = contenu };
    }

    public static TextBlock Texte(string texte, double taille, string cleCouleur, FontWeight? poids = null, Thickness? marge = null)
    {
        var t = new TextBlock { Text = texte, FontSize = taille, VerticalAlignment = VerticalAlignment.Center };
        if (poids != null) t.FontWeight = poids.Value;
        if (marge != null) t.Margin = marge.Value;
        return Ref(t, TextBlock.ForegroundProperty, cleCouleur);
    }

    public static MenuItem ItemMenu(string entete, Action action, bool coche = false)
    {
        var item = new MenuItem { Header = entete };
        if (coche)
        {
            item.IsCheckable = true;
            item.IsChecked = true;
        }
        item.Click += (_, _) => action();
        return item;
    }

    public static void CopierTexte(string texte)
    {
        try { Clipboard.SetText(texte); }
        catch (Exception) { /* presse-papiers occupé : on ignore */ }
    }

    public static void OuvrirUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* aucun navigateur disponible : on ignore */ }
    }

    /// <summary>Icône vectorielle d'une extension, dans un carré de taille donnée.</summary>
    private static readonly Dictionary<string, ImageSource?> _imagesIcones = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Logo PNG de l'extension, déposé dans Assets/Extensions/&lt;identifiant&gt;.png (null si absent).</summary>
    private static FrameworkElement? IconePng(string id, double taille)
    {
        if (!_imagesIcones.TryGetValue(id, out var source))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri($"pack://application:,,,/Assets/Extensions/{id}.png", UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                source = image;
            }
            catch (Exception)
            {
                source = null;
            }
            _imagesIcones[id] = source;
        }

        if (source == null) return null;
        var vue = new Image { Source = source, Width = taille, Height = taille, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(vue, BitmapScalingMode.HighQuality);
        return vue;
    }

    /// <summary>Pastille bleue « éditeur vérifié ».</summary>
    public static FrameworkElement Verifie(double taille)
    {
        var contenu = new Grid { Width = 16, Height = 16 };
        contenu.Children.Add(Trace("M8,1 A7,7 0 1 0 8,15 A7,7 0 1 0 8,1 Z", Hex("#3794FF"), null, 0));
        contenu.Children.Add(Trace("M4.8,8.2 L7,10.4 L11.2,5.8", null, Brushes.White, 1.8));
        return new Viewbox { Width = taille, Height = taille, Child = contenu, VerticalAlignment = VerticalAlignment.Center };
    }

    public static FrameworkElement Icone(string id, double taille)
    {
        var png = IconePng(id, taille);
        if (png != null) return png;

        var canvas = new Canvas { Width = 100, Height = 100 };

        switch (id)
        {
            case ExtensionsService.IdMaterialIcon:
                canvas.Children.Add(new Ellipse { Width = 100, Height = 100, Fill = Hex("#0B5BA5") });
                canvas.Children.Add(Trace(
                    "M20,30 H42 L50,39 H80 A4,4 0 0 1 84,43 V68 A4,4 0 0 1 80,72 H20 A4,4 0 0 1 16,68 V34 A4,4 0 0 1 20,30 Z",
                    Brushes.White, null, 0));
                break;

            case ExtensionsService.IdOneDarkPro:
            {
                canvas.Children.Add(new Ellipse { Width = 100, Height = 100, Fill = Hex("#2E3240") });
                var violet = Hex("#B58EF0");
                foreach (var angle in new[] { 0.0, 60.0, 120.0 })
                {
                    var orbite = new Ellipse
                    {
                        Width = 66, Height = 24, Stroke = violet, StrokeThickness = 3.5,
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new RotateTransform(angle)
                    };
                    Canvas.SetLeft(orbite, 17);
                    Canvas.SetTop(orbite, 38);
                    canvas.Children.Add(orbite);
                }
                var noyau = new Ellipse { Width = 11, Height = 11, Fill = violet };
                Canvas.SetLeft(noyau, 44.5);
                Canvas.SetTop(noyau, 44.5);
                canvas.Children.Add(noyau);
                break;
            }

            case ExtensionsService.IdErrorLens:
            {
                const string triangle = "M50,17 L90,84 L10,84 Z";
                var noir = Hex("#111111");
                var jaune = Hex("#FFE600");
                canvas.Children.Add(Trace(triangle, Brushes.White, Brushes.White, 16));
                canvas.Children.Add(Trace(triangle, noir, noir, 8));
                canvas.Children.Add(Trace(triangle, jaune, jaune, 2));
                var barre = new Rectangle { Width = 8, Height = 24, RadiusX = 3, RadiusY = 3, Fill = noir };
                Canvas.SetLeft(barre, 46);
                Canvas.SetTop(barre, 42);
                var point = new Ellipse { Width = 9, Height = 9, Fill = noir };
                Canvas.SetLeft(point, 45.5);
                Canvas.SetTop(point, 70);
                canvas.Children.Add(barre);
                canvas.Children.Add(point);
                break;
            }

            case ExtensionsService.IdCodeRunner:
            {
                var texte = new TextBlock
                {
                    Text = ".run",
                    FontFamily = new FontFamily("Segoe UI Black"),
                    FontSize = 44,
                    Foreground = Hex("#F5871F")
                };
                Canvas.SetLeft(texte, 2);
                Canvas.SetTop(texte, 22);
                canvas.Children.Add(texte);
                break;
            }

            case ExtensionsService.IdBetterComments:
                canvas.Children.Add(Trace(
                    "M16,14 H84 A8,8 0 0 1 92,22 V60 A8,8 0 0 1 84,68 H46 L26,86 V68 H16 A8,8 0 0 1 8,60 V22 A8,8 0 0 1 16,14 Z",
                    Hex("#3C4250"), null, 0));
                canvas.Children.Add(Trace("M22,29 H72", null, Hex("#98C379"), 6));
                canvas.Children.Add(Trace("M22,41 H58", null, Hex("#FF4D4D"), 6));
                canvas.Children.Add(Trace("M22,53 H78", null, Hex("#FFA24A"), 6));
                break;

            default:
            {
                var nomComplet = ExtensionsService.Obtenir(id)?.Nom ?? id;
                var mots = nomComplet.Split(new[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
                var initiales = mots.Length >= 2
                    ? (mots[0][..1] + mots[1][..1]).ToUpperInvariant()
                    : nomComplet[..Math.Min(2, nomComplet.Length)].ToUpperInvariant();

                var palette = new[] { "#3B82F6", "#8B5CF6", "#EC4899", "#F97316", "#10B981", "#06B6D4", "#EAB308", "#EF4444" };
                var somme = 0;
                foreach (var c in id) somme += c;

                canvas.Children.Add(new Rectangle
                {
                    Width = 100, Height = 100, RadiusX = 20, RadiusY = 20,
                    Fill = Hex(palette[somme % palette.Length])
                });
                var lettres = new TextBlock
                {
                    Text = initiales,
                    Width = 100,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 42,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                Canvas.SetTop(lettres, 26);
                canvas.Children.Add(lettres);
                break;
            }
        }

        return new Viewbox { Width = taille, Height = taille, Stretch = Stretch.Uniform, Child = canvas };
    }
}