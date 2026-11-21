using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using KeyCode.Services;

namespace KeyCode.Controls;

public partial class RechercheControl : UserControl
{
    private Window? _fenetre;

    private static readonly Brush CouleurNormale = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly Brush CouleurErreur = new SolidColorBrush(Color.FromRgb(0xF4, 0x87, 0x71));

    public RechercheControl()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            _fenetre = Window.GetWindow(this);
            if (_fenetre != null) _fenetre.PreviewKeyDown += Fenetre_PreviewKeyDown;
            AppEvents.EditeurActifChange += SurEditeurActifChange;
        };
        Unloaded += (_, _) =>
        {
            if (_fenetre != null) _fenetre.PreviewKeyDown -= Fenetre_PreviewKeyDown;
            AppEvents.EditeurActifChange -= SurEditeurActifChange;
        };
    }

    // Éditeur de l'onglet actif (écrit par OngletsControl)
    private static TextEditor? Editeur => Contexte.EditeurActif;

    // ---------- Affichage ----------

    // Ctrl+F : recherche, Ctrl+H : recherche + remplacement
    public void Afficher(bool remplacer)
    {
        Visibility = Visibility.Visible;
        LigneAller.Visibility = Visibility.Collapsed;
        LigneRemplacer.Visibility = remplacer ? Visibility.Visible : Visibility.Collapsed;

        // Comme VS Code : le texte sélectionné devient le terme recherché
        var editeur = Editeur;
        if (editeur != null && editeur.SelectionLength > 0 &&
            !editeur.SelectedText.Contains('\n') && !editeur.SelectedText.Contains('\r'))
        {
            Recherche.Text = editeur.SelectedText;
        }

        Recherche.Focus();
        Recherche.SelectAll();
        MettreAJourCompteur();
    }

    public void Masquer()
    {
        Visibility = Visibility.Collapsed;
        Editeur?.TextArea.Focus();
    }

    private void AfficherAllerALigne()
    {
        Visibility = Visibility.Visible;
        LigneRemplacer.Visibility = Visibility.Collapsed;
        LigneAller.Visibility = Visibility.Visible;
        NumLigne.Focus();
        NumLigne.SelectAll();
    }

    // ---------- Recherche ----------

    // Liste de toutes les correspondances (position, longueur)
    private List<(int Debut, int Longueur)> Chercher()
    {
        var resultat = new List<(int Debut, int Longueur)>();
        var editeur = Editeur;
        var terme = Recherche.Text;
        if (editeur == null || terme.Length == 0) return resultat;

        var motif = Regex.Escape(terme);
        if (BtnMot.IsChecked == true)
            motif = @"(?<!\w)" + motif + @"(?!\w)"; // mot entier

        var options = BtnCasse.IsChecked == true
            ? RegexOptions.None
            : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        foreach (Match m in Regex.Matches(editeur.Text, motif, options))
            resultat.Add((m.Index, m.Length));

        return resultat;
    }

    private static void Selectionner(TextEditor editeur, int debut, int longueur)
    {
        editeur.Select(debut, longueur);
        var position = editeur.Document.GetLocation(debut);
        editeur.ScrollTo(position.Line, position.Column);
    }

    // Sélectionne la première correspondance à partir de la position (revient au début si besoin)
    private void TrouverDepuis(int position)
    {
        var editeur = Editeur;
        var resultats = Chercher();
        if (editeur == null || resultats.Count == 0)
        {
            MettreAJourCompteur();
            return;
        }

        var cible = resultats.FirstOrDefault(r => r.Debut >= position);
        if (cible.Longueur == 0) cible = resultats[0];

        Selectionner(editeur, cible.Debut, cible.Longueur);
        MettreAJourCompteur();
    }

    public void TrouverSuivant()
    {
        var editeur = Editeur;
        if (editeur == null) return;
        TrouverDepuis(editeur.SelectionStart + editeur.SelectionLength);
    }

    public void TrouverPrecedent()
    {
        var editeur = Editeur;
        var resultats = Chercher();
        if (editeur == null || resultats.Count == 0)
        {
            MettreAJourCompteur();
            return;
        }

        var cible = resultats.LastOrDefault(r => r.Debut < editeur.SelectionStart);
        if (cible.Longueur == 0) cible = resultats[^1]; // retour à la fin

        Selectionner(editeur, cible.Debut, cible.Longueur);
        MettreAJourCompteur();
    }

    // ---------- Remplacement ----------

    public void Remplacer()
    {
        var editeur = Editeur;
        if (editeur == null) return;

        var courant = Chercher().FirstOrDefault(
            r => r.Debut == editeur.SelectionStart && r.Longueur == editeur.SelectionLength);

        // Rien de sélectionné : on va d'abord sur la prochaine correspondance
        if (courant.Longueur == 0)
        {
            TrouverSuivant();
            return;
        }

        editeur.Document.Replace(courant.Debut, courant.Longueur, Remplacement.Text);
        TrouverDepuis(courant.Debut + Remplacement.Text.Length);
    }

    public void RemplacerTout()
    {
        var editeur = Editeur;
        var resultats = Chercher();
        if (editeur == null || resultats.Count == 0) return;

        // Un seul bloc : Ctrl+Z annule tout d'un coup. On part de la fin pour garder les positions valides.
        using (editeur.Document.RunUpdate())
        {
            for (var i = resultats.Count - 1; i >= 0; i--)
                editeur.Document.Replace(resultats[i].Debut, resultats[i].Longueur, Remplacement.Text);
        }

        Compteur.Foreground = CouleurNormale;
        Compteur.Text = resultats.Count == 1 ? "1 remplacement" : $"{resultats.Count} remplacements";
    }

    // ---------- Aller à la ligne ----------

    public void AllerALigne(int ligne)
    {
        var editeur = Editeur;
        if (editeur == null) return;

        ligne = Math.Clamp(ligne, 1, editeur.Document.LineCount);
        var l = editeur.Document.GetLineByNumber(ligne);
        editeur.Select(l.Offset, 0);
        editeur.TextArea.Caret.Offset = l.Offset;
        editeur.ScrollToLine(ligne);
        editeur.TextArea.Focus();
    }

    // ---------- Compteur « 3 sur 12 » ----------

    private void MettreAJourCompteur()
    {
        if (Compteur == null) return;

        if (Recherche.Text.Length == 0)
        {
            Compteur.Text = "";
            Compteur.Foreground = CouleurNormale;
            return;
        }

        var editeur = Editeur;
        var resultats = Chercher();
        if (resultats.Count == 0)
        {
            Compteur.Text = "Aucun résultat";
            Compteur.Foreground = CouleurErreur;
            return;
        }

        Compteur.Foreground = CouleurNormale;
        var index = editeur == null
            ? -1
            : resultats.FindIndex(r => r.Debut == editeur.SelectionStart && r.Longueur == editeur.SelectionLength);
        Compteur.Text = index >= 0 ? $"{index + 1} sur {resultats.Count}" : $"? sur {resultats.Count}";
    }

    private void SurEditeurActifChange() => Dispatcher.InvokeAsync(MettreAJourCompteur);

    // ---------- Raccourcis ----------

    // Raccourcis valables dans toute la fenêtre
    private void Fenetre_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl && e.Key == Key.F)
        {
            Afficher(false);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.H)
        {
            Afficher(true);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.G)
        {
            AfficherAllerALigne();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && Recherche.Text.Length > 0)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) TrouverPrecedent();
            else TrouverSuivant();
            e.Handled = true;
        }
    }

    // Échap ferme la barre
    private void Controle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Masquer();
            e.Handled = true;
        }
    }

    private void Recherche_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) TrouverPrecedent();
        else TrouverSuivant();
        e.Handled = true;
    }

    private void Remplacement_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        Remplacer();
        e.Handled = true;
    }

    private void NumLigne_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (int.TryParse(NumLigne.Text, out var ligne)) AllerALigne(ligne);
        Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    // ---------- Boutons ----------

    // On cherche au fur et à mesure de la frappe
    private void Recherche_TextChanged(object sender, TextChangedEventArgs e)
    {
        var editeur = Editeur;
        if (editeur == null)
        {
            MettreAJourCompteur();
            return;
        }
        TrouverDepuis(editeur.SelectionStart);
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        var editeur = Editeur;
        if (editeur == null)
        {
            MettreAJourCompteur();
            return;
        }
        TrouverDepuis(editeur.SelectionStart);
    }

    private void Suivant_Click(object sender, RoutedEventArgs e) => TrouverSuivant();
    private void Precedent_Click(object sender, RoutedEventArgs e) => TrouverPrecedent();
    private void Fermer_Click(object sender, RoutedEventArgs e) => Masquer();
    private void Remplacer_Click(object sender, RoutedEventArgs e) => Remplacer();
    private void RemplacerTout_Click(object sender, RoutedEventArgs e) => RemplacerTout();
}
