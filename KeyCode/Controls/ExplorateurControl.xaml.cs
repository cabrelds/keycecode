using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyCode.Services;
namespace KeyCode.Controls
{
    /// <summary>
    /// Explorateur de fichiers : affiche l'arborescence d'un dossier.
    /// Les sous-dossiers sont chargés à l'ouverture d'un nœud (chargement paresseux).
    /// </summary>
    public partial class ExplorateurControl : UserControl
    {
        // Éléments ignorés à l'affichage
        private static readonly string[] Ignores = { "bin", "obj", ".git" };

        // Nœud factice pour afficher la flèche d'expansion avant le chargement réel
        private const string Factice = "__factice__";

        private string? _dossierRacine;

        public ExplorateurControl()
        {
            InitializeComponent();
        }

        // ---------------------------------------------------------------
        // API publique
        // ---------------------------------------------------------------

        /// <summary>Affiche le dossier, met à jour le contexte et prévient l'application.</summary>
        public void OuvrirDossier(string chemin)
        {
            if (string.IsNullOrWhiteSpace(chemin) || !Directory.Exists(chemin))
            {
                MessageBox.Show("Dossier introuvable : " + chemin, "KeyCode",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _dossierRacine = chemin;
            Contexte.DossierRacine = chemin;

            Reconstruire();

            AppEvents.SignalerDossierOuvert(chemin);
        }

        /// <summary>Recharge l'arborescence en conservant la racine actuelle.</summary>
        public void Actualiser()
        {
            if (_dossierRacine == null) return;
            Reconstruire();
        }

        // ---------------------------------------------------------------
        // Construction de l'arbre
        // ---------------------------------------------------------------

        private void Reconstruire()
        {
            Arbre.Items.Clear();
            if (_dossierRacine == null) return;

            var racine = CreerNoeud(_dossierRacine, true);
            racine.Header = CreerEntete(Path.GetFileName(_dossierRacine.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } nom
                                        ? nom : _dossierRacine, true);
            Arbre.Items.Add(racine);
            racine.IsExpanded = true; // déclenche le chargement du premier niveau
        }

        private TreeViewItem CreerNoeud(string chemin, bool estDossier)
        {
            var item = new TreeViewItem
            {
                Header = CreerEntete(Path.GetFileName(chemin), estDossier),
                Tag = chemin
            };

            if (estDossier)
                item.Items.Add(Factice); // permet d'afficher la flèche d'ouverture

            return item;
        }

        /// <summary>En-tête avec une icône différente pour dossier et fichier.</summary>
        private static StackPanel CreerEntete(string texte, bool estDossier)
        {
            var panneau = new StackPanel { Orientation = Orientation.Horizontal };
            panneau.Children.Add(new TextBlock
            {
                Text = estDossier ? "📁" : "📄",
                Margin = new Thickness(0, 0, 5, 0)
            });
            panneau.Children.Add(new TextBlock { Text = texte });
            return panneau;
        }

        private static bool EstIgnore(string chemin)
        {
            var nom = Path.GetFileName(chemin.TrimEnd(Path.DirectorySeparatorChar));
            return Ignores.Contains(nom, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Charge le contenu d'un dossier dans son nœud.</summary>
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
            catch (UnauthorizedAccessException)
            {
                // Dossier inaccessible : on le laisse vide
            }
            catch (IOException)
            {
                // Dossier supprimé entre-temps : on le laisse vide
            }
        }

        // ---------------------------------------------------------------
        // Évènements de l'arbre
        // ---------------------------------------------------------------

        // Chargement paresseux : seulement quand on ouvre le nœud
        private void Noeud_Ouvert(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem noeud
                && noeud.Items.Count == 1
                && noeud.Items[0] is string s && s == Factice)
            {
                ChargerEnfants(noeud);
            }
        }

        // Double-clic sur un fichier : demande son ouverture
        private void Arbre_DoubleClic(object sender, MouseButtonEventArgs e)
        {
            if (Arbre.SelectedItem is TreeViewItem noeud
                && noeud.Tag is string chemin
                && File.Exists(chemin))
            {
                AppEvents.DemanderFichier(chemin);
                e.Handled = true;
            }
        }

        // Sélectionne l'élément sous la souris avant d'afficher le menu contextuel
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

        // ---------------------------------------------------------------
        // Menu contextuel
        // ---------------------------------------------------------------

        private TreeViewItem? NoeudSelectionne => Arbre.SelectedItem as TreeViewItem;

        private void Menu_Ouvert(object sender, RoutedEventArgs e)
        {
            bool aSelection = NoeudSelectionne != null;
            bool estRacine = aSelection && NoeudSelectionne!.Tag is string c
                             && string.Equals(c, _dossierRacine, StringComparison.OrdinalIgnoreCase);

            MenuNouveauFichier.IsEnabled = _dossierRacine != null;
            MenuNouveauDossier.IsEnabled = _dossierRacine != null;
            MenuRenommer.IsEnabled = aSelection && !estRacine;
            MenuSupprimer.IsEnabled = aSelection && !estRacine;
        }

        /// <summary>Dossier où créer un élément : le dossier sélectionné, ou le parent du fichier sélectionné.</summary>
        private string? DossierCible()
        {
            if (NoeudSelectionne?.Tag is string chemin)
                return Directory.Exists(chemin) ? chemin : Path.GetDirectoryName(chemin);
            return _dossierRacine;
        }

        private void NouveauFichier_Click(object sender, RoutedEventArgs e)
        {
            var dossier = DossierCible();
            if (dossier == null) return;

            var nom = DemanderNom("Nouveau fichier", "Nom du fichier :", "");
            if (string.IsNullOrWhiteSpace(nom)) return;

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
            if (string.IsNullOrWhiteSpace(nom)) return;

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
            if (string.IsNullOrWhiteSpace(nouveauNom)) return;

            var parent = Path.GetDirectoryName(ancien)!;
            var nouveau = Path.Combine(parent, nouveauNom);

            if (File.Exists(nouveau) || Directory.Exists(nouveau))
            {
                MessageBox.Show("Ce nom existe déjà.", "KeyCode", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (Directory.Exists(ancien)) Directory.Move(ancien, nouveau);
                else File.Move(ancien, nouveau);
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
                if (Directory.Exists(chemin)) Directory.Delete(chemin, true);
                else File.Delete(chemin);
                Actualiser();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de supprimer : " + ex.Message, "KeyCode",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------------------------------------------------------
        // Petite boîte de saisie (WPF n'en fournit pas de base)
        // ---------------------------------------------------------------

        private string? DemanderNom(string titre, string question, string valeurInitiale)
        {
            var champ = new TextBox { Text = valeurInitiale, Margin = new Thickness(0, 8, 0, 12) };
            var ok = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var annuler = new Button { Content = "Annuler", Width = 70, IsCancel = true };

            var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            boutons.Children.Add(ok);
            boutons.Children.Add(annuler);

            var contenu = new StackPanel { Margin = new Thickness(14) };
            contenu.Children.Add(new TextBlock { Text = question });
            contenu.Children.Add(champ);
            contenu.Children.Add(boutons);

            var fenetre = new Window
            {
                Title = titre,
                Content = contenu,
                Width = 340,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            ok.Click += (_, _) => fenetre.DialogResult = true;
            fenetre.Loaded += (_, _) => { champ.Focus(); champ.SelectAll(); };

            return fenetre.ShowDialog() == true ? champ.Text.Trim() : null;
        }
    }
}
