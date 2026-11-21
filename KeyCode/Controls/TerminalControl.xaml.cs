using System.IO;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeyCode.Controls;

public partial class TerminalControl : UserControl
{
    private readonly Runner _runner = new();
    private readonly List<string> _historique = new();
    private int _indexHistorique;
    private volatile int _numeroExecution;

    public TerminalControl()
    {
        InitializeComponent();
    }

    // Lance le programme avec le Runner
    public void Executer(string chemin)
    {
        // Le numéro évite qu'un ancien programme écrive dans la nouvelle sortie
        var numero = ++_numeroExecution;

        Arreter();
        Effacer();
        Ajouter($"> {Path.GetFileName(chemin)}\n");

        _runner.Demarrer(chemin,
            texte => { if (numero == _numeroExecution) Ajouter(texte); },
            () => { if (numero == _numeroExecution) Ajouter("\n[Programme terminé]\n"); });

        Saisie.Focus();
    }

    // Stoppe le programme en cours
    public void Arreter() => _runner.Arreter();

    // Vide la sortie
    public void Effacer() => Sortie.Clear();

    // Ajoute du texte (appelable depuis un autre thread)
    private void Ajouter(string texte)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Sortie.AppendText(texte);
            Sortie.ScrollToEnd();
        });
    }

    private void EnvoyerSaisie()
    {
        var texte = Saisie.Text;
        Saisie.Clear();
        if (!_runner.EnCours) return;

        Ajouter(texte + "\n"); // on affiche ce qu'on a tapé, comme VS Code
        if (texte.Length > 0) _historique.Add(texte);
        _indexHistorique = _historique.Count;

        try { _runner.Envoyer(texte); }
        catch (Exception) { /* le programme vient de se terminer */ }
    }

    // Flèches haut/bas : rappelle les saisies précédentes
    private void ParcourirHistorique(int sens)
    {
        var index = _indexHistorique + sens;
        if (index < 0 || index > _historique.Count) return;

        _indexHistorique = index;
        Saisie.Text = index == _historique.Count ? "" : _historique[index];
        Saisie.CaretIndex = Saisie.Text.Length;
    }

    private void Saisie_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                EnvoyerSaisie();
                e.Handled = true;
                break;
            case Key.Up:
                ParcourirHistorique(-1);
                e.Handled = true;
                break;
            case Key.Down:
                ParcourirHistorique(+1);
                e.Handled = true;
                break;
            // Ctrl+C sans texte sélectionné : arrête le programme
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && Saisie.SelectionLength == 0:
                Arreter();
                e.Handled = true;
                break;
        }
    }

    private void Arreter_Click(object sender, System.Windows.RoutedEventArgs e) => Arreter();
    private void Effacer_Click(object sender, System.Windows.RoutedEventArgs e) => Effacer();
}