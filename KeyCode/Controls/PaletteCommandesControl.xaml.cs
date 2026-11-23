using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>
/// Palette de commandes (Ctrl+Maj+P) et accès rapide aux fichiers du projet (Ctrl+P) style VS Code.
/// </summary>
public partial class PaletteCommandesControl : UserControl
{
    public sealed record EntreePalette(string Libelle, string SousTitre, Action Executer)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(SousTitre) ? Libelle : $"{Libelle}   —   {SousTitre}";
    }

    private static readonly string[] Ignores = { "bin", "obj", ".git", "node_modules" };
    private List<EntreePalette> _commandes = new();
    private List<EntreePalette> _fichiers = new();

    public PaletteCommandesControl()
    {
        InitializeComponent();
    }

    public void DefinirCommandes(IEnumerable<EntreePalette> commandes)
    {
        _commandes = commandes.ToList();
    }

    public void Ouvrir(bool modeCommandes)
    {
        ChargerFichiersEspaceTravail();
        Visibility = Visibility.Visible;
        ChampFiltre.Text = modeCommandes ? ">" : "";
        ChampFiltre.Focus();
        ChampFiltre.CaretIndex = ChampFiltre.Text.Length;
        Filtrer();
    }

    public void Fermer()
    {
        Visibility = Visibility.Collapsed;
        Contexte.EditeurActif?.TextArea.Focus();
    }

    private void ChargerFichiersEspaceTravail()
    {
        _fichiers.Clear();
        var racine = Contexte.DossierRacine;
        if (string.IsNullOrWhiteSpace(racine) || !Directory.Exists(racine)) return;

        var pile = new Stack<string>();
        pile.Push(racine);

        while (pile.Count > 0 && _fichiers.Count < 800)
        {
            var dir = pile.Pop();
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    var cheminCapture = f;
                    var relatif = Path.GetRelativePath(racine, f);
                    _fichiers.Add(new EntreePalette(
                        Path.GetFileName(f),
                        relatif,
                        () => AppEvents.DemanderFichier(cheminCapture)));
                    if (_fichiers.Count >= 800) break;
                }

                foreach (var sous in Directory.GetDirectories(dir))
                {
                    var nom = Path.GetFileName(sous);
                    if (!Ignores.Contains(nom, StringComparer.OrdinalIgnoreCase))
                        pile.Push(sous);
                }
            }
            catch (Exception) { }
        }
    }

    private void Filtrer()
    {
        var saisie = ChampFiltre.Text ?? "";
        var modeCmd = saisie.StartsWith('>');
        var requete = (modeCmd ? saisie[1..] : saisie).Trim();
        var source = modeCmd ? _commandes : _fichiers;

        ListeElements.Items.Clear();

        var correspondants = string.IsNullOrEmpty(requete)
            ? source.Take(50)
            : source.Where(e =>
                e.Libelle.Contains(requete, StringComparison.OrdinalIgnoreCase) ||
                e.SousTitre.Contains(requete, StringComparison.OrdinalIgnoreCase)).Take(50);

        foreach (var item in correspondants)
            ListeElements.Items.Add(item);

        if (ListeElements.Items.Count > 0)
            ListeElements.SelectedIndex = 0;
    }

    private void ValiderSelection()
    {
        if (ListeElements.SelectedItem is EntreePalette entree)
        {
            Fermer();
            entree.Executer();
        }
    }

    private void ChampFiltre_TextChanged(object sender, TextChangedEventArgs e) => Filtrer();

    private void ChampFiltre_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (ListeElements.Items.Count > 0)
                    ListeElements.SelectedIndex = Math.Min(ListeElements.Items.Count - 1, ListeElements.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Up:
                if (ListeElements.Items.Count > 0)
                    ListeElements.SelectedIndex = Math.Max(0, ListeElements.SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Enter:
                ValiderSelection();
                e.Handled = true;
                break;
        }
    }

    private void Palette_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Fermer();
            e.Handled = true;
        }
    }

    private void ListeElements_DoubleClick(object sender, MouseButtonEventArgs e) => ValiderSelection();
}
