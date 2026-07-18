using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Cross-platform code metrics — the Visual Studio "Calculate Code Metrics" equivalent
// (cyclomatic complexity, source lines, maintainability index) computed with Roslyn.
//
//   dotnet run --project tools/CodeMetrics -- <sourceRoot> <outputDir>
//
// Defaults: sourceRoot=src, outputDir=TestResults/metrics. Emits metrics.csv and
// metrics.html and prints a summary + the worst offenders.

var sourceRoot = args.Length > 0 ? args[0] : "src";
var outputDir  = args.Length > 1 ? args[1] : Path.Combine("TestResults", "metrics");
Directory.CreateDirectory(outputDir);

var files = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
             && !f.EndsWith(".g.cs") && !f.EndsWith(".Designer.cs"))
    .OrderBy(f => f)
    .ToList();

var rows = new List<Row>();

foreach (var file in files)
{
    var text = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(text);
    var root = tree.GetCompilationUnitRoot();
    var project = ProjectOf(file, sourceRoot);
    var relFile = Path.GetRelativePath(sourceRoot, file);

    var members = root.DescendantNodes().Where(n =>
        n is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax);

    foreach (var member in members)
    {
        SyntaxNode? body = member switch
        {
            BaseMethodDeclarationSyntax m  => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            AccessorDeclarationSyntax a    => (SyntaxNode?)a.Body ?? a.ExpressionBody,
            LocalFunctionStatementSyntax l => (SyntaxNode?)l.Body ?? l.ExpressionBody,
            _ => null,
        };
        if (body is null) continue;

        var name = MemberName(member);
        var type = EnclosingType(member);
        var cc   = 1 + CountBranches(body);
        var loc  = SourceLines(member);
        var mi   = MaintainabilityIndex(cc, loc, HalsteadVolume(body));

        rows.Add(new Row(project, relFile, type, name, cc, loc, mi));
    }
}

rows = [.. rows.OrderByDescending(r => r.Cc).ThenBy(r => r.Mi)];

WriteCsv(Path.Combine(outputDir, "metrics.csv"), rows);
WriteHtml(Path.Combine(outputDir, "metrics.html"), rows);
PrintSummary(rows, outputDir);
return 0;

// ---------------------------------------------------------------------------

static int CountBranches(SyntaxNode body) =>
    body.DescendantNodes().Sum(n => n switch
    {
        IfStatementSyntax                                                     => 1,
        WhileStatementSyntax or DoStatementSyntax                             => 1,
        ForStatementSyntax or ForEachStatementSyntax
            or ForEachVariableStatementSyntax                                 => 1,
        CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax                 => 1,
        SwitchExpressionArmSyntax                                             => 1,
        CatchClauseSyntax                                                     => 1,
        ConditionalExpressionSyntax                                          => 1,
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression)
            || b.IsKind(SyntaxKind.LogicalOrExpression)
            || b.IsKind(SyntaxKind.CoalesceExpression)                        => 1,
        ConditionalAccessExpressionSyntax                                     => 1,
        _ => 0,
    });

// Non-blank, non-comment-only source lines within the member.
static int SourceLines(SyntaxNode member)
{
    var span  = member.GetLocation().GetLineSpan();
    var lines = member.ToFullString().Split('\n');
    return lines.Count(l =>
    {
        var t = l.Trim();
        return t.Length > 0 && !t.StartsWith("//") && !t.StartsWith("///")
            && !t.StartsWith("/*") && !t.StartsWith('*');
    });
}

// Halstead volume V = N * log2(n): operators = punctuation + keyword tokens,
// operands = identifiers + literals.
static double HalsteadVolume(SyntaxNode body)
{
    var distinctOps = new HashSet<string>();
    var distinctOpr = new HashSet<string>();
    int totalOps = 0, totalOpr = 0;

    foreach (var tok in body.DescendantTokens())
    {
        if (tok.IsKind(SyntaxKind.IdentifierToken) || tok.Value is not null && IsLiteral(tok))
        {
            totalOpr++;
            distinctOpr.Add(tok.Text);
        }
        else if (SyntaxFacts.IsPunctuation(tok.Kind()) || SyntaxFacts.IsKeywordKind(tok.Kind()))
        {
            totalOps++;
            distinctOps.Add(tok.Text);
        }
    }

    var n = distinctOps.Count + distinctOpr.Count;
    var bigN = totalOps + totalOpr;
    return n == 0 ? 0 : bigN * Math.Log2(n);
}

static bool IsLiteral(SyntaxToken t) =>
    t.IsKind(SyntaxKind.NumericLiteralToken) || t.IsKind(SyntaxKind.StringLiteralToken)
    || t.IsKind(SyntaxKind.CharacterLiteralToken) || t.IsKind(SyntaxKind.TrueKeyword)
    || t.IsKind(SyntaxKind.FalseKeyword);

// Microsoft's maintainability index, clamped to 0..100.
static int MaintainabilityIndex(int cc, int loc, double halsteadVolume)
{
    var hv  = Math.Max(halsteadVolume, 1);
    var sloc = Math.Max(loc, 1);
    var raw = (171 - 5.2 * Math.Log(hv) - 0.23 * cc - 16.2 * Math.Log(sloc)) * 100.0 / 171.0;
    return (int)Math.Round(Math.Clamp(raw, 0, 100));
}

static string ProjectOf(string file, string root)
{
    var rel   = Path.GetRelativePath(root, file);
    var first = rel.Split(Path.DirectorySeparatorChar)[0];
    return first;
}

static string MemberName(SyntaxNode m) => m switch
{
    MethodDeclarationSyntax x       => x.Identifier.Text + "()",
    ConstructorDeclarationSyntax x  => x.Identifier.Text + ".ctor()",
    OperatorDeclarationSyntax x     => "operator " + x.OperatorToken.Text,
    LocalFunctionStatementSyntax x  => x.Identifier.Text + "() [local]",
    AccessorDeclarationSyntax x     =>
        $"{x.Keyword.Text} ({x.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "?"})",
    _ => m.Kind().ToString(),
};

static string EnclosingType(SyntaxNode node)
{
    var t = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
    return t?.Identifier.Text ?? "<global>";
}

static void WriteCsv(string path, List<Row> rows)
{
    var sb = new StringBuilder("Project,File,Type,Member,CyclomaticComplexity,Lines,MaintainabilityIndex\n");
    foreach (var r in rows)
        sb.Append(CultureInfo.InvariantCulture,
            $"{Csv(r.Project)},{Csv(r.File)},{Csv(r.Type)},{Csv(r.Member)},{r.Cc},{r.Lines},{r.Mi}\n");
    File.WriteAllText(path, sb.ToString());

    static string Csv(string s) => s.Contains(',') ? $"\"{s}\"" : s;
}

static void WriteHtml(string path, List<Row> rows)
{
    static string CcClass(int c) => c > 30 ? "crit" : c > 20 ? "bad" : c > 10 ? "warn" : "ok";
    static string MiClass(int m) => m < 10 ? "crit" : m < 20 ? "bad" : m < 40 ? "warn" : "ok";

    var body = new StringBuilder();
    foreach (var r in rows)
        body.Append(
            $"<tr><td>{Html(r.Project)}</td><td>{Html(r.Type)}</td><td>{Html(r.Member)}</td>" +
            $"<td class='{CcClass(r.Cc)}'>{r.Cc}</td><td>{r.Lines}</td>" +
            $"<td class='{MiClass(r.Mi)}'>{r.Mi}</td><td class='file'>{Html(r.File)}</td></tr>\n");

    var html = $$"""
        <!doctype html><html><head><meta charset="utf-8"><title>ConduitSharp Code Metrics</title>
        <style>
          body{font:14px -apple-system,Segoe UI,sans-serif;margin:24px;color:#1a1a1a}
          h1{font-size:20px} .sub{color:#666;margin-bottom:16px}
          table{border-collapse:collapse;width:100%} th,td{padding:6px 10px;text-align:left;border-bottom:1px solid #eee}
          th{cursor:pointer;background:#f6f6f6;position:sticky;top:0;user-select:none}
          td.file{color:#999;font-size:12px} td:nth-child(4),td:nth-child(5),td:nth-child(6){text-align:right;font-variant-numeric:tabular-nums}
          .ok{color:#1a7f37} .warn{color:#9a6700;font-weight:600} .bad{color:#bc4c00;font-weight:600} .crit{color:#cf222e;font-weight:700}
          .legend span{margin-right:14px}
        </style></head><body>
        <h1>ConduitSharp — Code Metrics</h1>
        <div class="sub">{{rows.Count}} members. Cyclomatic complexity ≤10 good, 11–20 watch, 21–30 refactor, &gt;30 critical.
          Maintainability index ≥40 good, 20–39 watch, &lt;20 poor. Click a header to sort.</div>
        <table id="t"><thead><tr>
          <th>Project</th><th>Type</th><th>Member</th><th>Cyclomatic</th><th>Lines</th><th>Maintainability</th><th>File</th>
        </tr></thead><tbody>
        {{body}}
        </tbody></table>
        <script>
          const t=document.getElementById('t');
          t.querySelectorAll('th').forEach((th,i)=>th.onclick=()=>{
            const rows=[...t.tBodies[0].rows], num=[3,4,5].includes(i);
            const asc=th.dataset.asc==='1'; th.dataset.asc=asc?'0':'1';
            rows.sort((a,b)=>{let x=a.cells[i].textContent,y=b.cells[i].textContent;
              if(num){x=+x;y=+y} return (x>y?1:x<y?-1:0)*(asc?-1:1)});
            rows.forEach(r=>t.tBodies[0].appendChild(r));
          });
        </script></body></html>
        """;
    File.WriteAllText(path, html);

    static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

static void PrintSummary(List<Row> rows, string outputDir)
{
    Console.WriteLine();
    Console.WriteLine($"Code metrics — {rows.Count} members analyzed");
    Console.WriteLine($"  Avg cyclomatic complexity: {rows.Average(r => r.Cc):F1}");
    Console.WriteLine($"  Max cyclomatic complexity: {rows.Max(r => r.Cc)}");
    Console.WriteLine($"  Members with CC > 20:      {rows.Count(r => r.Cc > 20)}");
    Console.WriteLine($"  Members with MI < 20:      {rows.Count(r => r.Mi < 20)}");
    Console.WriteLine();
    Console.WriteLine("  Top 10 by cyclomatic complexity:");
    foreach (var r in rows.Take(10))
        Console.WriteLine($"    CC={r.Cc,3}  MI={r.Mi,3}  {r.Type}.{r.Member}  ({r.Project})");
    Console.WriteLine();
    Console.WriteLine($"  Report: {Path.Combine(outputDir, "metrics.html")}");
    Console.WriteLine($"  CSV:    {Path.Combine(outputDir, "metrics.csv")}");
}

record Row(string Project, string File, string Type, string Member, int Cc, int Lines, int Mi);
