using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var apply = args.Contains("--apply");
var positional = args.Where(a => a != "--apply").ToArray();
if (positional.Length < 2)
{
    Console.Error.WriteLine("usage: CommentMover <outputRoot> [--apply] <sourceRoot>...");
    return 1;
}

var outputRoot  = positional[0];
var sourceRoots = positional[1..];

var repoRoot = Directory.GetCurrentDirectory();
int filesTouched = 0, commentsMoved = 0;

foreach (var sourceRoot in sourceRoots)
{
    if (!Directory.Exists(sourceRoot))
    {
        Console.Error.WriteLine($"skipping missing root {sourceRoot}");
        continue;
    }

    foreach (var path in Directory
                 .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                 .Where(NotGenerated)
                 .OrderBy(p => p, StringComparer.Ordinal))
    {
        var text = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();

        var comments = root.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia)
                     || t.IsKind(SyntaxKind.MultiLineCommentTrivia))
            .OrderBy(t => t.SpanStart)
            .ToList();

        if (comments.Count == 0)
            continue;

        var worthKeeping = comments.Where(t => !IsDecoration(t)).ToList();

        var lines = tree.GetText();
        var entries = GroupRuns(worthKeeping, lines, root);

        var relative = Path.GetRelativePath(repoRoot, path);
        var notePath = Path.Combine(outputRoot, Path.ChangeExtension(relative, ".md"));

        if (apply)
        {
            if (entries.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
                File.WriteAllText(notePath, RenderNotes(relative, entries));
            }
            File.WriteAllText(path, StripComments(text, comments));
        }

        filesTouched++;
        commentsMoved += entries.Count;
        Console.WriteLine($"  {entries.Count,4}  {relative}");
    }
}

Console.WriteLine();
Console.WriteLine($"{commentsMoved} comments across {filesTouched} files"
                + (apply ? $" -> {outputRoot}" : "  (dry run; pass --apply to write)"));
return 0;

static bool IsDecoration(SyntaxTrivia trivia)
{
    var body = trivia.ToString().Replace("//", "").Replace("/*", "").Replace("*/", "");
    return !body.Any(char.IsLetterOrDigit);
}

static bool NotGenerated(string path) =>
    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
    && !path.EndsWith(".g.cs", StringComparison.Ordinal)
    && !path.EndsWith(".Designer.cs", StringComparison.Ordinal);

static string SymbolPathOf(SyntaxTrivia trivia, SyntaxNode root)
{
    var node = root.FindNode(trivia.Span, findInsideTrivia: false, getInnermostNodeForTie: true);
    var parts = new List<string>();

    for (var current = node; current is not null; current = current.Parent)
    {
        var name = current switch
        {
            MethodDeclarationSyntax m        => m.Identifier.Text,
            ConstructorDeclarationSyntax c   => ".ctor",
            DestructorDeclarationSyntax     => "~",
            PropertyDeclarationSyntax p      => p.Identifier.Text,
            IndexerDeclarationSyntax        => "this[]",
            EventDeclarationSyntax e         => e.Identifier.Text,
            OperatorDeclarationSyntax o      => "operator " + o.OperatorToken.Text,
            LocalFunctionStatementSyntax l   => l.Identifier.Text,
            TypeDeclarationSyntax t          => t.Identifier.Text,
            EnumDeclarationSyntax en         => en.Identifier.Text,
            DelegateDeclarationSyntax d      => d.Identifier.Text,
            BaseNamespaceDeclarationSyntax  => null,
            _                                => null,
        };
        if (name is not null)
            parts.Insert(0, name);
    }

    return parts.Count == 0 ? "(file)" : string.Join('.', parts);
}

static List<Entry> GroupRuns(List<SyntaxTrivia> comments, SourceText text, SyntaxNode root)
{
    var entries = new List<Entry>();
    var run = new List<SyntaxTrivia>();

    void Flush()
    {
        if (run.Count == 0) return;
        var body = string.Join(
            ' ',
            run.SelectMany(t => t.ToString().Split('\n'))
               .Select(l => l.Trim().TrimStart('/', '*').Trim())
               .Select(l => l.TrimEnd('*', '/').TrimEnd())
               .Where(l => l.Length > 0));
        entries.Add(new Entry(
            SymbolPathOf(run[0], root),
            text.Lines.GetLinePosition(run[0].SpanStart).Line + 1,
            body));
        run.Clear();
    }

    foreach (var trivia in comments)
    {
        if (run.Count > 0)
        {
            var previousLine = text.Lines.GetLinePosition(run[^1].SpanStart).Line;
            var thisLine     = text.Lines.GetLinePosition(trivia.SpanStart).Line;
            var sameSymbol   = SymbolPathOf(trivia, root) == SymbolPathOf(run[^1], root);
            if (thisLine != previousLine + 1 || !sameSymbol)
                Flush();
        }
        run.Add(trivia);
    }
    Flush();
    return entries;
}

static string RenderNotes(string relative, List<Entry> entries)
{
    var sb = new StringBuilder();
    sb.Append("# ").Append(relative.Replace('\\', '/')).AppendLine();
    sb.AppendLine();
    sb.AppendLine("Inline commentary for the source file above, keyed by symbol. Read this before");
    sb.AppendLine("changing that file; update it in the same change. Line numbers are where each note");
    sb.AppendLine("sat when it was moved and are a hint only — the symbol is the anchor.");

    foreach (var group in entries.GroupBy(e => e.Symbol))
    {
        sb.AppendLine();
        sb.Append("## ").AppendLine(group.Key);
        sb.AppendLine();
        foreach (var entry in group)
            sb.Append("- (was line ").Append(entry.Line).Append(") ").AppendLine(entry.Text);
    }
    return sb.ToString();
}

static string StripComments(string text, List<SyntaxTrivia> comments)
{
    var sb = new StringBuilder(text);
    foreach (var trivia in Enumerable.Reverse(comments))
    {
        var start = trivia.SpanStart;
        var end   = trivia.Span.End;

        var lineStart = start;
        while (lineStart > 0 && sb[lineStart - 1] != '\n')
            lineStart--;

        var onlyIndentBefore = true;
        for (var i = lineStart; i < start; i++)
        {
            if (!char.IsWhiteSpace(sb[i])) { onlyIndentBefore = false; break; }
        }

        if (onlyIndentBefore)
        {
            var lineEnd = end;
            while (lineEnd < sb.Length && sb[lineEnd] != '\n')
                lineEnd++;
            if (lineEnd < sb.Length)
                lineEnd++;
            sb.Remove(lineStart, lineEnd - lineStart);
        }
        else
        {
            var cut = start;
            while (cut > lineStart && (sb[cut - 1] == ' ' || sb[cut - 1] == '\t'))
                cut--;
            sb.Remove(cut, end - cut);
        }
    }

    var stripped = sb.ToString();
    while (stripped.Contains("\n\n\n", StringComparison.Ordinal))
        stripped = stripped.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
    return stripped;
}

internal readonly record struct Entry(string Symbol, int Line, string Text);
