using ICSharpCode.AvalonEdit;

namespace KeyCode.Services;

/// <summary>
/// État partagé de l'application.
/// Propriétaire : cabrelds. Ne pas renommer, ne pas supprimer.
/// </summary>
public static class Contexte
{
    private static TextEditor? _editeurActif;

    // Éditeur de l'onglet actif : écrit par OngletsControl, lu par RechercheControl et la barre de statut
    public static TextEditor? EditeurActif
    {
        get => _editeurActif;
        set
        {
            _editeurActif = value;
            AppEvents.SignalerEditeurActifChange();
        }
    }

    // Chemin du fichier de l'onglet actif (null si l'onglet n'a pas encore été enregistré)
    public static string? CheminActif { get; set; }

    // Dossier ouvert dans l'explorateur (null si aucun)
    public static string? DossierRacine { get; set; }
}