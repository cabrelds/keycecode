using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace KeyCode.Services;

public enum SeveriteDiagnostic
{
    Erreur,
    Avertissement,
    Information
}

public sealed record MarqueurDiagnostic(
    int Debut,
    int Longueur,
    SeveriteDiagnostic Severite,
    string Message,
    string Source = "Analyse");

/// <summary>
/// Dessine des soulignages ondulés (rouges/jaunes/bleus) sous les erreurs dans AvalonEdit
/// et affiche une infobulle détaillée au survol de la souris.
/// </summary>
public sealed class MarqueurService : IBackgroundRenderer
{
    private readonly TextEditor _editeur;
    private readonly List<MarqueurDiagnostic> _marqueurs = new();
    private readonly ToolTip _infobulle = new();

    private static readonly Pen _styloErreur = CreerStylo(Color.FromRgb(0xF1, 0x4C, 0x4C));
    private static readonly Pen _styloAvertissement = CreerStylo(Color.FromRgb(0xCC, 0xA7, 0x00));
    private static readonly Pen _styloInfo = CreerStylo(Color.FromRgb(0x37, 0x94, 0xFF));

    // Error Lens : teinte de la ligne entière et couleur du message en fin de ligne
    private static readonly Brush _fondErreur = PinceauArgb(0x2E, 0xF1, 0x4C, 0x4C);
    private static readonly Brush _fondAvertissement = PinceauArgb(0x2E, 0xCC, 0xA7, 0x00);
    private static readonly Brush _fondInfo = PinceauArgb(0x2E, 0x37, 0x94, 0xFF);
    private static readonly Brush _texteErreur = PinceauArgb(0xD8, 0xF1, 0x4C, 0x4C);
    private static readonly Brush _texteAvertissement = PinceauArgb(0xD8, 0xCC, 0xA7, 0x00);
    private static readonly Brush _texteInfo = PinceauArgb(0xD8, 0x37, 0x94, 0xFF);

    public MarqueurService(TextEditor editeur)
    {
        _editeur = editeur;
        _editeur.TextArea.TextView.BackgroundRenderers.Add(this);
        _editeur.MouseHover += Editeur_MouseHover;
        _editeur.MouseHoverStopped += Editeur_MouseHoverStopped;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<MarqueurDiagnostic> Marqueurs => _marqueurs;

    public void DefinirMarqueurs(IEnumerable<MarqueurDiagnostic> marqueurs)
    {
        _marqueurs.Clear();
        _marqueurs.AddRange(marqueurs);
        _editeur.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public void Effacer()
    {
        if (_marqueurs.Count == 0) return;
        _marqueurs.Clear();
        _editeur.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>Force le redessin (utilisé quand une extension comme Error Lens change d'état).</summary>
    public void Redessiner() => _editeur.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

    /// <summary>Error Lens : colore la ligne et écrit le message du diagnostic après la fin de la ligne.</summary>
    private void DessinerErrorLens(TextView textView, DrawingContext dc)
    {
        var doc = _editeur.Document;
        if (doc == null || doc.TextLength == 0) return;

        // Un seul diagnostic par ligne : le plus grave (Erreur < Avertissement < Information)
        var parLigne = new Dictionary<int, MarqueurDiagnostic>();
        foreach (var m in _marqueurs)
        {
            var offset = Math.Clamp(m.Debut, 0, doc.TextLength);
            var numero = doc.GetLineByOffset(offset).LineNumber;
            if (!parLigne.TryGetValue(numero, out var existant) || m.Severite < existant.Severite)
                parLigne[numero] = m;
        }

        var police = new Typeface(_editeur.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var taille = Math.Max(9, _editeur.FontSize * 0.88);
        var dpi = VisualTreeHelper.GetDpi(textView).PixelsPerDip;

        foreach (var (numero, marqueur) in parLigne)
        {
            if (textView.GetVisualLine(numero) == null) continue;

            var ligne = doc.GetLineByNumber(numero);
            var fond = marqueur.Severite switch
            {
                SeveriteDiagnostic.Erreur => _fondErreur,
                SeveriteDiagnostic.Avertissement => _fondAvertissement,
                _ => _fondInfo
            };
            var couleurTexte = marqueur.Severite switch
            {
                SeveriteDiagnostic.Erreur => _texteErreur,
                SeveriteDiagnostic.Avertissement => _texteAvertissement,
                _ => _texteInfo
            };

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, ligne, true))
                dc.DrawRectangle(fond, null, rect);

            var message = marqueur.Message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (message.Length == 0) continue;

            var position = textView.GetVisualPosition(
                new TextViewPosition(numero, ligne.Length + 1), VisualYPosition.TextTop) - textView.ScrollOffset;
            var largeur = textView.ActualWidth - (position.X + 28) - 8;
            if (largeur < 40) continue;

            var texte = new FormattedText(message, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                police, taille, couleurTexte, dpi)
            {
                MaxTextWidth = largeur,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(texte, new Point(position.X + 28, position.Y));
        }
    }

    private static Brush PinceauArgb(byte a, byte r, byte g, byte b)
    {
        var pinceau = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        pinceau.Freeze();
        return pinceau;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_marqueurs.Count == 0 || !textView.VisualLinesValid) return;

        var longueurDoc = _editeur.Document?.TextLength ?? 0;
        if (longueurDoc == 0) return;

        if (ExtensionsService.EstActive(ExtensionsService.IdErrorLens))
            DessinerErrorLens(textView, drawingContext);

        foreach (var marqueur in _marqueurs)
        {
            var debut = Math.Clamp(marqueur.Debut, 0, longueurDoc);
            var fin = Math.Clamp(debut + Math.Max(1, marqueur.Longueur), 0, longueurDoc);
            if (fin <= debut)
            {
                if (debut > 0) debut--;
                fin = Math.Min(debut + 1, longueurDoc);
            }
            if (fin <= debut) continue;

            var segment = new TextSegment { StartOffset = debut, Length = fin - debut };
            var stylo = marqueur.Severite switch
            {
                SeveriteDiagnostic.Erreur => _styloErreur,
                SeveriteDiagnostic.Avertissement => _styloAvertissement,
                _ => _styloInfo
            };

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DessinerOndulation(drawingContext, stylo, rect);
        }
    }

    private static void DessinerOndulation(DrawingContext dc, Pen stylo, Rect rect)
    {
        if (rect.Width < 2) return;

        const double pas = 3.0;
        const double amplitude = 1.7;
        var yBase = rect.Bottom - 1.5;

        var geometrie = new StreamGeometry();
        using (var ctx = geometrie.Open())
        {
            ctx.BeginFigure(new Point(rect.Left, yBase), isFilled: false, isClosed: false);
            var haut = true;
            for (var x = rect.Left + pas; x <= rect.Right + pas * 0.5; x += pas)
            {
                var xClamped = Math.Min(x, rect.Right);
                var y = haut ? yBase - amplitude : yBase + amplitude * 0.6;
                ctx.LineTo(new Point(xClamped, y), isStroked: true, isSmoothJoin: false);
                haut = !haut;
            }
        }

        geometrie.Freeze();
        dc.DrawGeometry(null, stylo, geometrie);
    }

    private void Editeur_MouseHover(object sender, MouseEventArgs e)
    {
        if (_marqueurs.Count == 0) return;

        var pos = _editeur.GetPositionFromPoint(e.GetPosition(_editeur));
        if (pos == null) return;

        var offset = _editeur.Document.GetOffset(pos.Value.Location);
        var correspondants = _marqueurs
            .Where(m => offset >= m.Debut && offset <= m.Debut + Math.Max(1, m.Longueur))
            .ToList();

        if (correspondants.Count == 0) return;

        _infobulle.PlacementTarget = _editeur;
        _infobulle.Content = string.Join("\n", correspondants.Select(m =>
        {
            var tag = m.Severite switch
            {
                SeveriteDiagnostic.Erreur => "Erreur",
                SeveriteDiagnostic.Avertissement => "Avertissement",
                _ => "Info"
            };
            return $"[{tag} • {m.Source}] {m.Message}";
        }));
        _infobulle.IsOpen = true;
        e.Handled = true;
    }

    private void Editeur_MouseHoverStopped(object sender, MouseEventArgs e)
    {
        _infobulle.IsOpen = false;
    }

    private static Pen CreerStylo(Color couleur)
    {
        var pinceau = new SolidColorBrush(couleur);
        pinceau.Freeze();
        var stylo = new Pen(pinceau, 1.25);
        stylo.Freeze();
        return stylo;
    }
}
