using System.IO;
using System.Security;
using System.Text;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace KeyCode.Services;

/// <summary>
/// Coloration syntaxique selon l'extension du fichier et le thème actif.
/// Les définitions XSHD sont générées en mémoire avec les palettes Clair+ et Sombre+ de VS Code.
/// Note importante : AvalonEdit compile les règles XSHD avec RegexOptions.IgnorePatternWhitespace,
/// tout caractère '#' dans une règle doit donc être échappé en '\#'.
/// </summary>
public static class ColorationService
{
    private sealed record Palette(
        string Commentaire, string Chaine, string MotCle, string Controle,
        string Type, string Nombre, string Prepro, string Variable);

    private static readonly Palette _paletteClair =
        new("#008000", "#A31515", "#0000FF", "#AF00DB", "#267F99", "#098658", "#808080", "#001080");

    private static readonly Palette _paletteSombre =
        new("#6A9955", "#CE9178", "#569CD6", "#C586C0", "#4EC9B0", "#B5CEA8", "#C586C0", "#9CDCFE");

    // One Dark Pro : palette de l'éditeur Atom
    private static readonly Palette _paletteOneDark =
        new("#7F848E", "#98C379", "#C678DD", "#C678DD", "#E5C07B", "#D19A66", "#61AFEF", "#E06C75");

    private sealed record Chaine(string Debut, string Fin, bool Multiligne = false, bool Echappement = true);
    private sealed record Regle(string Couleur, string Expression);

    private sealed class Langage
    {
        public required string Nom { get; init; }
        public string[] Extensions { get; init; } = [];
        public string[] Controle { get; init; } = [];
        public string[] MotsCles { get; init; } = [];
        public string[] Types { get; init; } = [];
        public string[] CommentairesLigne { get; init; } = [];
        public (string Debut, string Fin)? CommentaireBloc { get; init; }
        public Chaine[] Chaines { get; init; } = [];
        public Regle[] Regles { get; init; } = [];
        public bool IgnorerCasse { get; init; }
        public bool Markup { get; init; }
    }

    private const string TroisGuillemets = "\"\"\"";
    private const string TroisApostrophes = "'''";

    private static readonly Chaine _guillemets = new("\"", "\"");
    private static readonly Chaine _apostrophes = new("'", "'");

    private static string[] Mots(string liste) =>
        liste.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static readonly (string Debut, string Fin) _blocC = (@"/\*", @"\*/");
    // '#' doit être échappé en '\#' à cause de RegexOptions.IgnorePatternWhitespace dans AvalonEdit
    private const string DirectivePrepro = @"^[ \t]*\#[ \t]*[A-Za-z]+";

    private static readonly Langage[] _langages =
    [
        new()
        {
            Nom = "Python", Extensions = [".py", ".pyw"],
            Controle = Mots("if elif else for while break continue return try except finally raise with yield pass assert import from as await async lambda del global nonlocal match case"),
            MotsCles = Mots("def class and or not in is None True False self cls"),
            Types = Mots("int str float bool list dict set tuple bytes object print input len range open type enumerate zip map filter sum min max abs sorted reversed isinstance super Exception"),
            CommentairesLigne = [@"\#"],
            Chaines = [new(TroisGuillemets, TroisGuillemets, true), new(TroisApostrophes, TroisApostrophes, true), _guillemets, _apostrophes],
            Regles = [new("Prepro", @"@[A-Za-z_][\w.]*")],
        },
        new()
        {
            Nom = "JavaScript", Extensions = [".js", ".mjs", ".cjs", ".jsx"],
            Controle = Mots("if else for while do switch case break continue return throw try catch finally yield await default"),
            MotsCles = Mots("async class const debugger delete export extends function import in instanceof let new of static super this typeof var void with null undefined true false get set"),
            Types = Mots("Object Array String Number Boolean Map Set Promise Date Math JSON console window document"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes, new("`", "`", true)],
        },
        new()
        {
            Nom = "TypeScript", Extensions = [".ts", ".tsx"],
            Controle = Mots("if else for while do switch case break continue return throw try catch finally yield await default"),
            MotsCles = Mots("abstract async as class const declare debugger delete enum export extends function implements import in instanceof interface keyof let module namespace new of private protected public readonly static super this type typeof var void with null undefined true false get set"),
            Types = Mots("any boolean never number object string symbol unknown void bigint Array Record Partial Required Readonly Promise Map Set console"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes, new("`", "`", true)],
        },
        new()
        {
            Nom = "C#", Extensions = [".cs"],
            Controle = Mots("if else for foreach while do switch case break continue return goto throw try catch finally yield await default when"),
            MotsCles = Mots("abstract as async base class const delegate enum event explicit extern false fixed implicit in interface internal is lock namespace new null operator out override params partial private protected public readonly record ref sealed sizeof stackalloc static struct this true typeof unchecked unsafe using virtual volatile where get set init required"),
            Types = Mots("bool byte char decimal double float int long object sbyte short string uint ulong ushort void var dynamic nint nuint Task List Dictionary StringBuilder Console"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", DirectivePrepro)],
        },
        new()
        {
            Nom = "Java", Extensions = [".java"],
            Controle = Mots("if else for while do switch case break continue return throw try catch finally yield default assert"),
            MotsCles = Mots("abstract class const enum extends final implements import instanceof interface native new null package private protected public static strictfp super synchronized this throws transient true false var volatile record sealed permits"),
            Types = Mots("boolean byte char double float int long short void String Integer Long Double Boolean Object List Map Set ArrayList HashMap Scanner System"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", @"@[A-Za-z_]\w*")],
        },
        new()
        {
            Nom = "C", Extensions = [".c", ".h"],
            Controle = Mots("if else for while do switch case break continue return goto default"),
            MotsCles = Mots("auto const enum extern inline register restrict sizeof static struct typedef union volatile NULL true false"),
            Types = Mots("char double float int long short signed unsigned void size_t bool FILE"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", DirectivePrepro), new("Chaine", @"(?<=^[ \t]*\#[ \t]*include[ \t]*)<[^>]*>")],
        },
        new()
        {
            Nom = "C++", Extensions = [".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx"],
            Controle = Mots("if else for while do switch case break continue return goto default try catch throw"),
            MotsCles = Mots("auto class const constexpr delete enum explicit extern false final friend inline mutable namespace new noexcept nullptr operator override private protected public static struct template this true typedef typename union using virtual volatile"),
            Types = Mots("bool char char16_t char32_t double float int long short signed unsigned void wchar_t size_t string vector map set std cout cin endl"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", DirectivePrepro), new("Chaine", @"(?<=^[ \t]*\#[ \t]*include[ \t]*)<[^>]*>")],
        },
        new()
        {
            Nom = "PHP", Extensions = [".php"],
            Controle = Mots("if elseif else endif for foreach endforeach while endwhile do switch case break continue return throw try catch finally yield match default"),
            MotsCles = Mots("abstract and as class clone const declare echo empty enum extends final function global implements include include_once instanceof interface isset list namespace new or print private protected public readonly require require_once static trait use var xor true false null"),
            Types = Mots("int float string bool array object void mixed self parent"),
            CommentairesLigne = ["//", @"\#"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", @"<\?php|<\?=|\?>"), new("Variable", @"\$[A-Za-z_]\w*")],
        },
        new()
        {
            Nom = "PowerShell", Extensions = [".ps1", ".psm1", ".psd1"], IgnorerCasse = true,
            Controle = Mots("if elseif else for foreach while do switch break continue return throw try catch finally exit"),
            MotsCles = Mots("function param begin process end in filter class using"),
            CommentairesLigne = [@"\#"], CommentaireBloc = (@"<\#", @"\#>"),
            Chaines = [new("\"", "\"", false, false), new("'", "'", false, false)],
            Regles = [new("Variable", @"\$\{[^}]*\}|\$[\w:]+"), new("Type", @"\b[A-Za-z]+-[A-Za-z]\w+\b")],
        },
        new()
        {
            Nom = "Batch", Extensions = [".bat", ".cmd"], IgnorerCasse = true,
            Controle = Mots("if else goto call exit for do in"),
            MotsCles = Mots("echo set setlocal endlocal enabledelayedexpansion pause cls cd dir copy move del start title type md mkdir rd rmdir ren not exist defined errorlevel equ neq lss leq gtr geq off on"),
            CommentairesLigne = ["::", @"^[ \t]*@?rem\b"],
            Chaines = [new("\"", "\"", false, false)],
            Regles = [new("Prepro", @"^[ \t]*:[A-Za-z_][\w.-]*"), new("Variable", @"%[~\w]+%|%%?~?[0-9A-Za-z*]|![\w]+!")],
        },
        new()
        {
            Nom = "Go", Extensions = [".go"],
            Controle = Mots("if else for switch case break continue return goto default select fallthrough defer go range"),
            MotsCles = Mots("chan const func import interface map package struct type var nil true false iota"),
            Types = Mots("bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes, new("`", "`", true, false)],
        },
        new()
        {
            Nom = "Rust", Extensions = [".rs"],
            Controle = Mots("if else for while loop match break continue return in"),
            MotsCles = Mots("as async await const crate dyn enum extern false fn impl let mod move mut pub ref self Self static struct super trait true type unsafe use where"),
            Types = Mots("i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box Some None Ok Err"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets],
            Regles =
            [
                new("Chaine", @"'(\\.|[^'\\])'"),
                new("Type", @"'[A-Za-z_]\w*(?!['\w])"),
                new("Prepro", @"\#!?\[[^\]]*\]"),
                new("Prepro", @"\b[a-z_]\w*!(?=[\(\[\{])"),
            ],
        },
        new()
        {
            Nom = "CSS", Extensions = [".css", ".scss"],
            CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles =
            [
                new("Prepro", @"@[A-Za-z-]+"),
                new("Type", @"\.[A-Za-z_-][\w-]*|\#[A-Za-z0-9_-]+"),
                new("MotCle", @"[A-Za-z-]+(?=[ \t]*:)"),
            ],
        },
        new()
        {
            Nom = "SQL", Extensions = [".sql"], IgnorerCasse = true,
            Controle = Mots("SELECT FROM WHERE JOIN INNER LEFT RIGHT FULL ON GROUP BY HAVING ORDER ASC DESC LIMIT OFFSET UNION ALL INSERT INTO VALUES UPDATE SET DELETE CREATE ALTER DROP TABLE VIEW INDEX TRIGGER CASE WHEN THEN ELSE END"),
            MotsCles = Mots("AS AND OR NOT IN IS NULL LIKE BETWEEN EXISTS DISTINCT PRIMARY KEY FOREIGN REFERENCES DEFAULT UNIQUE CHECK CONSTRAINT"),
            Types = Mots("INT INTEGER BIGINT SMALLINT VARCHAR CHAR TEXT BOOLEAN DATE DATETIME TIMESTAMP DECIMAL FLOAT DOUBLE BLOB"),
            CommentairesLigne = ["--"], CommentaireBloc = _blocC,
            Chaines = [new("'", "'", false, false)],
        },
        new()
        {
            Nom = "Markdown", Extensions = [".md"],
            Chaines = [new("```", "```", true, false), new("`", "`", false, false)],
            Regles =
            [
                new("MotCle", @"^[ \t]*\#{1,6}[^\r\n]*"),
                new("Controle", @"^[ \t]*[-*+][ \t]+|^[ \t]*\d+\.[ \t]+"),
                new("Type", @"\*\*[^*\r\n]+\*\*|__[^_\r\n]+__"),
                new("Chaine", @"\[[^\]\r\n]+\]\([^\)\r\n]+\)"),
            ],
        },
        new()
        {
            Nom = "YAML", Extensions = [".yml", ".yaml"],
            MotsCles = Mots("true false null yes no on off"),
            CommentairesLigne = [@"\#"],
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("MotCle", @"^[ \t\-]*[A-Za-z0-9_.-]+(?=[ \t]*:)")],
        },
        new()
        {
            Nom = "JSON", Extensions = [".json"],
            MotsCles = Mots("true false null"),
            Chaines = [_guillemets],
        },
        new() { Nom = "HTML", Extensions = [".html", ".htm"], Markup = true },
        new() { Nom = "XML", Extensions = [".xml", ".xaml", ".csproj", ".svg", ".config", ".xsd", ".props", ".targets", ".resx"], Markup = true },
    ];

    private static readonly Dictionary<string, Langage> _parExtension = new();
    private static readonly Dictionary<string, IHighlightingDefinition?> _cache = new();
    private static readonly object _verrouCache = new();

    static ColorationService()
    {
        foreach (var langage in _langages)
            foreach (var extension in langage.Extensions)
                _parExtension[extension] = langage;
    }

    /// <summary>Applique la coloration correspondant à l'extension du fichier et au thème actuel.</summary>
    public static void Appliquer(TextEditor editeur, string chemin)
    {
        try
        {
            var extension = Path.GetExtension(chemin);
            var definition = ObtenirDefinition(extension);
            if (!ReferenceEquals(editeur.SyntaxHighlighting, definition))
                editeur.SyntaxHighlighting = definition;
        }
        catch (Exception)
        {
            editeur.SyntaxHighlighting = null;
        }
    }

    /// <summary>Mots-clés du langage du fichier (pour l'auto-complétion). Liste vide si le langage est inconnu.</summary>
    public static IReadOnlyList<string> ObtenirMotsCles(string? chemin)
    {
        if (string.IsNullOrEmpty(chemin)) return Array.Empty<string>();
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        if (!_parExtension.TryGetValue(ext, out var langage))
            return Array.Empty<string>();
        return langage.Controle.Concat(langage.MotsCles).Concat(langage.Types).ToList();
    }

    /// <summary>Retourne le nom du langage pour affichage dans la barre de statut.</summary>
    public static string? ObtenirNomLangage(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return null;
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        return _parExtension.TryGetValue(ext, out var langage) ? langage.Nom : null;
    }

    private static IHighlightingDefinition? ObtenirDefinition(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        extension = extension.ToLowerInvariant();

        if (!_parExtension.TryGetValue(extension, out var langage))
            return HighlightingManager.Instance.GetDefinitionByExtension(extension);

        var sombre = string.Equals(ThemeManager.ThemeActuel, "Sombre", StringComparison.OrdinalIgnoreCase);
        var oneDark = sombre && ThemeManager.Variante == "OneDarkPro";
        var commentairesColores = ExtensionsService.EstActive(ExtensionsService.IdBetterComments);
        var cle = langage.Nom
                  + (sombre ? (oneDark ? "|onedark" : "|sombre") : "|clair")
                  + (commentairesColores ? "|bc" : "");

        lock (_verrouCache)
        {
            if (_cache.TryGetValue(cle, out var enCache)) return enCache;
        }

        IHighlightingDefinition? definition;
        try
        {
            var palette = !sombre ? _paletteClair : oneDark ? _paletteOneDark : _paletteSombre;
            var xshd = ConstruireXshd(langage, palette, commentairesColores);
            using var lecteur = XmlReader.Create(new StringReader(xshd));
            definition = HighlightingLoader.Load(lecteur, HighlightingManager.Instance);
        }
        catch (Exception)
        {
            definition = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        }

        lock (_verrouCache)
        {
            _cache[cle] = definition;
        }
        return definition;
    }

    private static string Echapper(string texte) => SecurityElement.Escape(texte) ?? "";

    private static string ConstruireXshd(Langage langage, Palette palette, bool commentairesColores)
    {
        var sb = new StringBuilder();
        sb.Append("<SyntaxDefinition name=\"").Append(Echapper(langage.Nom))
          .Append("\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");

        AjouterCouleur(sb, "Commentaire", palette.Commentaire);
        AjouterCouleur(sb, "Chaine", palette.Chaine);
        AjouterCouleur(sb, "MotCle", palette.MotCle);
        AjouterCouleur(sb, "Controle", palette.Controle);
        AjouterCouleur(sb, "Type", palette.Type);
        AjouterCouleur(sb, "Nombre", palette.Nombre);
        AjouterCouleur(sb, "Prepro", palette.Prepro);
        AjouterCouleur(sb, "Variable", palette.Variable);
        AjouterCouleur(sb, "Tag", palette.MotCle);
        AjouterCouleur(sb, "Attribut", palette.Variable);
        AjouterCouleur(sb, "CommentaireAlerte", "#FF2D00");
        AjouterCouleur(sb, "CommentaireQuestion", "#3498DB");
        AjouterCouleur(sb, "CommentaireTodo", "#FF8C00");
        AjouterCouleur(sb, "CommentaireInfo", "#3E9B4F");
        AjouterCouleur(sb, "CommentaireBarre", "#7A7A7A");

        sb.Append(langage.IgnorerCasse ? "<RuleSet ignoreCase=\"true\">" : "<RuleSet>");

        if (langage.Markup)
        {
            AjouterMarkup(sb);
        }
        else
        {
            if (langage.CommentaireBloc is { } bloc)
                AjouterSpan(sb, "Commentaire", bloc.Debut, bloc.Fin, multiligne: true, echappement: false);

            if (commentairesColores)
                AjouterCommentairesColores(sb, langage);

            foreach (var debut in langage.CommentairesLigne)
                sb.Append("<Span color=\"Commentaire\" begin=\"").Append(Echapper(debut)).Append("\"/>");

            foreach (var chaine in langage.Chaines)
                AjouterSpan(sb, "Chaine", chaine.Debut, chaine.Fin, chaine.Multiligne, chaine.Echappement);

            foreach (var regle in langage.Regles)
                sb.Append("<Rule color=\"").Append(regle.Couleur).Append("\">").Append(Echapper(regle.Expression)).Append("</Rule>");

            AjouterMots(sb, "Controle", langage.Controle);
            AjouterMots(sb, "MotCle", langage.MotsCles);
            AjouterMots(sb, "Type", langage.Types);

            sb.Append("<Rule color=\"Nombre\">")
              .Append(Echapper(@"\b0[xX][0-9a-fA-F_]+\b|\b\d[\d_]*(\.\d+)?([eE][+-]?\d+)?[fFdDlLuU]*\b"))
              .Append("</Rule>");
        }

        sb.Append("</RuleSet></SyntaxDefinition>");
        return sb.ToString();
    }

    private static void AjouterCouleur(StringBuilder sb, string nom, string couleur) =>
        sb.Append("<Color name=\"").Append(nom).Append("\" foreground=\"").Append(couleur).Append("\"/>");

    private static void AjouterSpan(StringBuilder sb, string couleur, string debut, string fin, bool multiligne, bool echappement)
    {
        sb.Append("<Span color=\"").Append(couleur).Append('"').Append(multiligne ? " multiline=\"true\"" : "")
          .Append("><Begin>").Append(Echapper(debut)).Append("</Begin><End>").Append(Echapper(fin)).Append("</End>");

        if (echappement)
            sb.Append("<RuleSet><Span begin=\"\\\\\" end=\".\"/></RuleSet>");

        sb.Append("</Span>");
    }

    private static void AjouterMots(StringBuilder sb, string couleur, string[] mots)
    {
        if (mots.Length == 0) return;
        sb.Append("<Keywords color=\"").Append(couleur).Append("\">");
        foreach (var mot in mots)
            sb.Append("<Word>").Append(Echapper(mot)).Append("</Word>");
        sb.Append("</Keywords>");
    }

    // Better Comments : annotation placée juste après le symbole de commentaire (le '#' est déjà échappé par le langage)
    private static readonly (string Couleur, string Motif)[] _annotations =
    [
        ("CommentaireAlerte", @"!"),
        ("CommentaireQuestion", @"\?"),
        ("CommentaireTodo", @"(?:TODO|FIXME)\b"),
        ("CommentaireInfo", @"\*"),
        ("CommentaireBarre", @"//"),
    ];

    private static void AjouterCommentairesColores(StringBuilder sb, Langage langage)
    {
        foreach (var prefixe in langage.CommentairesLigne)
        {
            if (prefixe.StartsWith('^')) continue; // règle ancrée (ex. REM en Batch) : ignorée

            foreach (var (couleur, motif) in _annotations)
            {
                sb.Append("<Span color=\"").Append(couleur).Append("\" begin=\"")
                  .Append(Echapper(prefixe + @"[ \t]*" + motif)).Append("\"/>");
            }
        }
    }

    private static void AjouterMarkup(StringBuilder sb)
    {
        AjouterSpan(sb, "Commentaire", "<!--", "-->", multiligne: true, echappement: false);

        sb.Append("<Span color=\"Tag\" multiline=\"true\"><Begin>").Append(Echapper(@"<[/?!]?[\w:.\-]+"))
          .Append("</Begin><End>").Append(Echapper(@"[/?]?>")).Append("</End><RuleSet>");
        AjouterSpan(sb, "Chaine", "\"", "\"", multiligne: false, echappement: false);
        AjouterSpan(sb, "Chaine", "'", "'", multiligne: false, echappement: false);
        sb.Append("<Rule color=\"Attribut\">").Append(Echapper(@"[\w:.\-]+(?=[ \t]*=)")).Append("</Rule>");
        sb.Append("</RuleSet></Span>");
    }
}
