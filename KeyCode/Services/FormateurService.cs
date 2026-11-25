using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KeyCode.Services;

/// <summary>
/// Formatage de documents pour l'extension Prettier : JSON, famille XML/HTML, CSS/SCSS,
/// langages à accolades (C#, JS, TS, Java, C, C++, Rust, Go, PHP) et nettoyage d'indentation.
/// </summary>
public static class FormateurService
{
    private static readonly HashSet<string> _extensionsXml = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".xaml", ".csproj", ".svg", ".config", ".xsd", ".props", ".targets", ".resx"
    };

    private static readonly HashSet<string> _extensionsAccolades = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".java",
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".rs", ".go", ".php"
    };

    private static readonly HashSet<string> _extensionsTexteNettoyable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".py", ".pyw", ".md", ".yaml", ".yml", ".sql", ".ps1", ".sh", ".bat", ".cmd", ".txt"
    };

    /// <summary>Retourne le texte formaté, ou null si l'extension de fichier n'est pas prise en charge.</summary>
    public static string? Formater(string texte, string extension)
    {
        extension = (extension ?? "").ToLowerInvariant();
        if (extension == ".json") return FormaterJson(texte);
        if (_extensionsXml.Contains(extension)) return FormaterXml(texte);
        if (extension is ".html" or ".htm") return FormaterHtml(texte);
        if (extension is ".css" or ".scss") return FormaterCss(texte);
        if (_extensionsAccolades.Contains(extension)) return FormaterCodeAccolades(texte, extension == ".go");
        if (_extensionsTexteNettoyable.Contains(extension)) return NettoyerLignes(texte);
        return null;
    }

    /// <summary>Supprime les espaces et tabulations en fin de ligne sur tout document texte.</summary>
    public static string NettoyerLignes(string texte)
    {
        var lignes = texte.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(texte.Length);
        for (var i = 0; i < lignes.Length; i++)
        {
            if (i > 0) sb.AppendLine();
            sb.Append(lignes[i].TrimEnd(' ', '\t'));
        }
        return sb.ToString();
    }

    private static string FormaterJson(string texte)
    {
        using var document = JsonDocument.Parse(texte);
        using var flux = new MemoryStream();
        using (var ecrivain = new Utf8JsonWriter(flux, new JsonWriterOptions
               {
                   Indented = true,
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            document.WriteTo(ecrivain);
        }
        return Encoding.UTF8.GetString(flux.ToArray());
    }

    private static string FormaterXml(string texte)
    {
        var document = XDocument.Parse(texte, LoadOptions.None);
        var sb = new StringBuilder();
        if (document.Declaration != null)
            sb.AppendLine(document.Declaration.ToString());

        var reglages = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true
        };
        using (var ecrivain = XmlWriter.Create(sb, reglages))
            document.Save(ecrivain);
        return sb.ToString();
    }

    private static string FormaterHtml(string texte)
    {
        try
        {
            return FormaterXml(texte);
        }
        catch (Exception)
        {
            // HTML5 non strict (balises auto-fermantes br, img, input, meta, link...)
            var voidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "area", "base", "br", "col", "embed", "hr", "img", "input",
                "link", "meta", "param", "source", "track", "wbr", "!doctype"
            };

            var lignes = texte.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder(texte.Length);
            var niveau = 0;

            for (var i = 0; i < lignes.Length; i++)
            {
                var l = lignes[i].Trim();
                if (l.Length == 0)
                {
                    if (i > 0) sb.AppendLine();
                    continue;
                }

                var fermeDebut = l.StartsWith("</", StringComparison.Ordinal);
                if (fermeDebut && niveau > 0) niveau--;

                if (i > 0) sb.AppendLine();
                sb.Append(new string(' ', niveau * 2)).Append(l);

                var mOuvre = Regex.Match(l, @"^<([A-Za-z0-9!\-]+)");
                if (mOuvre.Success && !fermeDebut && !l.EndsWith("/>", StringComparison.Ordinal) &&
                    !voidTags.Contains(mOuvre.Groups[1].Value) &&
                    !Regex.IsMatch(l, $@"</{Regex.Escape(mOuvre.Groups[1].Value)}\s*>$", RegexOptions.IgnoreCase))
                {
                    niveau++;
                }
            }
            return sb.ToString();
        }
    }

    private static string FormaterCss(string texte)
    {
        return FormaterCodeAccolades(texte, utiliserTab: false, tailleIndent: 2);
    }

    private static string FormaterCodeAccolades(string texte, bool utiliserTab, int tailleIndent = 4)
    {
        var lignes = texte.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(texte.Length);
        var niveau = 0;

        for (var i = 0; i < lignes.Length; i++)
        {
            var brute = lignes[i].TrimEnd(' ', '\t');
            var trim = brute.TrimStart(' ', '\t');
            if (trim.Length == 0)
            {
                if (i > 0) sb.AppendLine();
                continue;
            }

            var fermantesEnTete = 0;
            while (fermantesEnTete < trim.Length && trim[fermantesEnTete] == '}')
                fermantesEnTete++;

            var niveauLigne = Math.Max(0, niveau - fermantesEnTete);
            if (i > 0) sb.AppendLine();

            var prefixe = utiliserTab
                ? new string('\t', niveauLigne)
                : new string(' ', niveauLigne * tailleIndent);
            sb.Append(prefixe).Append(trim);

            var delta = CompterDeltaAccoladesHorsChaines(trim);
            niveau = Math.Max(0, niveau + delta);
        }

        return sb.ToString();
    }

    private static int CompterDeltaAccoladesHorsChaines(string ligne)
    {
        var delta = 0;
        var dansGuillemets = false;
        var dansApostrophes = false;

        for (var i = 0; i < ligne.Length; i++)
        {
            var c = ligne[i];
            if (!dansGuillemets && !dansApostrophes && c == '/' && i + 1 < ligne.Length && ligne[i + 1] == '/')
                break;

            if (c == '\\')
            {
                i++;
                continue;
            }

            if (!dansApostrophes && c == '"') dansGuillemets = !dansGuillemets;
            else if (!dansGuillemets && c == '\'') dansApostrophes = !dansApostrophes;
            else if (!dansGuillemets && !dansApostrophes)
            {
                if (c == '{') delta++;
                else if (c == '}') delta--;
            }
        }
        return delta;
    }
}
