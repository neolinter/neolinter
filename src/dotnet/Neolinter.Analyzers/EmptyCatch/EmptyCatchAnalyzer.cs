using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.EmptyCatch;

/// <summary>NLR0028: a <c>catch</c> block without any statement swallows the exception silently.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyCatchAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0028";
    public const string AllowCommentOnlyOption = "nlr0028.allow_comment_only";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Empty catch block",
        messageFormat: "This 'catch' block is empty: the exception is swallowed; handle it, log it, rethrow it or explain why it is ignored",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An empty catch block hides failures; handle or log the exception, rethrow it, or at least document why it is safe to ignore.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0028.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.CatchClause);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Block.Statements.Count > 0)
        {
            return;
        }

        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(catchClause.SyntaxTree);
        if (GetAllowCommentOnly(options) && HasComment(catchClause.Block))
        {
            return;
        }

        if (IsCancellation(catchClause, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, catchClause.CatchKeyword.GetLocation()));
    }

    // Invalid values fall back to the default (true).
    internal static bool GetAllowCommentOnly(AnalyzerConfigOptions options) =>
        !(options.TryGetValue(AllowCommentOnlyOption, out var value)
          && string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    private static bool HasComment(BlockSyntax block) =>
        block.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    // Ignoring a cancellation on purpose is common: OperationCanceledException and its subtypes (TaskCanceledException).
    private static bool IsCancellation(CatchClauseSyntax catchClause, SemanticModel model, CancellationToken token)
    {
        if (catchClause.Declaration is not { } declaration)
        {
            return false;
        }

        var cancellation = model.Compilation.GetTypeByMetadataName("System.OperationCanceledException");
        for (var type = model.GetTypeInfo(declaration.Type, token).Type; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, cancellation))
            {
                return true;
            }
        }

        return false;
    }
}
