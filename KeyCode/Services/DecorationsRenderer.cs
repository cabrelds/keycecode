using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace KeyCode.Services;

/// <summary>
/// Décorations d'arrière-plan d'un éditeur : bandes d'indentation (indent-rainbow), espaces en fin de ligne
/// (Trailing Spaces) et barres sous les codes couleur (Color Highlight). Chaque décoration lit l'état de
/// son extension au moment de dessiner. Une instance par éditeur.
/// </summary>
public sealed class DecorationsRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editeur;

    private static readonly Brush[] _niveaux =
    [
        Pinceau(0x1F, 0xFF, 0xFF, 0x40),
        Pinceau(0x1F, 0x7F, 0xFF, 0x7F),
        Pinceau(0x1F, 0xFF, 0x7F, 0xFF),
        Pinceau(0x1F, 0x4F, 0xEC, 0xEC),
    ];

    private static readonly Brush _espaceFinal = Pinceau(0x66, 0xF1, 0x4C, 0x4C);

    private static readonly Regex _couleurs =
        new(@"(?<![\w&])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})\b", RegexOptions.Compiled);

    private static readonly Dictionary<string, Brush?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DecorationsRenderer(TextEditor editeur)
    {
        _editeur = editeur;
        var vue = editeur.TextArea.TextView;
        vue.BackgroundRenderers.Add(this);
        vue.VisualLinesChanged += (_, _) => RedessinerSiActif();
        editeur.TextChanged += (_, _) => RedessinerSiActif();
    }

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Force le redessin (appelé quand une extension change d'état).</summary>
    public void Redessiner() => _editeur.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

    private void RedessinerSiActif()
    {
        if (UneActive()) Redessiner();
    }

    private static bool UneActive() =>
        ExtensionsService.EstActive(ExtensionsService.IdIndentRainbow)
        || ExtensionsService.EstActive(ExtensionsService.IdTrailingSpaces)
        || ExtensionsService.EstActive(ExtensionsService.IdColorHighlight);

    public void Draw(TextView textView, DrawingContext dc)
    {
        var indentation = ExtensionsService.EstActive(ExtensionsService.IdIndentRainbow);
        var espaces = ExtensionsService.EstActive(ExtensionsService.IdTrailingSpaces);
        var couleurs = ExtensionsService.EstActive(ExtensionsService.IdColorHighlight);
        if (!(indentation || espaces || couleurs) || !textView.VisualLinesValid) return;

        var document = _editeur.Document;
        if (document == null) return;

        foreach (var visuelle in textView.VisualLines)
        {
            var ligne = visuelle.FirstDocumentLine;
            if (ligne.Length == 0 || ligne.Length > 3000) continue;

            var texte = document.GetText(ligne.Offset, ligne.Length);
            if (indentation) DessinerIndentation(textView, dc, ligne, texte);
            if (espaces) DessinerEspacesFinaux(textView, dc, ligne, texte);
            if (couleurs) DessinerCouleurs(textView, dc, ligne, texte);
        }
    }

    private static void DessinerIndentation(TextView vue, DrawingContext dc, DocumentLine ligne, string texte)
    {
        var fin = texte.TrimEnd(' ', '\t').Length;
        if (fin == 0) return;

        var position = 0;
        var niveau = 0;
        while (position < fin)
        {
            int longueur;
            if (texte[position] == '\t') longueur = 1;
            else if (texte[position] == ' ' && position + 4 <= fin
                     && string.CompareOrdinal(texte, position, "    ", 0, 4) == 0) longueur = 4;
            else break;

            Remplir(vue, dc, _niveaux[niveau % _niveaux.Length], ligne.Offset + position, longueur);
            position += longueur;
            niveau++;
        }
    }

    private static void DessinerEspacesFinaux(TextView vue, DrawingContext dc, DocumentLine ligne, string texte)
    {
        var fin = texte.TrimEnd(' ', '\t').Length;
        if (fin < texte.Length)
            Remplir(vue, dc, _espaceFinal, ligne.Offset + fin, texte.Length - fin);
    }

    private static void DessinerCouleurs(TextView vue, DrawingContext dc, DocumentLine ligne, string texte)
    {
        foreach (Match m in _couleurs.Matches(texte))
        {
            var pinceau = PinceauCouleur(m.Value);
            if (pinceau == null) continue;

            var segment = new TextSegment { StartOffset = ligne.Offset + m.Index, Length = m.Length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(vue, segment))
                dc.DrawRectangle(pinceau, null, new Rect(rect.Left, rect.Bottom - 3, rect.Width, 3));
        }
    }

    private static void Remplir(TextView vue, DrawingContext dc, Brush pinceau, int debut, int longueur)
    {
        var segment = new TextSegment { StartOffset = debut, Length = longueur };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(vue, segment))
            dc.DrawRectangle(pinceau, null, rect);
    }

    private static Brush? PinceauCouleur(string hex)
    {
        if (_cache.TryGetValue(hex, out var enCache)) return enCache;

        Brush? pinceau = null;
        try
        {
            var couleur = (Color)ColorConverter.ConvertFromString(hex);
            var solide = new SolidColorBrush(couleur);
            solide.Freeze();
            pinceau = solide;
        }
        catch (Exception) { }

        _cache[hex] = pinceau;
        return pinceau;
    }

    private static Brush Pinceau(byte a, byte r, byte g, byte b)
    {
        var pinceau = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        pinceau.Freeze();
        return pinceau;
    }
}