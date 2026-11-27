using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KeyCode.Services;
using Microsoft.Win32;
using ShapePath = System.Windows.Shapes.Path;

namespace KeyCode.Controls;

/// <summary>
/// Panneau Gemini de la barre latérale, façon agent VS Code : conversation en flux continu,
/// @ pour mentionner un fichier du dossier, / pour les commandes, contexte du fichier actif.
/// </summary>
public partial class AssistantIAControl : UserControl
{
    public sealed record Suggestion(string Libelle, string Detail, string Valeur);
    private sealed record Conversation(string Titre, List<GeminiService.MessageChat> Messages);

    private static readonly (string Nom, string Detail, string Prompt)[] _commandes =
    {
        ("/expliquer", "Expliquer le code", "Explique clairement ce code étape par étape."),
        ("/corriger", "Corriger les erreurs", "Identifie les bugs ou erreurs dans ce code (et dans les diagnostics) puis propose la correction complète."),
        ("/optimiser", "Optimiser le code", "Propose une version optimisée, propre et mieux structurée de ce code."),
        ("/commenter", "Ajouter des commentaires", "Ajoute des commentaires et docstrings clairs en français à ce code."),
        ("/effacer", "Nouvelle conversation", "")
    };

    private static readonly Regex _rxBlocCode = new(@"```([A-Za-z0-9#+\-]*)\r?\n([\s\S]*?)```", RegexOptions.Compiled);

    private readonly List<GeminiService.MessageChat> _historique = new();
    private readonly List<Conversation> _archives = new();
    private readonly Dictionary<string, string> _mentions = new(StringComparer.OrdinalIgnoreCase); // "@nom" -> chemin
    private List<string> _modeles = new(GeminiService.ModelesParDefaut);

    private CancellationTokenSource? _jetonEnCours;
    private int _generation;
    private bool _enCours;
    private bool _modeAgent;
    private bool _ctxFichier = true;
    private bool _ctxSelection = true;
    private bool _ctxErreurs = true;
    private bool _suggestionsFichiers;
    private int _debutJeton;

    private string? _cacheRacine;
    private List<string> _cacheFichiers = new();
    private DateTime _cacheHeure = DateTime.MinValue;

    public AssistantIAControl()
    {
        InitializeComponent();
        AppEvents.AideIADemandee += SurAideDemandee;
    }

    private void AssistantIAControl_Loaded(object sender, RoutedEventArgs e)
    {
        ChampCleApi.Text = SettingsService.CleApiGemini;
        AssurerModeleDansListe();
        MettreAJourModele();
        MettreAJourEtat();
    }

    private void AssistantIAControl_Unloaded(object sender, RoutedEventArgs e) => _jetonEnCours?.Cancel();

    public void FocaliserPrompt() => ChampPrompt.Focus();

    // ------------------------------------------------------------------
    // État de l'interface
    // ------------------------------------------------------------------

    private void MettreAJourEtat()
    {
        var connecte = !string.IsNullOrWhiteSpace(SettingsService.CleApiGemini);
        BlocConnexion.Visibility = connecte ? Visibility.Collapsed : Visibility.Visible;
        BlocPret.Visibility = connecte ? Visibility.Visible : Visibility.Collapsed;

        var vide = ListeMessages.Children.Count == 0;
        VueAccueil.Visibility = vide ? Visibility.Visible : Visibility.Collapsed;
        DefileurChat.Visibility = vide ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MettreAJourModele() => TexteModele.Text = SettingsService.ModeleGemini;

    private void AssurerModeleDansListe()
    {
        var m = SettingsService.ModeleGemini;
        if (!string.IsNullOrWhiteSpace(m) && !_modeles.Contains(m, StringComparer.OrdinalIgnoreCase))
            _modeles.Insert(0, m);
    }

    private void DefinirEnCours(bool valeur)
    {
        _enCours = valeur;
        IconeEnvoyer.Visibility = valeur ? Visibility.Collapsed : Visibility.Visible;
        IconeStop.Visibility = valeur ? Visibility.Visible : Visibility.Collapsed;
        BoutonEnvoyer.ToolTip = valeur ? "Arrêter la réponse" : "Envoyer (Entrée)";
        BoutonEnvoyer.Tag = valeur || ChampPrompt.Text.Trim().Length > 0 ? "Pret" : null;
    }

    private static ShapePath CreerLogo(double taille) => new()
    {
        Data = (Geometry)Application.Current.FindResource("GeminiLogoGeometry"),
        Fill = (Brush)Application.Current.FindResource("GeminiLogoBrush"),
        Width = taille,
        Height = taille,
        Stretch = Stretch.Uniform
    };

    // ------------------------------------------------------------------
    // Paramètres : clé API et modèles
    // ------------------------------------------------------------------

    private void SeConnecter_Click(object sender, RoutedEventArgs e) => OuvrirConfig();

    private void BasculerConfig_Click(object sender, RoutedEventArgs e)
    {
        if (PanneauConfig.Visibility == Visibility.Visible)
            PanneauConfig.Visibility = Visibility.Collapsed;
        else
            OuvrirConfig();
    }

    private void OuvrirConfig()
    {
        ChampCleApi.Text = SettingsService.CleApiGemini;
        PanneauConfig.Visibility = Visibility.Visible;
        ChampCleApi.Focus();
        ChampCleApi.CaretIndex = ChampCleApi.Text.Length;
    }

    private void ObtenirCle_Click(object sender, RoutedEventArgs e) =>
        ExtensionUi.OuvrirUrl("https://aistudio.google.com/apikey");

    private async void SauvegarderConfig_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.CleApiGemini = ChampCleApi.Text.Trim();
        SettingsService.Enregistrer();
        MettreAJourEtat();

        if (!string.IsNullOrWhiteSpace(SettingsService.CleApiGemini))
            await ChargerModelesEnLigneAsync(SettingsService.CleApiGemini);

        SettingsService.Enregistrer();
        PanneauConfig.Visibility = Visibility.Collapsed;
        FocaliserPrompt();
    }

    private async void RafraichirModeles_Click(object sender, RoutedEventArgs e) =>
        await ChargerModelesEnLigneAsync(ChampCleApi.Text.Trim());

    private async Task ChargerModelesEnLigneAsync(string? cleApi)
    {
        if (string.IsNullOrWhiteSpace(cleApi ?? SettingsService.CleApiGemini))
        {
            TexteConfig.Text = "Saisissez d'abord une clé API.";
            return;
        }

        TexteConfig.Text = "Récupération des modèles disponibles...";
        try
        {
            var modeles = await GeminiService.ListerModelesDisponiblesAsync(cleApi);
            if (modeles.Count > 0)
            {
                _modeles = modeles;
                if (!modeles.Contains(SettingsService.ModeleGemini, StringComparer.OrdinalIgnoreCase))
                    SettingsService.ModeleGemini = modeles[0];
                MettreAJourModele();
                TexteConfig.Text = $"{modeles.Count} modèle(s) disponible(s).";
            }
        }
        catch (Exception)
        {
            TexteConfig.Text = "Impossible de récupérer la liste des modèles.";
        }
    }

    // ------------------------------------------------------------------
    // Menus de la zone de saisie et de la barre du bas
    // ------------------------------------------------------------------

    private static void OuvrirMenu(ContextMenu menu, FrameworkElement cible, PlacementMode placement = PlacementMode.Top)
    {
        menu.PlacementTarget = cible;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    private static MenuItem CreerCase(string titre, bool etat, Action<bool> maj)
    {
        var item = new MenuItem { Header = titre, IsCheckable = true, IsChecked = etat };
        item.Click += (_, _) => maj(item.IsChecked);
        return item;
    }

    private void Modele_Click(object sender, RoutedEventArgs e)
    {
        AssurerModeleDansListe();
        var menu = new ContextMenu();
        foreach (var m in _modeles)
        {
            var nom = m;
            var item = new MenuItem
            {
                Header = nom.Replace("_", "__"),
                IsCheckable = true,
                IsChecked = string.Equals(nom, SettingsService.ModeleGemini, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                SettingsService.ModeleGemini = nom;
                SettingsService.Enregistrer();
                MettreAJourModele();
            };
            menu.Items.Add(item);
        }
        OuvrirMenu(menu, BoutonModele);
    }

    private void Plus_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var joindre = new MenuItem { Header = "Joindre un fichier..." };
        joindre.Click += (_, _) => JoindreFichier();
        var mention = new MenuItem { Header = "Mentionner un fichier du dossier (@)" };
        mention.Click += (_, _) => InsererDansPrompt("@");
        menu.Items.Add(joindre);
        menu.Items.Add(mention);
        OuvrirMenu(menu, (FrameworkElement)sender);
    }

    private void Agent_Click(object sender, RoutedEventArgs e)
    {
        _modeAgent = !_modeAgent;
        BoutonAgent.Tag = _modeAgent ? "Actif" : null;
        BoutonAgent.ToolTip = _modeAgent
            ? "Mode Agent activé : Gemini renvoie les modifications complètes, un bloc par fichier"
            : "Mode Agent (désactivé) : Gemini renvoie les modifications complètes, un bloc par fichier";
    }

    private void Contexte_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreerCase("Fichier actif", _ctxFichier, v => _ctxFichier = v));
        menu.Items.Add(CreerCase("Sélection", _ctxSelection, v => _ctxSelection = v));
        menu.Items.Add(CreerCase("Erreurs et sortie du terminal", _ctxErreurs, v => _ctxErreurs = v));
        OuvrirMenu(menu, (FrameworkElement)sender);
    }

    private void Actions_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var c in _commandes.Where(c => c.Nom != "/effacer"))
        {
            var cmd = c;
            var item = new MenuItem { Header = cmd.Detail, InputGestureText = cmd.Nom };
            item.Click += async (_, _) => await PoserQuestionAsync(cmd.Prompt);
            menu.Items.Add(item);
        }
        OuvrirMenu(menu, (FrameworkElement)sender);
    }

    private void Outils_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var modeles = new MenuItem { Header = "Actualiser la liste des modèles" };
        modeles.Click += async (_, _) => await ChargerModelesEnLigneAsync(null);
        var cle = new MenuItem { Header = "Configurer la clé API..." };
        cle.Click += (_, _) => OuvrirConfig();
        var site = new MenuItem { Header = "Obtenir une clé (Google AI Studio)" };
        site.Click += (_, _) => ExtensionUi.OuvrirUrl("https://aistudio.google.com/apikey");
        menu.Items.Add(modeles);
        menu.Items.Add(cle);
        menu.Items.Add(site);
        OuvrirMenu(menu, (FrameworkElement)sender);
    }

    // ------------------------------------------------------------------
    // Conversations : nouvelle, historique, restauration
    // ------------------------------------------------------------------

    private void Nouvelle_Click(object sender, RoutedEventArgs e) => NouvelleConversation();

    private void Historique_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        if (_archives.Count == 0)
            menu.Items.Add(new MenuItem { Header = "(aucune conversation précédente)", IsEnabled = false });

        foreach (var c in Enumerable.Reverse(_archives).ToList())
        {
            var conv = c;
            var item = new MenuItem { Header = conv.Titre.Replace("_", "__") };
            item.Click += (_, _) => RestaurerConversation(conv);
            menu.Items.Add(item);
        }
        OuvrirMenu(menu, (FrameworkElement)sender, PlacementMode.Bottom);
    }

    private void ArchiverConversationCourante()
    {
        if (_historique.Count == 0) return;
        var premier = _historique[0].Texte.Replace('\n', ' ').Trim();
        var titre = premier.Length > 48 ? premier[..48] + "…" : premier;
        _archives.Add(new Conversation(titre, new List<GeminiService.MessageChat>(_historique)));
        if (_archives.Count > 20) _archives.RemoveAt(0);
    }

    private void ArreterEtVider()
    {
        _generation++;
        _jetonEnCours?.Cancel();
        DefinirEnCours(false);
        _historique.Clear();
        ListeMessages.Children.Clear();
    }

    private void NouvelleConversation()
    {
        ArchiverConversationCourante();
        ArreterEtVider();
        MettreAJourEtat();
        FocaliserPrompt();
    }

    private void RestaurerConversation(Conversation conv)
    {
        ArchiverConversationCourante();
        _archives.Remove(conv);
        ArreterEtVider();
        foreach (var m in conv.Messages)
        {
            _historique.Add(m);
            AjouterCarteMessage(m.Role == "user" ? "user" : "model", m.Texte);
        }
        MettreAJourEtat();
    }

    // ------------------------------------------------------------------
    // Saisie : @ fichiers, / commandes, Entrée pour envoyer
    // ------------------------------------------------------------------

    private void ChampPrompt_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = ChampPrompt.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        BoutonEnvoyer.Tag = _enCours || ChampPrompt.Text.Trim().Length > 0 ? "Pret" : null;
        ActualiserSuggestions();
    }

    private void ActualiserSuggestions()
    {
        var texte = ChampPrompt.Text;
        var caret = Math.Min(ChampPrompt.CaretIndex, texte.Length);
        var debut = caret;
        while (debut > 0 && !char.IsWhiteSpace(texte[debut - 1])) debut--;
        var jeton = texte[debut..caret];

        if (jeton.StartsWith('@'))
        {
            _debutJeton = debut;
            AfficherSuggestionsFichiers(jeton[1..]);
        }
        else if (jeton.StartsWith('/') && debut == 0)
        {
            _debutJeton = 0;
            AfficherSuggestionsCommandes(jeton);
        }
        else
        {
            FermerSuggestions();
        }
    }

    private void AfficherSuggestionsFichiers(string filtre)
    {
        var racine = Contexte.DossierRacine;
        if (string.IsNullOrEmpty(racine) || !Directory.Exists(racine))
        {
            FermerSuggestions();
            return;
        }

        _suggestionsFichiers = true;
        var items = ListerFichiersProjet(racine)
            .Where(c => Path.GetFileName(c).Contains(filtre, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => !Path.GetFileName(c).StartsWith(filtre, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Length)
            .Take(8)
            .Select(c =>
            {
                var rel = Path.GetRelativePath(racine, Path.GetDirectoryName(c) ?? racine);
                return new Suggestion(Path.GetFileName(c), rel == "." ? "" : rel, c);
            })
            .ToList();
        OuvrirSuggestions(items);
    }

    private void AfficherSuggestionsCommandes(string jeton)
    {
        _suggestionsFichiers = false;
        var items = _commandes
            .Where(c => c.Nom.StartsWith(jeton, StringComparison.OrdinalIgnoreCase))
            .Select(c => new Suggestion(c.Nom, c.Detail, c.Nom))
            .ToList();
        OuvrirSuggestions(items);
    }

    private void OuvrirSuggestions(List<Suggestion> items)
    {
        if (items.Count == 0)
        {
            FermerSuggestions();
            return;
        }
        ListeSuggestions.ItemsSource = items;
        ListeSuggestions.SelectedIndex = 0;
        ListeSuggestions.Width = Math.Max(220, ChampPrompt.ActualWidth);
        PopupSuggestions.IsOpen = true;
    }

    private void FermerSuggestions() => PopupSuggestions.IsOpen = false;

    private void ValiderSuggestion()
    {
        if (ListeSuggestions.SelectedItem is not Suggestion s)
        {
            FermerSuggestions();
            return;
        }

        var texte = ChampPrompt.Text;
        var fin = Math.Min(ChampPrompt.CaretIndex, texte.Length);
        FermerSuggestions();

        string insertion;
        if (_suggestionsFichiers)
        {
            _mentions["@" + s.Libelle] = s.Valeur;
            insertion = "@" + s.Libelle + " ";
        }
        else
        {
            insertion = s.Valeur + " ";
        }

        ChampPrompt.Text = texte[.._debutJeton] + insertion + texte[fin..];
        ChampPrompt.CaretIndex = _debutJeton + insertion.Length;
        ChampPrompt.Focus();
    }

    private void Suggestion_Clic(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ListeSuggestions, source) is ListBoxItem)
        {
            ValiderSuggestion();
        }
    }

    private List<string> ListerFichiersProjet(string racine)
    {
        if (_cacheRacine == racine && DateTime.UtcNow - _cacheHeure < TimeSpan.FromSeconds(15))
            return _cacheFichiers;

        var resultat = new List<string>();
        var ignores = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bin", "obj", ".git", ".vs", "node_modules", "packages" };
        var pile = new Stack<string>();
        pile.Push(racine);

        while (pile.Count > 0 && resultat.Count < 3000)
        {
            var dossier = pile.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dossier))
                {
                    resultat.Add(f);
                    if (resultat.Count >= 3000) break;
                }
                foreach (var d in Directory.EnumerateDirectories(dossier))
                {
                    if (!ignores.Contains(Path.GetFileName(d))) pile.Push(d);
                }
            }
            catch (Exception)
            {
                // Dossier inaccessible : ignoré
            }
        }

        _cacheRacine = racine;
        _cacheFichiers = resultat;
        _cacheHeure = DateTime.UtcNow;
        return resultat;
    }

    private void JoindreFichier()
    {
        var dlg = new OpenFileDialog { Title = "Joindre un fichier à Gemini" };
        if (dlg.ShowDialog() != true) return;
        var nom = "@" + Path.GetFileName(dlg.FileName);
        _mentions[nom] = dlg.FileName;
        InsererDansPrompt(nom + " ");
    }

    private void InsererDansPrompt(string texte)
    {
        var i = Math.Min(ChampPrompt.CaretIndex, ChampPrompt.Text.Length);
        if (i > 0 && !char.IsWhiteSpace(ChampPrompt.Text[i - 1])) texte = " " + texte;
        ChampPrompt.Text = ChampPrompt.Text.Insert(i, texte);
        ChampPrompt.CaretIndex = i + texte.Length;
        ChampPrompt.Focus();
        ActualiserSuggestions();
    }

    private async void ChampPrompt_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PopupSuggestions.IsOpen)
        {
            switch (e.Key)
            {
                case Key.Down:
                    ListeSuggestions.SelectedIndex = Math.Min(ListeSuggestions.Items.Count - 1, ListeSuggestions.SelectedIndex + 1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    ListeSuggestions.SelectedIndex = Math.Max(0, ListeSuggestions.SelectedIndex - 1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                case Key.Tab:
                    ValiderSuggestion();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    FermerSuggestions();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (!_enCours) await EnvoyerPromptAsync();
        }
    }

    private async void Envoyer_Click(object sender, RoutedEventArgs e)
    {
        if (_enCours)
        {
            _jetonEnCours?.Cancel();
            return;
        }
        await EnvoyerPromptAsync();
    }

    private async Task EnvoyerPromptAsync()
    {
        var q = ChampPrompt.Text.Trim();
        if (q.Length == 0) return;

        if (q.StartsWith('/'))
        {
            var espace = q.IndexOf(' ');
            var mot = espace < 0 ? q : q[..espace];
            var reste = espace < 0 ? "" : q[(espace + 1)..].Trim();
            var cmd = _commandes.FirstOrDefault(c => c.Nom.Equals(mot, StringComparison.OrdinalIgnoreCase));
            if (cmd.Nom != null)
            {
                ChampPrompt.Clear();
                FermerSuggestions();
                if (cmd.Nom == "/effacer")
                {
                    NouvelleConversation();
                    return;
                }
                await PoserQuestionAsync(reste.Length == 0 ? cmd.Prompt : cmd.Prompt + " " + reste);
                return;
            }
        }

        ChampPrompt.Clear();
        FermerSuggestions();
        await PoserQuestionAsync(q);
    }

    // ------------------------------------------------------------------
    // Conversation en flux continu
    // ------------------------------------------------------------------

    private void SurAideDemandee(string messageErreur, string? chemin, int? ligne)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            var contexteMsg = ligne.HasValue
                ? $"Aide-moi sur ce point à la ligne {ligne.Value} de {Path.GetFileName(chemin ?? "ce fichier")} :\n{messageErreur}"
                : messageErreur;
            await PoserQuestionAsync(contexteMsg);
        });
    }

    private async Task PoserQuestionAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || _enCours) return;

        var generation = _generation;
        _jetonEnCours?.Dispose();
        var cts = new CancellationTokenSource();
        _jetonEnCours = cts;
        var jeton = cts.Token;

        DefinirEnCours(true);
        var contexte = ConstruireContexte(question);
        AjouterCarteMessage("user", question);
        _historique.Add(new GeminiService.MessageChat("user", question));

        var indicateur = CreerIndicateur();
        ListeMessages.Children.Add(indicateur);
        MettreAJourEtat();
        DefileurChat.ScrollToEnd();

        Border? carte = null;
        StackPanel? pile = null;
        TextBlock? flux = null;
        var cumul = new StringBuilder();

        try
        {
            // Le rappel s'exécute sur le thread de l'interface (pas de ConfigureAwait(false) dans EnvoyerFluxAsync)
            var (succes, reponse) = await GeminiService.EnvoyerFluxAsync(_historique, contexte, morceau =>
            {
                if (jeton.IsCancellationRequested || generation != _generation) return;
                if (carte == null)
                {
                    ListeMessages.Children.Remove(indicateur);
                    carte = CreerCarte("model", out var p);
                    pile = p;
                    flux = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        Foreground = ThemeManager.Pinceau("ThemeTexte")
                    };
                    pile.Children.Add(flux);
                    ListeMessages.Children.Add(carte);
                }
                cumul.Append(morceau);
                flux!.Text = cumul.ToString();
                DefileurChat.ScrollToEnd();
            }, jeton);

            if (generation != _generation) return;
            ListeMessages.Children.Remove(indicateur);

            if (jeton.IsCancellationRequested)
            {
                if (cumul.Length > 0)
                {
                    _historique.Add(new GeminiService.MessageChat("model", cumul.ToString()));
                    if (pile != null && flux != null)
                    {
                        pile.Children.Remove(flux);
                        ConstruireContenuFormate(pile, cumul.ToString());
                    }
                }
                else
                {
                    RetirerDernierMessageUtilisateur();
                }
                AjouterNote("Réponse interrompue.");
                return;
            }

            if (succes)
            {
                _historique.Add(new GeminiService.MessageChat("model", reponse));
                if (carte == null || pile == null)
                {
                    carte = CreerCarte("model", out var p);
                    pile = p;
                    ListeMessages.Children.Add(carte);
                }
                else if (flux != null)
                {
                    pile.Children.Remove(flux);
                }
                ConstruireContenuFormate(pile, reponse);
                AssurerModeleDansListe();
                MettreAJourModele();
            }
            else
            {
                RetirerDernierMessageUtilisateur();
                if (carte != null) ListeMessages.Children.Remove(carte);
                AjouterCarteMessage("error", reponse);
                if (string.IsNullOrWhiteSpace(SettingsService.CleApiGemini)) OuvrirConfig();
            }
        }
        finally
        {
            if (generation == _generation)
            {
                ListeMessages.Children.Remove(indicateur);
                DefinirEnCours(false);
                MettreAJourEtat();
                DefileurChat.ScrollToEnd();
            }
        }
    }

    private void RetirerDernierMessageUtilisateur()
    {
        if (_historique.Count > 0 && _historique[^1].Role == "user")
            _historique.RemoveAt(_historique.Count - 1);
    }

    private static FrameworkElement CreerIndicateur()
    {
        var ligne = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var logo = CreerLogo(14);
        var rotation = new RotateTransform();
        logo.RenderTransformOrigin = new Point(0.5, 0.5);
        logo.RenderTransform = rotation;
        rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        ligne.Children.Add(logo);
        ligne.Children.Add(new TextBlock
        {
            Text = "Gemini réfléchit…",
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = ThemeManager.Pinceau("ThemeTexteSecondaire")
        });
        return ligne;
    }

    private void AjouterNote(string texte) =>
        ListeMessages.Children.Add(new TextBlock
        {
            Text = texte,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = ThemeManager.Pinceau("ThemeTexteSecondaire")
        });

    // ------------------------------------------------------------------
    // Contexte envoyé à Gemini
    // ------------------------------------------------------------------

    private string ConstruireContexte(string question)
    {
        var sb = new StringBuilder();
        var editeur = Contexte.EditeurActif;
        var chemin = Contexte.CheminActif;

        if (_modeAgent)
        {
            sb.AppendLine("Mode agent activé : réponds avec les modifications complètes à appliquer, " +
                          "un bloc de code par fichier, précédé du nom du fichier.");
        }

        foreach (var (nom, cheminMention) in _mentions)
        {
            if (!question.Contains(nom, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var contenu = File.ReadAllText(cheminMention);
                if (contenu.Length > 12000) contenu = contenu[..12000] + "\n... (tronqué)";
                sb.AppendLine($"Fichier mentionné ({nom[1..]}) :");
                sb.AppendLine("```");
                sb.AppendLine(contenu);
                sb.AppendLine("```");
            }
            catch (Exception)
            {
                // Fichier illisible : ignoré
            }
        }

        if (_ctxSelection && editeur != null && editeur.SelectionLength > 0)
        {
            sb.AppendLine("Sélection active :");
            sb.AppendLine("```");
            sb.AppendLine(editeur.SelectedText);
            sb.AppendLine("```");
        }

        if (_ctxFichier && editeur != null)
        {
            var nom = string.IsNullOrWhiteSpace(chemin) ? "Sans titre" : Path.GetFileName(chemin);
            var texte = editeur.Text ?? "";
            if (texte.Length > 14000) texte = texte[..14000] + "\n... (tronqué)";
            sb.AppendLine($"Fichier actif ({nom}, curseur ligne {editeur.TextArea.Caret.Line}) :");
            sb.AppendLine("```");
            sb.AppendLine(texte);
            sb.AppendLine("```");
        }

        if (_ctxErreurs)
        {
            var diags = DiagnosticService.ObtenirTous();
            if (diags.Count > 0)
            {
                sb.AppendLine("Diagnostics / Erreurs détectées :");
                foreach (var d in diags.Take(12))
                    sb.AppendLine($"- [{d.Severite}] {Path.GetFileName(d.Chemin)} L{d.Ligne}: {d.Message}");
            }

            if (!string.IsNullOrWhiteSpace(DiagnosticService.DernierJournalExecution))
            {
                sb.AppendLine("Dernière sortie du terminal d'exécution :");
                sb.AppendLine("```");
                sb.AppendLine(DiagnosticService.DernierJournalExecution.Trim());
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Affichage des messages
    // ------------------------------------------------------------------

    private static Border CreerCarte(string role, out StackPanel pile)
    {
        var estUser = role == "user";
        var estErreur = role == "error";
        var avecCadre = estUser || estErreur;
        pile = new StackPanel();

        if (!estUser)
        {
            var entete = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            if (estErreur)
            {
                entete.Children.Add(new TextBlock
                {
                    Text = "⚠ Erreur Gemini",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = ThemeManager.Pinceau("ThemeErreur")
                });
            }
            else
            {
                entete.Children.Add(CreerLogo(13));
                entete.Children.Add(new TextBlock
                {
                    Text = "Gemini",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = ThemeManager.Pinceau("ThemeTexteSecondaire")
                });
            }
            pile.Children.Add(entete);
        }

        return new Border
        {
            Background = estUser ? ThemeManager.Pinceau("ThemeFondTertiaire") : Brushes.Transparent,
            BorderBrush = estErreur ? ThemeManager.Pinceau("ThemeErreur") : ThemeManager.Pinceau("ThemeBordure"),
            BorderThickness = new Thickness(avecCadre ? 1 : 0),
            CornerRadius = new CornerRadius(8),
            Padding = avecCadre ? new Thickness(10, 8, 10, 8) : new Thickness(2, 0, 2, 0),
            Margin = new Thickness(0, 0, 0, 12),
            Child = pile
        };
    }

    private void AjouterCarteMessage(string role, string texte)
    {
        var carte = CreerCarte(role, out var pile);
        if (role == "model")
            ConstruireContenuFormate(pile, texte);
        else
            AjouterTexteProse(pile, texte);

        ListeMessages.Children.Add(carte);
        DefileurChat.ScrollToEnd();
    }

    private void ConstruireContenuFormate(StackPanel pile, string texte)
    {
        var dernierIndex = 0;
        foreach (Match m in _rxBlocCode.Matches(texte))
        {
            if (m.Index > dernierIndex)
            {
                var prose = texte[dernierIndex..m.Index].Trim();
                if (!string.IsNullOrEmpty(prose))
                    AjouterTexteProse(pile, prose);
            }

            var code = m.Groups[2].Value.TrimEnd('\r', '\n');
            AjouterBlocCode(pile, m.Groups[1].Value, code);
            dernierIndex = m.Index + m.Length;
        }

        if (dernierIndex < texte.Length)
        {
            var reste = texte[dernierIndex..].Trim();
            if (!string.IsNullOrEmpty(reste))
                AjouterTexteProse(pile, reste);
        }
    }

    private static void AjouterTexteProse(StackPanel pile, string prose)
    {
        pile.Children.Add(new TextBox
        {
            Text = prose,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            Foreground = ThemeManager.Pinceau("ThemeTexte"),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6)
        });
    }

    private static void AjouterBlocCode(StackPanel pile, string langage, string code)
    {
        var carte = new Border
        {
            Background = ThemeManager.Pinceau("ThemeFond"),
            BorderBrush = ThemeManager.Pinceau("ThemeBordure"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2, 0, 6)
        };

        var blocPile = new StackPanel();

        var barre = new DockPanel
        {
            Background = ThemeManager.Pinceau("ThemeFondSecondaire"),
            LastChildFill = false
        };
        barre.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(langage) ? "code" : langage,
            Foreground = ThemeManager.Pinceau("ThemeTexteSecondaire"),
            FontSize = 10,
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        });

        var btnRemplacer = CreerMiniBouton("Remplacer");
        var btnInserer = CreerMiniBouton("Insérer");
        var btnCopier = CreerMiniBouton("Copier");

        DockPanel.SetDock(btnRemplacer, Dock.Right);
        DockPanel.SetDock(btnInserer, Dock.Right);
        DockPanel.SetDock(btnCopier, Dock.Right);

        barre.Children.Add(btnRemplacer);
        barre.Children.Add(btnInserer);
        barre.Children.Add(btnCopier);

        btnCopier.Click += (_, _) => Clipboard.SetText(code);
        btnInserer.Click += (_, _) =>
        {
            var ed = Contexte.EditeurActif;
            if (ed == null) return;
            ed.Document.Insert(ed.CaretOffset, code);
            ed.TextArea.Focus();
        };
        btnRemplacer.Click += (_, _) =>
        {
            var ed = Contexte.EditeurActif;
            if (ed == null) return;
            if (ed.SelectionLength > 0)
                ed.Document.Replace(ed.SelectionStart, ed.SelectionLength, code);
            else
                ed.Document.Text = code;
            ed.TextArea.Focus();
        };

        blocPile.Children.Add(barre);
        blocPile.Children.Add(new TextBox
        {
            Text = code,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Background = Brushes.Transparent,
            Foreground = ThemeManager.Pinceau("ThemeTexte"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        carte.Child = blocPile;
        pile.Children.Add(carte);
    }

    private static Button CreerMiniBouton(string texte) => new()
    {
        Content = texte,
        FontSize = 10,
        Padding = new Thickness(6, 2, 6, 2),
        Margin = new Thickness(2),
        Cursor = Cursors.Hand,
        Focusable = false,
        Style = (Style)Application.Current.FindResource("BoutonSecondaireVsCode")
    };
}