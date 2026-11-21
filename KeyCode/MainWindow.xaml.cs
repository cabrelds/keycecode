using Microsoft.Win32;
using System.IO;
using System.Windows;
using ICSharpCode.AvalonEdit.Highlighting;

namespace KeyCode;

public partial class MainWindow : Window
{
    private string? _chemin;

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

    private async void Executer_Click(object sender, RoutedEventArgs e)
    {
        Enregistrer_Click(sender, e);
        if (_chemin == null) return;
        Sortie.Text = "Exécution...";
        Sortie.Text = await Runner.ExecuterAsync(_chemin);
    }
}