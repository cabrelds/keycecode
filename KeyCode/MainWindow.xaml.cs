using Microsoft.Win32;
using System.IO;
using System.Windows;
using ICSharpCode.AvalonEdit.Highlighting;

namespace KeyCode;

public partial class MainWindow : Window
{
    private string? _chemin;
    private readonly Runner _runner = new();

    public MainWindow() { InitializeComponent(); }

    private void Ouvrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog();
        if (dlg.ShowDialog() == true)
        {
            _chemin = dlg.FileName;
            Editeur.Load(_chemin);
            Editeur.SyntaxHighlighting =
                HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(_chemin));
        }
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        if (_chemin == null)
        {
            var dlg = new SaveFileDialog();
            if (dlg.ShowDialog() != true) return;
            _chemin = dlg.FileName;
        }
        Editeur.Save(_chemin);
    }

    private void Executer_Click(object sender, RoutedEventArgs e)
    {
        if (_runner.EnCours) _runner.Arreter();
        Enregistrer_Click(sender, e);
        if (_chemin == null) return;
        Sortie.Text = "";
        Saisie.Focus();
        _runner.Demarrer(_chemin,
            texte => Dispatcher.Invoke(() => { Sortie.AppendText(texte); Sortie.ScrollToEnd(); }),
            () => Dispatcher.Invoke(() => { Sortie.AppendText("\n[Programme terminé]\n"); Sortie.ScrollToEnd(); }));
    }

    private void Saisie_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        Sortie.AppendText(Saisie.Text + "\n");
        _runner.Envoyer(Saisie.Text);
        Saisie.Clear();
    }
}