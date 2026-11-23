using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyCode.Services;

namespace KeyCode.Controls;

/// <summary>
/// Panneau latéral d'assistant IA Google Gemini intégré façon VS Code Copilot Chat.
/// </summary>
public partial class AssistantIAControl : UserControl
{
    private readonly List<GeminiService.MessageChat> _historique = new();
    private CancellationTokenSource? _jetonEnCours;
    private bool _abonne;

    private static readonly Regex _rxBlocCode = new(@"```([A-Za-z0-9#+\-]*)\r?\n([\s\S]*?)```", RegexOptions.Compiled);

    public event Action? DemandeMasquage;

    public AssistantIAControl()
    {
        InitializeComponent();
    }

    private void AssistantIAControl_Loaded(object sender, RoutedEventArgs e)
    {
        ChampCleApi.Text = SettingsService.CleApiGemini;
        SelectionnerModeleDansCombo(SettingsService.ModeleGemini);
        if (string.IsNullOrWhiteSpace(SettingsService.CleApiGemini))
            PanneauConfig.Visibility = Visibility.Visible;

        if (!_abonne)
        {
            AppEvents.AideIADemandee += SurAideDemandee;
            _abonne = true;
        }
    }

    private void AssistantIAControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_abonne)
        {
            AppEvents.AideIADemandee -= SurAideDemandee;
            _abonne = false;
        }
        _jetonEnCours?.Cancel();
    }

    private void SelectionnerModeleDansCombo(string modele)
    {
        foreach (ComboBoxItem item in ComboModele.Items)
        {
            if (string.Equals(item.Content?.ToString(), modele, StringComparison.OrdinalIgnoreCase))
            {
                ComboModele.SelectedItem = item;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(modele))
        {
            var ajoute = new ComboBoxItem { Content = modele };
            ComboModele.Items.Insert(0, ajoute);
            ComboModele.SelectedItem = ajoute;
            return;
        }

        if (ComboModele.Items.Count > 0)
            ComboModele.SelectedIndex = 0;
    }

    private async Task ChargerModelesEnLigneAsync(string? cleApi = null)
    {
        BoutonRafraichirModeles.IsEnabled = false;
        var ancienStatut = TexteStatut.Text;
        TexteStatut.Text = "Récupération des modèles Gemini disponibles...";
        try
        {
            var modeles = await GeminiService.ListerModelesDisponiblesAsync(cleApi);
            var cible = ComboModele.SelectedItem is ComboBoxItem ci
                ? ci.Content?.ToString()
                : SettingsService.ModeleGemini;

            ComboModele.Items.Clear();
            foreach (var m in modeles)
                ComboModele.Items.Add(new ComboBoxItem { Content = m });

            SelectionnerModeleDansCombo(string.IsNullOrWhiteSpace(cible) ? modeles[0] : cible);
            if (!modeles.Contains(SettingsService.ModeleGemini, StringComparer.OrdinalIgnoreCase) && modeles.Count > 0)
            {
                SettingsService.ModeleGemini = modeles[0];
                SelectionnerModeleDansCombo(modeles[0]);
            }
            TexteStatut.Text = $"{modeles.Count} modèle(s) Gemini chargé(s)";
        }
        catch (Exception)
        {
            TexteStatut.Text = ancienStatut;
        }
        finally
        {
            BoutonRafraichirModeles.IsEnabled = true;
        }
    }

    public void FocaliserPrompt()
    {
        ChampPrompt.Focus();
    }

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
        if (string.IsNullOrWhiteSpace(question)) return;

        _jetonEnCours?.Cancel();
        _jetonEnCours?.Dispose();
        _jetonEnCours = new CancellationTokenSource();
        var jeton = _jetonEnCours.Token;

        CarteBienvenue.Visibility = Visibility.Collapsed;
        AjouterCarteMessage("user", question);
        _historique.Add(new GeminiService.MessageChat("user", question));

        var contexteCode = ConstruireContexte();
        BoutonEnvoyer.IsEnabled = false;
        TexteStatut.Text = $"Gemini ({SettingsService.ModeleGemini}) réfléchit...";

        try
        {
            var (succes, reponse) = await GeminiService.EnvoyerAsync(_historique, contexteCode, jeton);
            if (jeton.IsCancellationRequested) return;

            if (succes)
            {
                _historique.Add(new GeminiService.MessageChat("model", reponse));
                SelectionnerModeleDansCombo(SettingsService.ModeleGemini);
            }

            AjouterCarteMessage(succes ? "model" : "error", reponse);
            TexteStatut.Text = succes ? "Réponse reçue" : "Configuration requise ou erreur";
            if (!succes && string.IsNullOrWhiteSpace(SettingsService.CleApiGemini))
                PanneauConfig.Visibility = Visibility.Visible;
        }
        finally
        {
            BoutonEnvoyer.IsEnabled = true;
        }
    }

    private string ConstruireContexte()
    {
        var sb = new StringBuilder();
        var editeur = Contexte.EditeurActif;
        var chemin = Contexte.CheminActif;

        if (ChkSelection.IsChecked == true && editeur != null && editeur.SelectionLength > 0)
        {
            sb.AppendLine("Sélection active :");
            sb.AppendLine("```");
            sb.AppendLine(editeur.SelectedText);
            sb.AppendLine("```");
        }

        if (ChkFichier.IsChecked == true && editeur != null)
        {
            var nom = string.IsNullOrWhiteSpace(chemin) ? "Sans titre" : Path.GetFileName(chemin);
            var texte = editeur.Text ?? "";
            if (texte.Length > 14000) texte = texte[..14000] + "\n... (tronqué)";
            sb.AppendLine($"Fichier actif ({nom}, curseur ligne {editeur.TextArea.Caret.Line}) :");
            sb.AppendLine("```");
            sb.AppendLine(texte);
            sb.AppendLine("```");
        }

        if (ChkErreurs.IsChecked == true)
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

    private void AjouterCarteMessage(string role, string texte)
    {
        var estUser = role == "user";
        var estErreur = role == "error";

        var conteneur = new Border
        {
            Background = estUser
                ? ThemeManager.Pinceau("ThemeSelection")
                : ThemeManager.Pinceau("ThemeFondTertiaire"),
            BorderBrush = estErreur
                ? ThemeManager.Pinceau("ThemeErreur")
                : ThemeManager.Pinceau("ThemeBordure"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(estUser ? 24 : 0, 0, estUser ? 0 : 12, 8)
        };

        var pile = new StackPanel();
        pile.Children.Add(new TextBlock
        {
            Text = estUser ? "Vous" : (estErreur ? "Alerte Gemini" : "✦ Gemini"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = estErreur ? ThemeManager.Pinceau("ThemeErreur") : ThemeManager.Pinceau("ThemeAccent"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        if (estUser || estErreur)
        {
            pile.Children.Add(new TextBox
            {
                Text = texte,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                Foreground = ThemeManager.Pinceau("ThemeTexte"),
                BorderThickness = new Thickness(0),
                FontSize = 12
            });
        }
        else
        {
            ConstruireContenuFormate(pile, texte);
        }

        conteneur.Child = pile;
        ListeMessages.Children.Add(conteneur);
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
            FontSize = 12,
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

    private async void Envoyer_Click(object sender, RoutedEventArgs e)
    {
        var q = ChampPrompt.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;
        ChampPrompt.Clear();
        await PoserQuestionAsync(q);
    }

    private async void ChampPrompt_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            var q = ChampPrompt.Text.Trim();
            if (string.IsNullOrEmpty(q)) return;
            ChampPrompt.Clear();
            await PoserQuestionAsync(q);
        }
    }

    private void BasculerConfig_Click(object sender, RoutedEventArgs e)
    {
        PanneauConfig.Visibility = PanneauConfig.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void SauvegarderConfig_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.CleApiGemini = ChampCleApi.Text.Trim();
        if (ComboModele.SelectedItem is ComboBoxItem item && item.Content is string m)
            SettingsService.ModeleGemini = m;
        SettingsService.Enregistrer();

        if (!string.IsNullOrWhiteSpace(SettingsService.CleApiGemini))
            await ChargerModelesEnLigneAsync(SettingsService.CleApiGemini);

        SettingsService.Enregistrer();
        PanneauConfig.Visibility = Visibility.Collapsed;
        TexteStatut.Text = $"Configuration enregistrée ({SettingsService.ModeleGemini})";
    }

    private async void RafraichirModeles_Click(object sender, RoutedEventArgs e)
    {
        await ChargerModelesEnLigneAsync(ChampCleApi.Text.Trim());
    }

    private void Effacer_Click(object sender, RoutedEventArgs e)
    {
        _historique.Clear();
        while (ListeMessages.Children.Count > 1)
            ListeMessages.Children.RemoveAt(ListeMessages.Children.Count - 1);
        CarteBienvenue.Visibility = Visibility.Visible;
        TexteStatut.Text = "Nouvelle conversation";
    }

    private void Fermer_Click(object sender, RoutedEventArgs e) => DemandeMasquage?.Invoke();

    private async void ActionExpliquer_Click(object sender, RoutedEventArgs e) =>
        await PoserQuestionAsync("Explique clairement ce code étape par étape.");

    private async void ActionCorriger_Click(object sender, RoutedEventArgs e) =>
        await PoserQuestionAsync("Identifie les bugs ou erreurs dans ce code (et dans les diagnostics) puis propose la correction complète.");

    private async void ActionOptimiser_Click(object sender, RoutedEventArgs e) =>
        await PoserQuestionAsync("Propose une version optimisée, propre et mieux structurée de ce code.");

    private async void ActionCommenter_Click(object sender, RoutedEventArgs e) =>
        await PoserQuestionAsync("Ajoute des commentaires et docstrings clairs en français à ce code.");
}
