using System.IO;
using System.Net;
using System.Text;

namespace KeyCode.Services;

/// <summary>
/// Serveur HTTP local pour l'extension Live Server : sert un dossier sur http://localhost:PORT/
/// et recharge les pages HTML ouvertes dans le navigateur quand un fichier change.
/// </summary>
public static class LiveServerService
{
    private static HttpListener? _ecouteur;
    private static FileSystemWatcher? _surveillant;
    private static CancellationTokenSource? _jeton;
    private static string _racine = "";
    private static string _racineAvecSep = "";
    private static int _version;

    public static int Port { get; private set; }
    public static bool EnCours => _ecouteur?.IsListening == true;

    /// <summary>Le serveur a démarré ou s'est arrêté.</summary>
    public static event Action? EtatChange;

    // Script injecté dans chaque page HTML : interroge la version et recharge si elle change
    private const string Script =
        "<script>(function(){var v=null;setInterval(function(){fetch('/__keycode_version')" +
        ".then(function(r){return r.text();}).then(function(t){if(v===null){v=t;}" +
        "else if(v!==t){location.reload();}}).catch(function(){});},1000);})();</script>";

    public static bool Demarrer(string racine)
    {
        Arreter();

        _racine = Path.GetFullPath(racine);
        _racineAvecSep = _racine.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        HttpListener? ecouteur = null;
        for (var port = 5500; port < 5520; port++)
        {
            var candidat = new HttpListener();
            candidat.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                candidat.Start();
                ecouteur = candidat;
                Port = port;
                break;
            }
            catch (HttpListenerException)
            {
                candidat.Close();
            }
        }
        if (ecouteur == null) return false;

        _ecouteur = ecouteur;
        _jeton = new CancellationTokenSource();
        var jeton = _jeton.Token;
        _ = Task.Run(() => BoucleAsync(ecouteur, jeton));

        try
        {
            _surveillant = new FileSystemWatcher(_racine)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _surveillant.Changed += (_, _) => Interlocked.Increment(ref _version);
            _surveillant.Created += (_, _) => Interlocked.Increment(ref _version);
            _surveillant.Deleted += (_, _) => Interlocked.Increment(ref _version);
            _surveillant.Renamed += (_, _) => Interlocked.Increment(ref _version);
        }
        catch (Exception)
        {
            _surveillant = null;
        }

        EtatChange?.Invoke();
        return true;
    }

    public static void Arreter()
    {
        var etaitEnCours = EnCours;

        _jeton?.Cancel();
        _surveillant?.Dispose();
        _surveillant = null;
        try
        {
            _ecouteur?.Stop();
            _ecouteur?.Close();
        }
        catch (Exception) { }
        _ecouteur = null;

        if (etaitEnCours) EtatChange?.Invoke();
    }

    /// <summary>Adresse à ouvrir dans le navigateur pour un fichier du dossier servi (racine sinon).</summary>
    public static string Url(string? cheminFichier)
    {
        var baseUrl = $"http://localhost:{Port}/";
        if (string.IsNullOrEmpty(cheminFichier)) return baseUrl;
        try
        {
            var complet = Path.GetFullPath(cheminFichier);
            if (!complet.StartsWith(_racineAvecSep, StringComparison.OrdinalIgnoreCase)) return baseUrl;
            var relatif = complet[_racineAvecSep.Length..].Replace(Path.DirectorySeparatorChar, '/');
            return baseUrl + string.Join("/", relatif.Split('/').Select(Uri.EscapeDataString));
        }
        catch (Exception)
        {
            return baseUrl;
        }
    }

    private static async Task BoucleAsync(HttpListener ecouteur, CancellationToken jeton)
    {
        while (!jeton.IsCancellationRequested && ecouteur.IsListening)
        {
            HttpListenerContext contexte;
            try
            {
                contexte = await ecouteur.GetContextAsync();
            }
            catch (Exception)
            {
                break;
            }
            _ = Task.Run(() => Repondre(contexte));
        }
    }

    private static void Repondre(HttpListenerContext contexte)
    {
        try
        {
            var chemin = Uri.UnescapeDataString(contexte.Request.Url?.AbsolutePath ?? "/");
            if (chemin == "/__keycode_version")
            {
                Ecrire(contexte, "text/plain", Encoding.UTF8.GetBytes(_version.ToString()));
                return;
            }

            var relatif = chemin.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var complet = Path.GetFullPath(Path.Combine(_racine, relatif));
            if (!(complet + Path.DirectorySeparatorChar).StartsWith(_racineAvecSep, StringComparison.OrdinalIgnoreCase))
            {
                Statut(contexte, 403);
                return;
            }

            if (Directory.Exists(complet))
                complet = Path.Combine(complet, "index.html");
            if (!File.Exists(complet))
            {
                Statut(contexte, 404);
                return;
            }

            var octets = File.ReadAllBytes(complet);
            var extension = Path.GetExtension(complet).ToLowerInvariant();
            if (extension is ".html" or ".htm")
            {
                var page = new UTF8Encoding(false).GetString(octets);
                var position = page.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                page = position >= 0 ? page.Insert(position, Script) : page + Script;
                octets = new UTF8Encoding(false).GetBytes(page);
            }
            Ecrire(contexte, TypeMime(extension), octets);
        }
        catch (Exception)
        {
            try { Statut(contexte, 500); } catch (Exception) { }
        }
    }

    private static void Ecrire(HttpListenerContext contexte, string type, byte[] octets)
    {
        contexte.Response.ContentType = type;
        contexte.Response.ContentLength64 = octets.Length;
        contexte.Response.Headers["Cache-Control"] = "no-store";
        contexte.Response.OutputStream.Write(octets, 0, octets.Length);
        contexte.Response.OutputStream.Close();
    }

    private static void Statut(HttpListenerContext contexte, int code)
    {
        contexte.Response.StatusCode = code;
        contexte.Response.Close();
    }

    private static string TypeMime(string extension) => extension switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".ico" => "image/x-icon",
        ".txt" or ".md" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };
}