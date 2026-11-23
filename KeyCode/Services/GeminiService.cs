using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace KeyCode.Services;

/// <summary>
/// Client asynchrone pour l'API Google Gemini (modèles gemini-2.5-flash, gemini-2.5-pro, etc.).
/// </summary>
public static class GeminiService
{
    public sealed record MessageChat(string Role, string Texte);

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(75) };

    private const string PromptSysteme =
        "Tu es l'assistant IA intégré à l'éditeur de code KeyCode (style VS Code, en C# WPF .NET 10). " +
        "Réponds en français de façon claire, structurée et concise. " +
        "Lorsque tu fournis du code, mets-le dans un bloc Markdown ```langage ... ``` propre et prêt à être inséré directement dans l'éditeur.";

    public static async Task<(bool Succes, string Reponse)> EnvoyerAsync(
        IReadOnlyList<MessageChat> historique,
        string? contexteSupplementaire,
        CancellationToken jeton = default)
    {
        var cle = (SettingsService.CleApiGemini ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cle))
        {
            return (false,
                "Aucune clé API Gemini n'est configurée.\n" +
                "Cliquez sur « ⚙ Clé API » ci-dessus pour coller votre clé Google AI Studio (https://aistudio.google.com/apikey).");
        }

        var modele = string.IsNullOrWhiteSpace(SettingsService.ModeleGemini)
            ? "gemini-2.5-flash"
            : SettingsService.ModeleGemini.Trim();

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modele)}:generateContent?key={Uri.EscapeDataString(cle)}";

        var contents = new List<object>();
        for (var i = 0; i < historique.Count; i++)
        {
            var msg = historique[i];
            var texte = msg.Texte;
            // Injecte le contexte du code uniquement sur le dernier message utilisateur
            if (i == historique.Count - 1 && msg.Role == "user" && !string.IsNullOrWhiteSpace(contexteSupplementaire))
                texte = $"{contexteSupplementaire}\n\n---\nDemande de l'utilisateur :\n{texte}";

            contents.Add(new
            {
                role = msg.Role,
                parts = new[] { new { text = texte } }
            });
        }

        var charge = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = PromptSysteme } }
            },
            contents,
            generationConfig = new
            {
                temperature = 0.35
            }
        };

        try
        {
            var json = JsonSerializer.Serialize(charge);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req, jeton).ConfigureAwait(false);
            var corps = await resp.Content.ReadAsStringAsync(jeton).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(corps);
            if (!resp.IsSuccessStatusCode)
            {
                var errMsg = doc.RootElement.TryGetProperty("error", out var err) &&
                             err.TryGetProperty("message", out var m)
                    ? m.GetString()
                    : $"Erreur HTTP {(int)resp.StatusCode}";
                return (false, $"Erreur Gemini : {errMsg}");
            }

            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var premier = candidates[0];
                if (premier.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts))
                {
                    var sb = new StringBuilder();
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var t))
                            sb.Append(t.GetString());
                    }
                    var resultat = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(resultat))
                        return (true, resultat);
                }
            }

            return (false, "Gemini n'a renvoyé aucun contenu exploitable.");
        }
        catch (OperationCanceledException)
        {
            return (false, "Requête annulée.");
        }
        catch (Exception ex)
        {
            return (false, $"Impossible de joindre l'API Gemini : {ex.Message}");
        }
    }
}
