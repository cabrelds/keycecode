using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyCode.Services;
namespace KeyCode.Controls
{
    /// <summary>
    /// Recherche globale dans tous les fichiers texte du dossier racine.
    /// Les résultats s'affichent au format « fichier : ligne : texte ».
    /// </summary>
    public partial class RechercheFichiersControl : UserControl
    {
        private static readonly string[] Ignores = { "bin", "obj", ".git" };

        // Au-delà, on arrête pour ne pas figer l'interface
        private const int MaxResultats = 1000;

        // Fichiers plus gros que 2 Mo ignorés
        private const long TailleMax = 2 * 1024 * 1024;

        /// <summary>Un résultat de recherche : le texte affiché et la cible à ouvrir.</summary>
        private sealed record Resultat(string Chemin, int Ligne, string Affichage)
        {
            public override string ToString() => Affichage;
        }

        public RechercheFichiersControl()
        {
            InitializeComponent();
        }

        // ---------------------------------------------------------------
        // API publique
        // ---------------------------------------------------------------

        /// <summary>Affiche le panneau de recherche (le raccourci Ctrl+Shift+F est géré par cabrelds).</summary>
        public void Afficher()
        {
            Visibility = Visibility.Visible;
            ChampRecherche.Focus();
            ChampRecherche.SelectAll();
        }

        /// <summary>Masque le panneau de recherche.</summary>
        public void Masquer()
        {
            Visibility = Visibility.Collapsed;
        }

        /// <summary>Cherche le texte dans tous les fichiers texte de Contexte.DossierRacine.</summary>
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
                // Recherche hors du thread de l'interface
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

        // ---------------------------------------------------------------
        // Recherche
        // ---------------------------------------------------------------

        private static List<Resultat> Chercher(string racine, string texte)
        {
            var resultats = new List<Resultat>();

            foreach (var fichier in ListerFichiers(racine))
            {
                if (resultats.Count >= MaxResultats) break;
                if (!EstFichierTexte(fichier)) continue;

                try
                {
                    int numero = 0;
                    foreach (var ligne in File.ReadLines(fichier))
                    {
                        numero++;
                        if (ligne.Contains(texte, StringComparison.OrdinalIgnoreCase))
                        {
                            var relatif = Path.GetRelativePath(racine, fichier);
                            resultats.Add(new Resultat(fichier, numero,
                                relatif + " : " + numero + " : " + ligne.Trim()));

                            if (resultats.Count >= MaxResultats) break;
                        }
                    }
                }
                catch (IOException) { /* fichier verrouillé : on passe */ }
                catch (UnauthorizedAccessException) { /* accès refusé : on passe */ }
            }

            return resultats;
        }

        /// <summary>Parcours récursif en ignorant bin, obj et .git.</summary>
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

        /// <summary>Considère un fichier comme binaire s'il contient un octet nul dans ses premiers Ko.</summary>
        private static bool EstFichierTexte(string chemin)
        {
            try
            {
                var info = new FileInfo(chemin);
                if (info.Length > TailleMax) return false;

                using var flux = File.OpenRead(chemin);
                var tampon = new byte[4096];
                int lus = flux.Read(tampon, 0, tampon.Length);
                for (int i = 0; i < lus; i++)
                    if (tampon[i] == 0) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------------
        // Évènements de l'interface
        // ---------------------------------------------------------------

        private void BoutonRechercher_Click(object sender, RoutedEventArgs e)
        {
            Rechercher(ChampRecherche.Text);
        }

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

        // Clic sur un résultat : ouvre le fichier.
        // TODO : quand cabrelds aura ajouté AppEvents.DemanderFichierALaLigne,
        // remplacer l'appel par DemanderFichierALaLigne(r.Chemin, r.Ligne).
        private void Resultat_DoubleClic(object sender, MouseButtonEventArgs e)
        {
            if (ListeResultats.SelectedItem is Resultat r)
                AppEvents.DemanderFichierALaLigne(r.Chemin, r.Ligne);
        }
    }
}
