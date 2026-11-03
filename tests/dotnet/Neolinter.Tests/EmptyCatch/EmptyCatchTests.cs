using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.EmptyCatch;

namespace Neolinter.Tests.EmptyCatch;

/// <summary>NLR0028 tests (report only, no code fix).</summary>
public class EmptyCatchTests
{
    private static Task VerifyAsync(string source, string? editorConfig = null)
    {
        var test = new CSharpAnalyzerTest<EmptyCatchAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        if (editorConfig is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\n" + editorConfig + "\n"));
        }

        return test.RunAsync();
    }

    private const string Header = "using System;\nusing System.IO;\nusing System.Threading;\nusing System.Threading.Tasks;\n\n";

    private static string Body(string catchClauses) =>
        Header + "class C\n{\n    void M()\n    {\n        try\n        {\n            Console.WriteLine();\n        }\n        " + catchClauses + "\n    }\n}\n";

    // --- Reported -------------------------------------------------------------------

    [Fact]
    public Task EmptyCatchWithType() => VerifyAsync(Body("{|NLR0028:catch|} (IOException)\n        {\n        }"));

    [Fact]
    public Task EmptyCatchWithVariable() => VerifyAsync(Body("{|NLR0028:catch|} (Exception e)\n        {\n        }"));

    [Fact]
    public Task EmptyCatchWithoutType() => VerifyAsync(Body("{|NLR0028:catch|}\n        {\n        }"));

    [Fact]
    public Task EmptyBlockOnOneLine() => VerifyAsync(Body("{|NLR0028:catch|} { }"));

    [Fact]
    public Task EmptyCatchWithFilterIsReported() => VerifyAsync(Body("{|NLR0028:catch|} (IOException e) when (e.HResult != 0)\n        {\n        }"));

    [Fact]
    public Task EachEmptyCatchOfSeveralIsReported() => VerifyAsync(
        Body("{|NLR0028:catch|} (IOException)\n        {\n        }\n        catch (ArgumentException)\n        {\n            throw;\n        }\n        {|NLR0028:catch|}\n        {\n        }"));

    [Fact]
    public Task NestedEmptyCatchInsideCatchIsReported() => VerifyAsync(
        Body("catch (IOException)\n        {\n            try\n            {\n                Console.WriteLine();\n            }\n            {|NLR0028:catch|}\n            {\n            }\n        }"));

    [Fact]
    public Task CommentOnlyIsReportedWhenNotAllowed() => VerifyAsync(
        Body("{|NLR0028:catch|} (IOException)\n        {\n            // ignored on purpose\n        }"),
        "nlr0028.allow_comment_only = false");

    [Fact]
    public Task MultiLineCommentIsReportedWhenNotAllowed() => VerifyAsync(
        Body("{|NLR0028:catch|} { /* ignored */ }"),
        "nlr0028.allow_comment_only = false");

    // --- Not reported ---------------------------------------------------------------

    [Fact]
    public Task NotReportedCommentOnly() => VerifyAsync(Body("catch (IOException)\n        {\n            // ignored on purpose\n        }"));

    [Fact]
    public Task NotReportedMultiLineComment() => VerifyAsync(Body("catch { /* ignored */ }"));

    [Theory]
    [InlineData("nlr0028.allow_comment_only = true")]
    [InlineData("nlr0028.allow_comment_only = maybe")]
    public Task NotReportedCommentOnlyWithAllowedOrInvalidOption(string editorConfig) => VerifyAsync(
        Body("catch (IOException)\n        {\n            // ignored\n        }"), editorConfig);

    [Fact]
    public Task NotReportedEmptyStatementOnly() => VerifyAsync(Body("catch (IOException)\n        {\n            ;\n        }"));

    [Fact]
    public Task NotReportedBlockWithStatement() => VerifyAsync(Body("catch (IOException e)\n        {\n            Console.WriteLine(e);\n        }"));

    [Fact]
    public Task NotReportedRethrow() => VerifyAsync(Body("catch (IOException)\n        {\n            throw;\n        }"));

    [Fact]
    public Task NotReportedOperationCanceledException() => VerifyAsync(Body("catch (OperationCanceledException)\n        {\n        }"));

    [Fact]
    public Task NotReportedTaskCanceledException() => VerifyAsync(Body("catch (TaskCanceledException)\n        {\n        }"));

    [Fact]
    public Task NotReportedCancellationWithFilterAndVariable() => VerifyAsync(
        Body("catch (OperationCanceledException e) when (e.CancellationToken.IsCancellationRequested)\n        {\n        }"));

    [Fact]
    public Task NotReportedDerivedCancellationException() => VerifyAsync(
        Header + "class MyCancel : OperationCanceledException { }\n\nclass C\n{\n    void M()\n    {\n        try\n        {\n            Console.WriteLine();\n        }\n        catch (MyCancel)\n        {\n        }\n    }\n}\n");

    [Fact]
    public Task NotReportedEmptyFinally() => VerifyAsync(
        Header + "class C\n{\n    void M()\n    {\n        try\n        {\n            Console.WriteLine();\n        }\n        finally\n        {\n        }\n    }\n}\n");
}
