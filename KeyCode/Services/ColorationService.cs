using System.IO;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace KeyCode.Services;

/// <summary>
/// Coloration syntaxique selon l'extension du fichier.
/// Propriétaire : nyangono (version minimale fournie par cabrelds).
/// </summary>
public static class ColorationService
{
    public static void Appliquer(TextEditor editeur, string chemin)
    {
        var extension = Path.GetExtension(chemin);
        editeur.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }
}