using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.UselessCatch;

internal static class UselessCatchAnalysis
{
    /// <summary>
    /// A catch clause without filter whose block is only <c>throw;</c>, and that no later clause relies on: removing it
    /// sends the exception to the same outer handler.
    /// </summary>
    public static bool IsUseless(CatchClauseSyntax clause, SemanticModel model, CancellationToken cancellationToken)
    {
        if (clause.Filter is not null
            || clause.Parent is not TryStatementSyntax tryStatement
            || clause.Block.Statements.Count != 1
            || clause.Block.Statements[0] is not ThrowStatementSyntax { Expression: null })
        {
            return false;
        }

        var caught = clause.Declaration is null ? null : model.GetTypeInfo(clause.Declaration.Type, cancellationToken).Type;
        var index = tryStatement.Catches.IndexOf(clause);
        for (var i = index + 1; i < tryStatement.Catches.Count; i++)
        {
            if (Shields(caught, tryStatement.Catches[i], model, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    // A later clause that could catch what this one catches would see it if the clause was removed. Unknown types count as shielded.
    private static bool Shields(ITypeSymbol? caught, CatchClauseSyntax later, SemanticModel model, CancellationToken cancellationToken)
    {
        if (later.Declaration is null || caught is null || caught.TypeKind == TypeKind.Error)
        {
            return true;
        }

        var laterType = model.GetTypeInfo(later.Declaration.Type, cancellationToken).Type;
        if (laterType is null || laterType.TypeKind == TypeKind.Error)
        {
            return true;
        }

        for (var type = caught; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, laterType))
            {
                return true;
            }
        }

        return false;
    }
}
