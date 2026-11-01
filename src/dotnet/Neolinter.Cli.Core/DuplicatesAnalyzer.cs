using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Neolinter.Cli.Core;

/// <summary>
/// NLC0001: finds sequences of statements that appear more than once in the solution, ignoring identifier names,
/// literal values and trivia. Algorithm and exclusions are described in docs/architecture.md ("duplicates").
/// </summary>
public static class DuplicatesAnalyzer
{
    private const string GeneratedCodeAttribute = "GeneratedCode";

    public static async Task<IReadOnlyList<Finding>> AnalyzeAsync(Solution solution, DuplicatesConfig config, string rootDirectory, CancellationToken cancellationToken = default)
    {
        // A file linked into several projects is one file, not a copy of itself.
        var trees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is { } path
                    && !trees.ContainsKey(path)
                    && await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false) is { } tree)
                {
                    trees[path] = tree;
                }
            }
        }

        return Analyze(trees.Values, config, rootDirectory);
    }

    /// <param name="rootDirectory">Directory the <c>excludePaths</c> globs are relative to; files outside it are ignored.</param>
    public static IReadOnlyList<Finding> Analyze(IEnumerable<SyntaxTree> trees, DuplicatesConfig config, string rootDirectory)
    {
        var excluded = DeadCodeAnalyzer.CreateMatcher(config.ExcludePaths);
        var lists = new List<StatementList>();
        foreach (var tree in trees.OrderBy(t => t.FilePath, StringComparer.Ordinal))
        {
            if (!DeadCodeAnalyzer.IsUnder(rootDirectory, tree.FilePath)
                || DeadCodeAnalyzer.Matches(excluded, rootDirectory, tree.FilePath)
                || (config.ExcludeGenerated && DeadCodeAnalyzer.IsGeneratedFile(tree)))
            {
                continue;
            }

            var collector = new ListCollector(config.ExcludeGenerated);
            collector.Visit(tree.GetRoot());
            // Source order: the walker visits a block before the blocks nested in it.
            lists.AddRange(collector.Lists.Select(statements => new StatementList(tree, statements)));
        }

        var groups = FindGroups(lists, config.MinStatements, config.MinTokens);
        return groups
            .Select(group => ToFinding(group, lists))
            .OrderBy(f => f.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(f => f.Location.StartLine)
            .ThenBy(f => f.Location.StartColumn)
            .ToList();
    }

    private readonly record struct WindowKey(ulong A, ulong B);

    private readonly record struct Occurrence(int List, int Start);

    private sealed class StatementList
    {
        public StatementList(SyntaxTree tree, IReadOnlyList<StatementSyntax> statements)
        {
            Tree = tree;
            Statements = statements;
            Hashes = new ulong[statements.Count];
            TokensBefore = new int[statements.Count + 1];
            for (var i = 0; i < statements.Count; i++)
            {
                Hashes[i] = Normalize(statements[i], out var tokens);
                TokensBefore[i + 1] = TokensBefore[i] + tokens;
            }
        }

        public SyntaxTree Tree { get; }

        public IReadOnlyList<StatementSyntax> Statements { get; }

        public ulong[] Hashes { get; }

        /// <summary>Prefix sums: <c>TokensBefore[i]</c> is the token count of the first <c>i</c> statements.</summary>
        public int[] TokensBefore { get; }

        public int Tokens(int start, int count) => TokensBefore[start + count] - TokensBefore[start];

        public TextSpan Span(int start, int count) =>
            TextSpan.FromBounds(Statements[start].SpanStart, Statements[start + count - 1].Span.End);
    }

    private sealed record CloneRange(int List, int Start, int Count, int Tokens);

    private sealed record Group(IReadOnlyList<CloneRange> Ranges)
    {
        public int Tokens => Ranges[0].Tokens;
    }

    /// <summary>Collects the statement list of every block and switch section, skipping generated members.</summary>
    private sealed class ListCollector(bool excludeGenerated) : CSharpSyntaxWalker
    {
        public List<IReadOnlyList<StatementSyntax>> Lists { get; } = [];

        public override void Visit(SyntaxNode? node)
        {
            if (node is not null && !(excludeGenerated && IsMarkedGenerated(node)))
            {
                base.Visit(node);
            }
        }

        public override void VisitBlock(BlockSyntax node)
        {
            Lists.Add(node.Statements);
            base.VisitBlock(node);
        }

        public override void VisitSwitchSection(SwitchSectionSyntax node)
        {
            Lists.Add(node.Statements);
            base.VisitSwitchSection(node);
        }

        /// <summary>Matches the attribute by name only: this analysis does not bind symbols.</summary>
        private static bool IsMarkedGenerated(SyntaxNode node) =>
            node is MemberDeclarationSyntax
            && node.ChildNodes().OfType<AttributeListSyntax>().SelectMany(list => list.Attributes).Any(attribute =>
            {
                var name = attribute.Name.ToString();
                name = name[(name.LastIndexOf('.') + 1)..].Trim();
                return name == GeneratedCodeAttribute || name == GeneratedCodeAttribute + "Attribute";
            });
    }

    /// <summary>
    /// Hash of the token kinds of a statement. Identifiers all share one kind and every literal is folded into one,
    /// so renamed variables and changed constants give the same hash; keywords and punctuation keep theirs.
    /// </summary>
    private static ulong Normalize(StatementSyntax statement, out int tokens)
    {
        var hash = 14695981039346656037UL;
        tokens = 0;
        foreach (var token in statement.DescendantTokens())
        {
            if (token.IsMissing)
            {
                continue;
            }

            tokens++;
            hash = unchecked((hash ^ (ulong)(int)FoldLiteral(token.Kind())) * 1099511628211UL);
        }

        return hash;
    }

    private static SyntaxKind FoldLiteral(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.NumericLiteralToken
                or SyntaxKind.StringLiteralToken
                or SyntaxKind.CharacterLiteralToken
                or SyntaxKind.InterpolatedStringTextToken
                or SyntaxKind.SingleLineRawStringLiteralToken
                or SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.Utf8StringLiteralToken
                or SyntaxKind.Utf8SingleLineRawStringLiteralToken
                or SyntaxKind.Utf8MultiLineRawStringLiteralToken => SyntaxKind.NumericLiteralToken,
            _ => kind,
        };

    private static WindowKey KeyOf(StatementList list, int start, int length)
    {
        ulong a = 17, b = 1469598103934665603UL;
        for (var i = start; i < start + length; i++)
        {
            a = unchecked((a ^ list.Hashes[i]) * 1099511628211UL);
            b = unchecked((b * 31) + list.Hashes[i]);
        }

        return new WindowKey(a, b);
    }

    private static List<Group> FindGroups(List<StatementList> lists, int length, int minTokens)
    {
        // Every window of `length` consecutive statements, grouped by hash. Occurrences stay in (list, start) order.
        var keys = new WindowKey?[lists.Count][];
        var occurrences = new Dictionary<WindowKey, List<Occurrence>>();
        for (var l = 0; l < lists.Count; l++)
        {
            var list = lists[l];
            var windows = Math.Max(0, list.Statements.Count - length + 1);
            keys[l] = new WindowKey?[windows];
            for (var i = 0; i < windows; i++)
            {
                if (list.Tokens(i, length) < minTokens)
                {
                    continue;
                }

                var key = KeyOf(list, i, length);
                keys[l][i] = key;
                if (!occurrences.TryGetValue(key, out var found))
                {
                    occurrences[key] = found = [];
                }

                found.Add(new Occurrence(l, i));
            }
        }

        // A window continues the previous one when all its occurrences are the previous ones moved by one statement.
        // Walking such chains gives the maximal repeated ranges instead of one report per window.
        var groups = new List<Group>();
        var seen = new HashSet<Occurrence>();
        for (var l = 0; l < lists.Count; l++)
        {
            for (var i = 0; i < keys[l].Length; i++)
            {
                if (keys[l][i] is not { } key || occurrences[key].Count < 2 || seen.Contains(new Occurrence(l, i)))
                {
                    continue;
                }

                var starts = occurrences[key];
                if (i > 0 && keys[l][i - 1] is { } previous && Continues(occurrences[previous], starts))
                {
                    continue;
                }

                var extra = 0;
                var current = starts;
                while (i + extra + 1 < keys[l].Length
                    && keys[l][i + extra + 1] is { } next
                    && occurrences[next].Count >= 2
                    && Continues(current, occurrences[next]))
                {
                    current = occurrences[next];
                    extra++;
                }

                seen.UnionWith(starts);
                var count = length + extra;
                var ranges = new List<CloneRange>();
                foreach (var start in starts)
                {
                    // Repetitive code makes copies overlap inside one list: keep the first of each overlapping pair.
                    if (ranges.Count > 0 && ranges[^1].List == start.List && ranges[^1].Start + ranges[^1].Count > start.Start)
                    {
                        continue;
                    }

                    ranges.Add(new CloneRange(start.List, start.Start, count, lists[start.List].Tokens(start.Start, count)));
                }

                if (ranges.Count >= 2)
                {
                    groups.Add(new Group(ranges));
                }
            }
        }

        return RemoveNested(groups, lists);
    }

    private static bool Continues(List<Occurrence> previous, List<Occurrence> next)
    {
        if (previous.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < previous.Count; i++)
        {
            if (previous[i].List != next[i].List || previous[i].Start + 1 != next[i].Start)
            {
                return false;
            }
        }

        return true;
    }

    // shortcut: quadratic in the number of groups; index the ranges by file if a solution has tens of thousands of groups.
    /// <summary>A group whose copies all sit inside the copies of a larger group (e.g. a repeated loop body in a repeated method) adds nothing.</summary>
    private static List<Group> RemoveNested(List<Group> groups, List<StatementList> lists)
    {
        var spans = groups.Select(g => g.Ranges.Select(r => (Tree: lists[r.List].Tree, Span: lists[r.List].Span(r.Start, r.Count))).ToList()).ToList();
        var result = new List<Group>();
        for (var g = 0; g < groups.Count; g++)
        {
            var nested = false;
            for (var h = 0; h < groups.Count && !nested; h++)
            {
                nested = h != g
                    && groups[h].Tokens > groups[g].Tokens
                    && spans[g].All(inner => spans[h].Any(outer => outer.Tree == inner.Tree && outer.Span.Contains(inner.Span)));
            }

            if (!nested)
            {
                result.Add(groups[g]);
            }
        }

        return result;
    }

    private static Finding ToFinding(Group group, List<StatementList> lists)
    {
        var locations = group.Ranges
            .Select(range =>
            {
                var list = lists[range.List];
                var span = list.Tree.GetText().Lines.GetLinePositionSpan(list.Span(range.Start, range.Count));
                return new SourceLocation(
                    list.Tree.FilePath,
                    span.Start.Line + 1,
                    span.Start.Character + 1,
                    span.End.Line + 1,
                    span.End.Character + 1);
            })
            .OrderBy(l => l.FilePath, StringComparer.Ordinal)
            .ThenBy(l => l.StartLine)
            .ThenBy(l => l.StartColumn)
            .ToList();

        var others = locations.Count - 1;
        var message = $"These {group.Ranges[0].Count} statements are repeated in {others} other {(others == 1 ? "place" : "places")} of the solution.";
        return new Finding(CliRules.DuplicatedCode.Id, message, CliRules.DuplicatedCode.DefaultLevel, locations[0], locations.Skip(1).ToList());
    }
}
