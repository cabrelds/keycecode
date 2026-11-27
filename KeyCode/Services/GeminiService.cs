using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace KeyCode.Services;

/// <summary>
/// Client asynchrone pour l'API Google Gemini avec découverte dynamique des modèles actifs et repli automatique.
/// </summary>
public static class GeminiService
{
    public sealed record MessageChat(string Role, string Texte);

    public static readonly string[] ModelesParDefaut =
    {
        "gemini-3.8-flash",
        "gemini-flash-latest",
        "gemini-3.7-flash",
        "gemini-3.8-pro"
    };

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(75) };

    private const string PromptSysteme =
        "Tu es l'assistant IA intégré à l'éditeur de code KeyCode (style VS Code, en C# WPF .NET 10). " +
        "Réponds en français de façon claire, structurée et concise. " +
        "Lorsque tu fournis du code, mets-le dans un bloc Markdown ```langage ... ``` propre et prêt à être inséré directement dans l'éditeur.";

    /// <summary>Interroge l'API Google AI pour lister les modèles Gemini compatibles generateContent.</summary>
    public static async Task<List<string>> ListerModelesDisponiblesAsync(
        string? cleApi = null,
        CancellationToken jeton = default)
    {
        var cle = (cleApi ?? SettingsService.CleApiGemini ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cle))
            return new List<string>(ModelesParDefaut);

        var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(cle)}";
        try
        {
            using var resp = await _http.GetAsync(url, jeton).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new List<string>(ModelesParDefaut);

            var corps = await resp.Content.ReadAsStringAsync(jeton).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(corps);
            if (!doc.RootElement.TryGetProperty("models", out var models))
                return new List<string>(ModelesParDefaut);

            var candidats = new List<string>();
            foreach (var m in models.EnumerateArray())
            {
                if (!m.TryGetProperty("name", out var nameEl)) continue;
                var nomComplet = nameEl.GetString() ?? "";
                var id = nomComplet.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                    ? nomComplet["models/".Length..]
                    : nomComplet;

                if (!id.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)) continue;
                if (id.Contains("embedding", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("tts", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("imagen", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("live", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("native-audio", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("robotics", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!m.TryGetProperty("supportedGenerationMethods", out var methods)) continue;
                var supporteChat = false;
                foreach (var meth in methods.EnumerateArray())
                {
                    if (string.Equals(meth.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase))
                    {
                        supporteChat = true;
                        break;
                    }
                }

                if (supporteChat)
                    candidats.Add(id);
            }

            if (candidats.Count == 0)
                return new List<string>(ModelesParDefaut);

            // Classe les meilleurs modèles récents Flash puis Pro en tête
            return candidats
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c.Contains("omni", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("preview", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("exp", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(c => c.Contains("flash", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Contains("latest", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return new List<string>(ModelesParDefaut);
        }
    }

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
                "Cliquez sur « Se connecter » (ou sur l'engrenage ⚙ en haut du panneau Gemini) pour coller votre clé Google AI Studio (https://aistudio.google.com/apikey).");
        }

        var modele = string.IsNullOrWhiteSpace(SettingsService.ModeleGemini)
            ? ModelesParDefaut[0]
            : SettingsService.ModeleGemini.Trim();

        var chargeJson = ConstruireChargeJson(historique, contexteSupplementaire);
        var (succes, codeHttp, texteReponse) = await ExecuterRequeteAsync(cle, modele, chargeJson, jeton).ConfigureAwait(false);

        // Si le modèle configuré est obsolète / indisponible pour cette clé, bascule automatiquement sur un modèle actif
        if (!succes && EstErreurModeleIndisponible(codeHttp, texteReponse))
        {
            var disponibles = await ListerModelesDisponiblesAsync(cle, jeton).ConfigureAwait(false);
            var deRepli = disponibles.FirstOrDefault(m =>
                !string.Equals(m, modele, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(deRepli))
            {
                var tentativeRepli = await ExecuterRequeteAsync(cle, deRepli, chargeJson, jeton).ConfigureAwait(false);
                if (tentativeRepli.Succes)
                {
                    SettingsService.ModeleGemini = deRepli;
                    SettingsService.Enregistrer();
                    return (true, $"*(Modèle mis à jour automatiquement vers `{deRepli}`)*\n\n{tentativeRepli.Reponse}");
                }
            }
        }

        // Modèle saturé ou sans quota : essaie un autre modèle sans modifier le réglage
        if (!succes && !jeton.IsCancellationRequested
            && (EstErreurTemporaire(codeHttp, texteReponse) || EstErreurQuota(texteReponse)))
        {
            var autres = await ListerModelesDisponiblesAsync(cle, jeton).ConfigureAwait(false);
            foreach (var autre in autres
                         .Where(m => !string.Equals(m, modele, StringComparison.OrdinalIgnoreCase))
                         .Take(3))
            {
                var reponseAutre = await ExecuterRequeteAsync(cle, autre, chargeJson, jeton).ConfigureAwait(false);
                if (reponseAutre.Succes)
                    return (true, $"*(`{modele}` indisponible : réponse donnée par `{autre}`)*\n\n{reponseAutre.Reponse}");
            }

            return (false, texteReponse + "\n\nAucun modèle n'a pu répondre. Choisissez un autre modèle avec le sélecteur du panneau, ou réessayez plus tard.");
        }

        return (succes, texteReponse);
    }

    /// <summary>
    /// Version en flux continu (SSE) : appelle surTexte à chaque morceau reçu.
    /// Pas de ConfigureAwait(false) ici : le rappel reste sur le thread de l'appelant (interface).
    /// En cas d'erreur HTTP, repli sur EnvoyerAsync (message d'erreur clair + changement de modèle automatique).
    /// </summary>
    public static async Task<(bool Succes, string Reponse)> EnvoyerFluxAsync(
        IReadOnlyList<MessageChat> historique,
        string? contexteSupplementaire,
        Action<string> surTexte,
        CancellationToken jeton = default)
    {
        var cle = (SettingsService.CleApiGemini ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cle))
            return await EnvoyerAsync(historique, contexteSupplementaire, jeton);

        var modele = string.IsNullOrWhiteSpace(SettingsService.ModeleGemini)
            ? ModelesParDefaut[0]
            : SettingsService.ModeleGemini.Trim();

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modele)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(cle)}";
        var chargeJson = ConstruireChargeJson(historique, contexteSupplementaire);
        var total = new StringBuilder();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(chargeJson, Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, jeton);
            if (!resp.IsSuccessStatusCode)
                return await EnvoyerAsync(historique, contexteSupplementaire, jeton);

            using var flux = await resp.Content.ReadAsStreamAsync(jeton);
            using var lecteur = new StreamReader(flux, Encoding.UTF8);

            string? ligne;
            while ((ligne = await lecteur.ReadLineAsync(jeton)) != null)
            {
                if (!ligne.StartsWith("data:", StringComparison.Ordinal)) continue;
                var json = ligne["data:".Length..].Trim();
                if (json.Length == 0) continue;

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("candidates", out var candidats) || candidats.GetArrayLength() == 0)
                    continue;
                if (!candidats[0].TryGetProperty("content", out var contenu) ||
                    !contenu.TryGetProperty("parts", out var parties))
                    continue;

                foreach (var partie in parties.EnumerateArray())
                {
                    if (partie.TryGetProperty("text", out var t) && t.GetString() is { Length: > 0 } texte)
                    {
                        total.Append(texte);
                        surTexte(texte);
                    }
                }
            }

            return total.Length > 0
                ? (true, total.ToString().Trim())
                : (false, "Gemini n'a renvoyé aucun contenu exploitable.");
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

    private static string ConstruireChargeJson(
        IReadOnlyList<MessageChat> historique,
        string? contexteSupplementaire)
    {
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

        return JsonSerializer.Serialize(charge);
    }

    private static async Task<(bool Succes, HttpStatusCode CodeHttp, string Reponse)> ExecuterRequeteAsync(
        string cle,
        string modele,
        string chargeJson,
        CancellationToken jeton)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modele)}:generateContent?key={Uri.EscapeDataString(cle)}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(chargeJson, Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req, jeton).ConfigureAwait(false);
            var corps = await resp.Content.ReadAsStringAsync(jeton).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(corps);
            if (!resp.IsSuccessStatusCode)
            {
                var errMsg = doc.RootElement.TryGetProperty("error", out var err) &&
                             err.TryGetProperty("message", out var m)
                    ? m.GetString() ?? $"Erreur HTTP {(int)resp.StatusCode}"
                    : $"Erreur HTTP {(int)resp.StatusCode}";
                return (false, resp.StatusCode, $"Erreur Gemini : {errMsg}");
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
                        return (true, resp.StatusCode, resultat);
                }
            }

            return (false, resp.StatusCode, "Gemini n'a renvoyé aucun contenu exploitable.");
        }
        catch (OperationCanceledException)
        {
            return (false, HttpStatusCode.RequestTimeout, "Requête annulée.");
        }
        catch (Exception ex)
        {
            return (false, 0, $"Impossible de joindre l'API Gemini : {ex.Message}");
        }
    }

    private static bool EstErreurQuota(string message) =>
        message.Contains("quota", StringComparison.OrdinalIgnoreCase)
        || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    private static bool EstErreurTemporaire(HttpStatusCode codeHttp, string message) =>
        !EstErreurQuota(message)
        && (codeHttp == 0
            || codeHttp == HttpStatusCode.TooManyRequests
            || codeHttp == HttpStatusCode.ServiceUnavailable
            || codeHttp == HttpStatusCode.GatewayTimeout
            || codeHttp == HttpStatusCode.RequestTimeout
            || message.Contains("high demand", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase));

    private static bool EstErreurModeleIndisponible(HttpStatusCode codeHttp, string message)
    {
        if (codeHttp == HttpStatusCode.NotFound) return true;
        return message.Contains("no longer available", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not supported for generateContent", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("deprecated", StringComparison.OrdinalIgnoreCase);
    }
}
