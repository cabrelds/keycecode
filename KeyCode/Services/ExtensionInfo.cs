namespace KeyCode.Services;

/// <summary>Fiche d'une extension du catalogue KeyCode (données d'affichage de la Place de marché).</summary>
public sealed class ExtensionInfo
{
    public string Id { get; init; } = "";
    public string Nom { get; init; } = "";
    public string Editeur { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public long Telechargements { get; init; }
    public double Note { get; init; }
    public int NombreAvis { get; init; }
    public int Commanditaires { get; init; }
    public string Categorie { get; init; } = "Other";
    public string Taille { get; init; } = "";
    public string Publie { get; init; } = "";
    public string DernierePublication { get; init; } = "";
    public string DerniereMiseAJour { get; init; } = "";
    public string Depot { get; init; } = "";

    /// <summary>Faux = fiche d'affichage seulement : KeyCode ne l'exécute pas.</summary>
    public bool PriseEnCharge { get; init; } = true;

    /// <summary>Domaine de l'éditeur vérifié (pastille ✔ + lien). Vide = non vérifié.</summary>
    public string Domaine { get; init; } = "";

    /// <summary>Toutes les catégories (la première est aussi dans Categorie).</summary>
    public string[] Categories { get; init; } = [];

    /// <summary>Onglets affichés : details, fonctionnalites, journal, dependances, pack.</summary>
    public string[] Onglets { get; init; } = ["details", "fonctionnalites", "journal"];

    /// <summary>Bouton « Installer ▾ » scindé.</summary>
    public bool MenuInstaller { get; init; }

    /// <summary>Bouton « Passer à la version préliminaire » quand l'extension est installée.</summary>
    public bool Preliminaire { get; init; }

    /// <summary>Étiquette « Intégrée » (Copilot Chat).</summary>
    public bool Integree { get; init; }

    /// <summary>Pack d'extensions, au format "nom|description|éditeur".</summary>
    public string[] Pack { get; init; } = [];

    /// <summary>Affiche le pack en haut de l'onglet DÉTAILS (Jupyter).</summary>
    public bool PackEnDetails { get; init; }

    public IEnumerable<string> ToutesCategories => Categories.Length > 0 ? Categories : new[] { Categorie };

    /// <summary>Texte de l'onglet DÉTAILS. Préfixes : "# " titre, "## " sous-titre, "- " puce, "> " code, sinon paragraphe.</summary>
    public string[] Details { get; init; } = [];

    /// <summary>Puces de l'onglet FONCTIONNALITÉS.</summary>
    public string[] Fonctionnalites { get; init; } = [];

    /// <summary>Commandes au format "commande|description|raccourci".</summary>
    public string[] Commandes { get; init; } = [];

    /// <summary>Journal au format "version|texte".</summary>
    public string[] Journal { get; init; } = [];
}