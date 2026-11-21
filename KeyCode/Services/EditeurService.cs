using ICSharpCode.AvalonEdit;

namespace KeyCode.Services;

public static class EditeurService
{
    public static void Configurer(TextEditor editeur, string? chemin)
    {
        editeur.ShowLineNumbers = true;
        editeur.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        editeur.FontSize = 14;
        if (chemin != null) ColorationService.Appliquer(editeur, chemin);
    }

    public static void ReconfigurerTous() { /* à faire par nyangono */ }
}