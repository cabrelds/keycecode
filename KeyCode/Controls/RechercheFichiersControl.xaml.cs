using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>
/// Recherche globale dans tous les fichiers texte du dossier racine.
/// Les résultats s'affichent au format « fichier : ligne : texte ».
/// </summary>
public partial class RechercheFichiersControl : UserControl
{
    private static readonly string[] Ignores = { "bin", "obj", ".git", "node_modules" };
    private const int MaxResultats = 1000;
    private const long TailleMax = 2 * 1024 * 1024;

    private sealed record Resultat(string Chemin, int Ligne, string Affichage)
    {
        public override string ToString() => Affichage;
    }

    public RechercheFichiersControl()
    {
        InitializeComponent();
    }

    public void Afficher()
    {
        Visibility = Visibility.Visible;
        ChampRecherche.Focus();
        ChampRecherche.SelectAll();
    }

    public void Masquer()
    {
        Visibility = Visibility.Collapsed;
    }

    public async void Rechercher(string texte)
    {
        ListeResultats.Items.Clear();

        if (string.IsNullOrEmpty(texte)) return;

        var racine = Contexte.DossierRacine;
        if (string.IsNullOrEmpty(racine) || !Directory.Exists(racine))
        {
            ListeResultats.Items.Add("Aucun dossier ouvert.");
            return;
        }

        BoutonRechercher.IsEnabled = false;
        try
        {
            var resultats = await Task.Run(() => Chercher(racine, texte));

            foreach (var r in resultats)
                ListeResultats.Items.Add(r);

            if (resultats.Count == 0)
                ListeResultats.Items.Add("Aucun résultat.");
            else if (resultats.Count >= MaxResultats)
                ListeResultats.Items.Add("… (limité à " + MaxResultats + " résultats)");
        }
        finally
        {
            BoutonRechercher.IsEnabled = true;
        }
    }

    private static List<Resultat> Chercher(string racine, string texte)
    {
        var resultats = new List<Resultat>();

        foreach (var fichier in ListerFichiers(racine))
        {
            if (resultats.Count >= MaxResultats) break;
            if (!EstFichierTexte(fichier)) continue;

            try
            {
                var numero = 0;
                foreach (var ligne in File.ReadLines(fichier))
                {
                    numero++;
                    if (ligne.Contains(texte, StringComparison.OrdinalIgnoreCase))
                    {
                        var relatif = Path.GetRelativePath(racine, fichier);
                        resultats.Add(new Resultat(fichier, numero,
                            $"{relatif} : {numero} : {ligne.Trim()}"));

                        if (resultats.Count >= MaxResultats) break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return resultats;
    }

    private static IEnumerable<string> ListerFichiers(string dossier)
    {
        var pile = new Stack<string>();
        pile.Push(dossier);

        while (pile.Count > 0)
        {
            var courant = pile.Pop();

            string[] fichiers = Array.Empty<string>();
            string[] sousDossiers = Array.Empty<string>();
            try
            {
                fichiers = Directory.GetFiles(courant);
                sousDossiers = Directory.GetDirectories(courant);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            foreach (var f in fichiers)
                yield return f;

            foreach (var d in sousDossiers)
            {
                var nom = Path.GetFileName(d);
                if (!Ignores.Contains(nom, StringComparer.OrdinalIgnoreCase))
                    pile.Push(d);
            }
        }
    }

    private static bool EstFichierTexte(string chemin)
    {
        try
        {
            var info = new FileInfo(chemin);
            if (info.Length > TailleMax) return false;

            using var flux = File.OpenRead(chemin);
            var tampon = new byte[4096];
            var lus = flux.Read(tampon, 0, tampon.Length);
            for (var i = 0; i < lus; i++)
                if (tampon[i] == 0) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void BoutonRechercher_Click(object sender, RoutedEventArgs e) => Rechercher(ChampRecherche.Text);

    private void Fermer_Click(object sender, RoutedEventArgs e) => Masquer();

    private void ChampRecherche_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Rechercher(ChampRecherche.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Masquer();
            e.Handled = true;
        }
    }

    private void Resultat_DoubleClic(object sender, MouseButtonEventArgs e)
    {
        if (ListeResultats.SelectedItem is Resultat r)
            AppEvents.DemanderFichierALaLigne(r.Chemin, r.Ligne);
    }
}
