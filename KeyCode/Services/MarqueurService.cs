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

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_marqueurs.Count == 0 || !textView.VisualLinesValid) return;

        var longueurDoc = _editeur.Document?.TextLength ?? 0;
        if (longueurDoc == 0) return;

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
