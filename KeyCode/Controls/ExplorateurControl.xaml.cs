using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>
/// Explorateur de fichiers : affiche l'arborescence d'un dossier avec chargement paresseux,
/// préservation de l'état déplié à l'actualisation et synchronisation des onglets lors des opérations.
/// </summary>
public partial class ExplorateurControl : UserControl
{
    private static readonly string[] Ignores = { "bin", "obj", ".git" };
    private const string Factice = "__factice__";

    private string? _dossierRacine;

    private bool _iconesMaterial;

    public ExplorateurControl()
    {
        InitializeComponent();

        _iconesMaterial = ExtensionsService.EstActive(ExtensionsService.IdMaterialIcon);
        AppEvents.ExtensionsChangees += () => Dispatcher.InvokeAsync(() =>
        {
            var actif = ExtensionsService.EstActive(ExtensionsService.IdMaterialIcon);
            if (actif == _iconesMaterial) return;
            _iconesMaterial = actif;
            Actualiser();
        });
    }

    /// <summary>Affiche le dossier, met à jour le contexte et prévient l'application.</summary>
    public void OuvrirDossier(string chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin) || !Directory.Exists(chemin))
        {
            MessageBox.Show("Dossier introuvable : " + chemin, "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _dossierRacine = Path.GetFullPath(chemin);
        Contexte.DossierRacine = _dossierRacine;

        Reconstruire(null);

        AppEvents.SignalerDossierOuvert(_dossierRacine);
    }

    /// <summary>Recharge l'arborescence en préservant les nœuds actuellement dépliés.</summary>
    public void Actualiser()
    {
        if (_dossierRacine == null) return;
        var deplies = CollecterDossiersDeplies();
        Reconstruire(deplies);
    }

    private HashSet<string> CollecterDossiersDeplies()
    {
        var resultat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Parcourir(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is not TreeViewItem noeud || noeud.Tag is not string chemin) continue;
                if (noeud.IsExpanded)
                {
                    resultat.Add(chemin);
                    Parcourir(noeud.Items);
                }
            }
        }
        Parcourir(Arbre.Items);
        return resultat;
    }

    private void Reconstruire(HashSet<string>? deplies)
    {
        Arbre.Items.Clear();
        if (_dossierRacine == null)
        {
            TexteAucunDossier.Visibility = Visibility.Visible;
            return;
        }

        TexteAucunDossier.Visibility = Visibility.Collapsed;
        var racine = CreerNoeud(_dossierRacine, true);
        var nomRacine = Path.GetFileName(_dossierRacine.TrimEnd(Path.DirectorySeparatorChar));
        racine.Header = CreerEntete(string.IsNullOrEmpty(nomRacine) ? _dossierRacine : nomRacine, true, _dossierRacine);
        Arbre.Items.Add(racine);
        racine.IsExpanded = true;

        if (deplies != null)
            RestaurerDeplies(racine, deplies);
    }

    private void RestaurerDeplies(TreeViewItem parent, HashSet<string> deplies)
    {
        foreach (var obj in parent.Items)
        {
            if (obj is not TreeViewItem enfant || enfant.Tag is not string chemin) continue;
            if (Directory.Exists(chemin) && deplies.Contains(chemin))
            {
                enfant.IsExpanded = true;
                RestaurerDeplies(enfant, deplies);
            }
        }
    }

    private TreeViewItem CreerNoeud(string chemin, bool estDossier)
    {
        var item = new TreeViewItem
        {
            Header = CreerEntete(Path.GetFileName(chemin), estDossier, chemin),
            Tag = chemin
        };

        if (estDossier)
            item.Items.Add(Factice);

        return item;
    }

    private static StackPanel CreerEntete(string texte, bool estDossier, string chemin)
    {
        var panneau = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

        // Extension Material Icon Theme active : icônes vectorielles à la place des emojis et des pastilles
        if (ExtensionsService.EstActive(ExtensionsService.IdMaterialIcon))
        {
            panneau.Children.Add(IconeMaterial(chemin, estDossier));
            panneau.Children.Add(new TextBlock { Text = texte, VerticalAlignment = VerticalAlignment.Center });
            return panneau;
        }

        if (estDossier)
        {
            panneau.Children.Add(new TextBlock
            {
                Text = "▸📁",
                FontSize = 11,
                Foreground = ThemeManager.Pinceau("ThemeAccent"),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            // Affiche uniquement l'icône dossier proprement
            ((TextBlock)panneau.Children[0]).Text = "📁";
        }
        else
        {
            var (sigle, couleurHex) =BadgeFichier(chemin);
            var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(couleurHex));
            pinceau.Freeze();

            panneau.Children.Add(new Border
            {
                Background = pinceau,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 0, 3, 0),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = sigle,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            });
        }

        panneau.Children.Add(new TextBlock
        {
            Text = texte,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panneau;
    }

    private static Brush PinceauHex(string hex)
    {
        var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        pinceau.Freeze();
        return pinceau;
    }

    private static System.Windows.Shapes.Path Forme(string donnees, Brush? remplissage, Brush? trait, double epaisseur) => new()
    {
        Data = Geometry.Parse(donnees),
        Fill = remplissage,
        Stroke = trait,
        StrokeThickness = epaisseur,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
    };

    private static string CouleurDossier(string chemin)
    {
        var nom = Path.GetFileName(chemin.TrimEnd(Path.DirectorySeparatorChar)).ToLowerInvariant();
        return nom switch
        {
            "src" or "source" or "lib" or "node_modules" => "#8BC34A",
            "docs" or "doc" or "documentation" => "#42A5F5",
            "assets" or "images" or "img" or "resources" or "icons" => "#FFCA28",
            "test" or "tests" or "__tests__" or "spec" => "#26A69A",
            "config" or ".vscode" or "settings" => "#7E57C2",
            "controls" or "components" or "views" or "ui" => "#EC407A",
            "services" or "utils" or "helpers" => "#26C6DA",
            "models" or "data" => "#FF7043",
            _ => "#42A5F5"
        };
    }

    /// <summary>Icône façon Material Icon Theme : dossier coloré selon son nom, fichier coloré selon son type.</summary>
    private static FrameworkElement IconeMaterial(string chemin, bool estDossier)
    {
        var contenu = new Grid { Width = 16, Height = 16 };

        if (estDossier)
        {
            contenu.Children.Add(Forme("M1.5,3.5 H6.3 L7.8,5 H14.5 V12.5 H1.5 Z",
                PinceauHex(CouleurDossier(chemin)), null, 0));
        }
        else
        {
            var couleur = PinceauHex(BadgeFichier(chemin).CouleurHex);
            var blanc = Brushes.White;
            var blancDoux = PinceauHex("#99FFFFFF");

            contenu.Children.Add(Forme("M3,1.5 H9.5 L13,5 V14.5 H3 Z", couleur, null, 0));
            contenu.Children.Add(Forme("M9.5,1.5 V5 H13", null, blancDoux, 1));

            var ext = Path.GetExtension(chemin).ToLowerInvariant();
            var marque = ext switch
            {
                ".cs" or ".py" or ".pyw" or ".js" or ".mjs" or ".ts" or ".tsx" or ".java" or ".c" or ".h"
                    or ".cpp" or ".hpp" or ".php" or ".go" or ".rs" or ".xaml" or ".xml" or ".csproj"
                    or ".html" or ".htm" or ".css" or ".scss" or ".ps1" or ".bat" or ".cmd" or ".sql"
                    => "M6.6,8 L4.8,10 L6.6,12 M9.4,8 L11.2,10 L9.4,12",
                ".json" or ".yml" or ".yaml" or ".config"
                    => "M6.9,7.6 Q5.9,7.6 5.9,8.6 V9.2 Q5.9,10 5.1,10.3 Q5.9,10.6 5.9,11.4 V12 Q5.9,13 6.9,13 M9.5,7.6 Q10.5,7.6 10.5,8.6 V9.2 Q10.5,10 11.3,10.3 Q10.5,10.6 10.5,11.4 V12 Q10.5,13 9.5,13",
                ".md" or ".txt" or ".log" or ".csv"
                    => "M5.2,8.3 H10.8 M5.2,10.3 H10.8 M5.2,12.3 H8.6",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".svg"
                    => "M5,13 L7.4,9.6 L8.8,11.4 L9.8,10.4 L11.2,13",
                _ => ""
            };
            if (marque.Length > 0)
                contenu.Children.Add(Forme(marque, null, blanc, 1.1));
        }

        return new Viewbox
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = contenu
        };
    }

    private static (string Sigle, string CouleurHex) BadgeFichier(string chemin)
    {
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        return ext switch
        {
            ".py" or ".pyw" => ("PY", "#3572A5"),
            ".cs" => ("C#", "#68217A"),
            ".js" or ".mjs" => ("JS", "#C99A14"),
            ".ts" or ".tsx" => ("TS", "#3178C6"),
            ".java" => ("JV", "#B07219"),
            ".c" or ".h" => ("C", "#555555"),
            ".cpp" or ".hpp" => ("C++", "#E04A7A"),
            ".php" => ("PHP", "#4F5D95"),
            ".go" => ("GO", "#00ADD8"),
            ".rs" => ("RS", "#B7410E"),
            ".html" or ".htm" => ("HTM", "#E34C26"),
            ".css" or ".scss" => ("CSS", "#563D7C"),
            ".json" => ("{}", "#2E8B57"),
            ".xml" or ".xaml" or ".csproj" => ("<>", "#D96523"),
            ".md" => ("MD", "#1F6FEB"),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".svg" => ("IMG", "#008B8B"),
            _ => ("•", "#6E7681")
        };
    }

    private static bool EstIgnore(string chemin)
    {
        var nom = Path.GetFileName(chemin.TrimEnd(Path.DirectorySeparatorChar));
        return Ignores.Contains(nom, StringComparer.OrdinalIgnoreCase);
    }

    private void ChargerEnfants(TreeViewItem noeud)
    {
        if (noeud.Tag is not string chemin) return;

        noeud.Items.Clear();
        try
        {
            foreach (var dossier in Directory.GetDirectories(chemin)
                         .Where(d => !EstIgnore(d))
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                noeud.Items.Add(CreerNoeud(dossier, true));
            }

            foreach (var fichier in Directory.GetFiles(chemin)
                         .Where(f => !EstIgnore(f))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                noeud.Items.Add(CreerNoeud(fichier, false));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void Noeud_Ouvert(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem noeud
            && noeud.Items.Count == 1
            && noeud.Items[0] is string s && s == Factice)
        {
            ChargerEnfants(noeud);
        }
    }

    private void Arbre_DoubleClic(object sender, MouseButtonEventArgs e)
    {
        // Vérifie que le double-clic a bien eu lieu sur un TreeViewItem (pas dans le vide)
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not TreeViewItem)
            source = VisualTreeHelper.GetParent(source);

        if (source is TreeViewItem noeud && noeud.Tag is string chemin && File.Exists(chemin))
        {
            AppEvents.DemanderFichier(chemin);
            e.Handled = true;
        }
    }

    private void Arbre_ClicDroitPrealable(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not TreeViewItem)
            source = VisualTreeHelper.GetParent(source);

        if (source is TreeViewItem noeud)
        {
            noeud.IsSelected = true;
            noeud.Focus();
        }
    }

    private TreeViewItem? NoeudSelectionne => Arbre.SelectedItem as TreeViewItem;

    private void Menu_Ouvert(object sender, RoutedEventArgs e)
    {
        var aSelection = NoeudSelectionne != null;
        var estRacine = aSelection && NoeudSelectionne!.Tag is string c
                         && string.Equals(c, _dossierRacine, StringComparison.OrdinalIgnoreCase);

        MenuNouveauFichier.IsEnabled = _dossierRacine != null;
        MenuNouveauDossier.IsEnabled = _dossierRacine != null;
        MenuRenommer.IsEnabled = aSelection && !estRacine;
        MenuSupprimer.IsEnabled = aSelection && !estRacine;
        MenuActualiser.IsEnabled = _dossierRacine != null;
    }

    private string? DossierCible()
    {
        if (NoeudSelectionne?.Tag is string chemin)
            return Directory.Exists(chemin) ? chemin : Path.GetDirectoryName(chemin);
        return _dossierRacine;
    }

    private void Actualiser_Click(object sender, RoutedEventArgs e) => Actualiser();

    private void NouveauFichier_Click(object sender, RoutedEventArgs e)
    {
        var dossier = DossierCible();
        if (dossier == null) return;

        var nom = DemanderNom("Nouveau fichier", "Nom du fichier :", "");
        if (string.IsNullOrWhiteSpace(nom) || !NomValide(nom)) return;

        var chemin = Path.Combine(dossier, nom);
        if (File.Exists(chemin) || Directory.Exists(chemin))
        {
            MessageBox.Show("Ce nom existe déjà.", "KeyCode", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            File.WriteAllText(chemin, string.Empty);
            Actualiser();
            AppEvents.DemanderFichier(chemin);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible de créer le fichier : " + ex.Message, "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NouveauDossier_Click(object sender, RoutedEventArgs e)
    {
        var dossier = DossierCible();
        if (dossier == null) return;

        var nom = DemanderNom("Nouveau dossier", "Nom du dossier :", "");
        if (string.IsNullOrWhiteSpace(nom) || !NomValide(nom)) return;

        var chemin = Path.Combine(dossier, nom);
        if (File.Exists(chemin) || Directory.Exists(chemin))
        {
            MessageBox.Show("Ce nom existe déjà.", "KeyCode", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(chemin);
            Actualiser();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible de créer le dossier : " + ex.Message, "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Renommer_Click(object sender, RoutedEventArgs e)
    {
        if (NoeudSelectionne?.Tag is not string ancien) return;

        var nouveauNom = DemanderNom("Renommer", "Nouveau nom :", Path.GetFileName(ancien));
        if (string.IsNullOrWhiteSpace(nouveauNom) || !NomValide(nouveauNom)) return;

        var parent = Path.GetDirectoryName(ancien)!;
        var nouveau = Path.Combine(parent, nouveauNom);
        if (string.Equals(ancien, nouveau, StringComparison.Ordinal)) return;

        if (File.Exists(nouveau) || Directory.Exists(nouveau))
        {
            MessageBox.Show("Ce nom existe déjà.", "KeyCode", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (Directory.Exists(ancien))
            {
                Directory.Move(ancien, nouveau);
            }
            else
            {
                File.Move(ancien, nouveau);
                AppEvents.SignalerFichierRenomme(ancien, nouveau);
            }
            Actualiser();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible de renommer : " + ex.Message, "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        if (NoeudSelectionne?.Tag is not string chemin) return;

        var reponse = MessageBox.Show(
            "Supprimer définitivement « " + Path.GetFileName(chemin) + " » ?",
            "Confirmer la suppression",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (reponse != MessageBoxResult.Yes) return;

        try
        {
            if (Directory.Exists(chemin))
            {
                Directory.Delete(chemin, true);
            }
            else
            {
                File.Delete(chemin);
                AppEvents.SignalerFichierSupprime(chemin);
            }
            Actualiser();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible de supprimer : " + ex.Message, "KeyCode",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool NomValide(string nom)
    {
        if (nom.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) return true;
        MessageBox.Show("Le nom contient des caractères non autorisés.", "KeyCode",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private string? DemanderNom(string titre, string question, string valeurInitiale)
    {
        var champ = new TextBox
        {
            Text = valeurInitiale,
            Margin = new Thickness(0, 8, 0, 14),
            Style = (Style)Application.Current.FindResource("ChampVsCode")
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 80,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.FindResource("BoutonPrimaireVsCode")
        };
        var annuler = new Button
        {
            Content = "Annuler",
            Width = 80,
            IsCancel = true,
            Style = (Style)Application.Current.FindResource("BoutonSecondaireVsCode")
        };

        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        boutons.Children.Add(ok);
        boutons.Children.Add(annuler);

        var contenu = new StackPanel { Margin = new Thickness(16) };
        contenu.Children.Add(new TextBlock
        {
            Text = question,
            Foreground = ThemeManager.Pinceau("ThemeTexte")
        });
        contenu.Children.Add(champ);
        contenu.Children.Add(boutons);

        var fenetre = new Window
        {
            Title = titre,
            Content = contenu,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Background = ThemeManager.Pinceau("ThemeFondSecondaire"),
            Foreground = ThemeManager.Pinceau("ThemeTexte"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this)
        };

        ok.Click += (_, _) => fenetre.DialogResult = true;
        fenetre.Loaded += (_, _) => { champ.Focus(); champ.SelectAll(); };

        return fenetre.ShowDialog() == true ? champ.Text.Trim() : null;
    }
}
