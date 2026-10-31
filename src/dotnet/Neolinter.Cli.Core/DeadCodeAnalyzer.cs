using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Neolinter.Cli.Core;

/// <summary>
/// NLD0001: finds types and members of any accessibility that nothing in the solution references.
/// Built-in exclusions are listed in docs/architecture.md ("dead-code").
/// </summary>
public static class DeadCodeAnalyzer
{
    private const string ModuleInitializerAttribute = "System.Runtime.CompilerServices.ModuleInitializerAttribute";
    private const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";
    private const string SerializationInfo = "System.Runtime.Serialization.SerializationInfo";

    private static readonly string[] GeneratedFileSuffixes = [".g.cs", ".g.i.cs", ".designer.cs", ".generated.cs"];

    /// <param name="rootDirectory">Directory the <c>excludePaths</c> and <c>testProjects</c> globs are relative to.</param>
    public static async Task<IReadOnlyList<Finding>> AnalyzeAsync(Solution solution, DeadCodeConfig config, string rootDirectory, CancellationToken cancellationToken = default)
    {
        var excludedPaths = CreateMatcher(config.ExcludePaths);
        var testMatcher = CreateMatcher(config.TestProjects);
        var testProjects = solution.Projects
            .Where(p => p.FilePath is not null && Matches(testMatcher, rootDirectory, p.FilePath))
            .Select(p => p.Id)
            .ToHashSet();

        var candidates = new List<ISymbol>();
        var used = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
        foreach (var project in solution.Projects.Where(p => !testProjects.Contains(p.Id)))
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            // Without references every framework type is an error type and nearly everything would look unused.
            if (compilation.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error)
            {
                throw new InvalidOperationException($"Project '{project.Name}' has no framework references. Run 'dotnet restore' first.");
            }

            var context = new ProjectContext(
                compilation,
                config,
                excludedPaths,
                rootDirectory,
                compilation.GetEntryPoint(cancellationToken),
                !config.IncludePublicApi && IsPackableLibrary(project));
            CollectCandidates(compilation.Assembly.GlobalNamespace, context, candidates, used);
        }

        await Parallel.ForEachAsync(candidates, cancellationToken, async (symbol, ct) =>
        {
            if (await IsReferencedAsync(symbol, solution, config.CountTestReferences ? null : testProjects, ct).ConfigureAwait(false))
            {
                used[symbol] = true;
            }
        }).ConfigureAwait(false);

        // A type is in use when it, or one of its members, is referenced or used by the runtime or a framework.
        // An unused type is reported once, not member by member.
        var usedTypes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var symbol in used.Keys)
        {
            for (var type = symbol as INamedTypeSymbol ?? symbol.ContainingType; type is not null; type = type.ContainingType)
            {
                usedTypes.Add(type);
            }
        }

        var candidateSet = candidates.ToHashSet(SymbolEqualityComparer.Default);
        return candidates
            .Where(symbol => !used.ContainsKey(symbol) && !usedTypes.Contains(symbol) && !HasUnusedContainer(symbol, usedTypes, candidateSet))
            .Select(ToFinding)
            .OrderBy(f => f.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(f => f.Location.StartLine)
            .ThenBy(f => f.Location.StartColumn)
            .ToList();
    }

    private sealed record ProjectContext(
        Compilation Compilation,
        DeadCodeConfig Config,
        Matcher ExcludedPaths,
        string RootDirectory,
        IMethodSymbol? EntryPoint,
        bool PublicApiIsUsed);

    private enum Role
    {
        /// <summary>Not a declaration the rule looks at, or only called through another member (overrides).</summary>
        Ignored,

        /// <summary>Reported unless something references it.</summary>
        Candidate,

        /// <summary>Used by the runtime, a framework or external code: never reported, keeps its containing types in use.</summary>
        Root,
    }

    /// <param name="roots">Receives the <see cref="Role.Root"/> symbols.</param>
    private static void CollectCandidates(INamespaceOrTypeSymbol container, ProjectContext context, List<ISymbol> candidates, ConcurrentDictionary<ISymbol, bool> roots)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                CollectCandidates(ns, context, candidates, roots);
                continue;
            }

            if (member is INamedTypeSymbol type && IsExcludedType(type, context))
            {
                continue;
            }

            switch (Classify(member, context))
            {
                case Role.Candidate:
                    candidates.Add(member);
                    break;
                case Role.Root:
                    roots[member] = true;
                    break;
            }

            if (member is INamedTypeSymbol nested)
            {
                CollectCandidates(nested, context, candidates, roots);
            }
        }
    }

    /// <summary>Exclusions that also cover every member of the type.</summary>
    private static bool IsExcludedType(INamedTypeSymbol type, ProjectContext context) =>
        !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, context.Compilation.Assembly)
        || type.IsImplicitlyDeclared
        || HasAttribute(type, context.Config.ExcludeAttributes)
        || HasAttribute(type, [GeneratedCodeAttribute])
        || DerivesFromEntryPointBase(type, context.Config.EntryPointBaseTypes);

    private static Role Classify(ISymbol symbol, ProjectContext context)
    {
        if (symbol.IsImplicitlyDeclared || symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree is not { } tree)
        {
            return Role.Ignored;
        }

        var kindIsReported = symbol switch
        {
            INamedTypeSymbol => true,
            IMethodSymbol method => method.MethodKind switch
            {
                MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion => true,
                // Parameterless constructors are often required by serializers or generic constraints, or only block instantiation.
                MethodKind.Constructor => method.Parameters.Length > 0,
                _ => false,
            },
            IPropertySymbol or IFieldSymbol or IEventSymbol => true,
            _ => false,
        };

        if (!kindIsReported || symbol.IsOverride || IsInterfaceImplementation(symbol))
        {
            return Role.Ignored;
        }

        var isRoot = SymbolEqualityComparer.Default.Equals(symbol, context.EntryPoint)
            || (symbol is IMethodSymbol constructor && IsSerializationConstructor(constructor))
            || HasAttribute(symbol, context.Config.ExcludeAttributes)
            || HasAttribute(symbol, [ModuleInitializerAttribute, GeneratedCodeAttribute])
            || IsGeneratedFile(tree)
            || !IsUnder(context.RootDirectory, tree.FilePath)
            || Matches(context.ExcludedPaths, context.RootDirectory, tree.FilePath)
            || (context.PublicApiIsUsed && IsVisibleOutsideAssembly(symbol));
        return isRoot ? Role.Root : Role.Candidate;
    }

    /// <summary>Projects outside the solution directory (loaded through project references) are not reported.</summary>
    private static bool IsUnder(string directory, string filePath)
    {
        var relative = Path.GetRelativePath(directory, filePath);
        return !Path.IsPathRooted(relative) && !relative.StartsWith("..", StringComparison.Ordinal);
    }

    private static async Task<bool> IsReferencedAsync(ISymbol symbol, Solution solution, HashSet<ProjectId>? ignoredProjects, CancellationToken cancellationToken)
    {
        var declarations = symbol.DeclaringSyntaxReferences;
        foreach (var referenced in await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false))
        {
            foreach (var reference in referenced.Locations)
            {
                var span = reference.Location.SourceSpan;
                var tree = reference.Location.SourceTree;
                var insideOwnDeclaration = declarations.Any(d => d.SyntaxTree == tree && d.Span.Contains(span));
                if (!insideOwnDeclaration
                    && ignoredProjects?.Contains(reference.Document.Project.Id) != true
                    && !await IsInDocumentationAsync(reference, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A <c>cref</c> in an XML comment mentions a member but does not use it.</summary>
    private static async Task<bool> IsInDocumentationAsync(ReferenceLocation reference, CancellationToken cancellationToken)
    {
        var root = await reference.Location.SourceTree!.GetRootAsync(cancellationToken).ConfigureAwait(false);
        return root.FindToken(reference.Location.SourceSpan.Start, findInsideTrivia: true).IsPartOfStructuredTrivia();
    }

    private static bool HasUnusedContainer(ISymbol symbol, HashSet<ISymbol> usedTypes, HashSet<ISymbol> candidates)
    {
        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (!usedTypes.Contains(type) && candidates.Contains(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInterfaceImplementation(ISymbol symbol) =>
        symbol switch
        {
            IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 } => true,
            IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 } => true,
            IEventSymbol { ExplicitInterfaceImplementations.Length: > 0 } => true,
            _ => symbol.ContainingType is { } type && type.AllInterfaces
                .SelectMany(i => i.GetMembers())
                .Any(m => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(m), symbol)),
        };

    private static bool IsSerializationConstructor(IMethodSymbol constructor) =>
        constructor.Parameters.Length == 2 && FullName(constructor.Parameters[0].Type) == SerializationInfo;

    private static bool DerivesFromEntryPointBase(INamedTypeSymbol type, IReadOnlyList<string> baseTypes)
    {
        if (baseTypes.Count == 0)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (baseTypes.Contains(FullName(current)))
            {
                return true;
            }
        }

        return type.AllInterfaces.Any(i => baseTypes.Contains(FullName(i)));
    }

    /// <summary>Accepts the attribute full name with or without the <c>Attribute</c> suffix.</summary>
    private static bool HasAttribute(ISymbol symbol, IReadOnlyList<string> names) =>
        names.Count > 0 && symbol.GetAttributes().Any(a => a.AttributeClass is { } attribute
            && names.Any(n => FullName(attribute) == n || FullName(attribute) == n + "Attribute"));

    private static bool IsVisibleOutsideAssembly(ISymbol symbol)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsGeneratedFile(SyntaxTree? tree) =>
        tree is null
        || GeneratedFileSuffixes.Any(s => tree.FilePath.EndsWith(s, StringComparison.OrdinalIgnoreCase))
        || tree.GetRoot().GetLeadingTrivia().ToString().Contains("<auto-generated", StringComparison.Ordinal);

    /// <summary>Namespace-qualified name without type arguments, e.g. <c>System.Collections.Generic.List</c>.</summary>
    private static string FullName(ITypeSymbol type)
    {
        var prefix = type.ContainingType is { } outer
            ? FullName(outer)
            : type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null;
        return prefix is null ? type.Name : $"{prefix}.{type.Name}";
    }

    private static Matcher CreateMatcher(IReadOnlyList<string> globs)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(globs);
        return matcher;
    }

    private static bool Matches(Matcher matcher, string rootDirectory, string filePath) =>
        matcher.Match(rootDirectory, filePath).HasMatches;

    /// <summary>A library that <c>dotnet pack</c> would publish: its public members may be used by other code bases.</summary>
    private static bool IsPackableLibrary(Project project) =>
        project.CompilationOptions?.OutputKind == OutputKind.DynamicallyLinkedLibrary
        && project.FilePath is not null
        && string.Equals(GetMSBuildProperty(project.FilePath, "IsPackable"), "true", StringComparison.OrdinalIgnoreCase);

    // shortcut: one sequential `dotnet msbuild` per library project (about a second each); batch them if large solutions need it.
    // The workspace does not expose MSBuild properties, so ask the SDK; this only runs when includePublicApi is false.
    private static string GetMSBuildProperty(string projectPath, string property)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "msbuild", projectPath, $"-getProperty:{property}", "-nologo" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the dotnet command.");
        var error = process.StandardError.ReadToEndAsync();
        var value = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0
            ? value
            : throw new InvalidOperationException($"Could not evaluate {projectPath}: {error.Result.Trim()}");
    }

    private static Finding ToFinding(ISymbol symbol)
    {
        var location = symbol.Locations.First(l => l.IsInSource);
        var span = location.GetLineSpan();
        var kind = symbol switch
        {
            INamedTypeSymbol type => type.TypeKind.ToString().ToLowerInvariant(),
            IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
            _ => symbol.Kind.ToString().ToLowerInvariant(),
        };
        var name = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

        return new Finding(
            CliRules.UnusedMember.Id,
            $"The {kind} '{name}' is not used anywhere in the solution.",
            CliRules.UnusedMember.DefaultLevel,
            new SourceLocation(
                span.Path,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                span.EndLinePosition.Line + 1,
                span.EndLinePosition.Character + 1));
    }
}
