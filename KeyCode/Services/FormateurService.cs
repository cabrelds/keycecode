using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace KeyCode.Services;

/// <summary>Formatage de documents pour l'extension Prettier : JSON et famille XML.</summary>
public static class FormateurService
{
    private static readonly HashSet<string> _extensionsXml = new()
    {
        ".xml", ".xaml", ".csproj", ".svg", ".config", ".xsd", ".props", ".targets", ".resx"
    };

    /// <summary>Retourne le texte formaté, ou null si l'extension de fichier n'est pas prise en charge.</summary>
    public static string? Formater(string texte, string extension)
    {
        extension = (extension ?? "").ToLowerInvariant();
        if (extension == ".json") return FormaterJson(texte);
        if (_extensionsXml.Contains(extension)) return FormaterXml(texte);
        return null;
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
}