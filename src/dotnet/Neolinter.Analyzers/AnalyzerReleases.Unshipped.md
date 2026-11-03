; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NLR0001  | Style    | Info     | InvertIfAnalyzer
NLR0004  | Style    | Info     | NullCoalescingAssignmentAnalyzer
NLR0005  | Style    | Info     | BooleanComparisonAnalyzer
NLR0006  | Performance | Warning | EfCoreClientLinqAnalyzer
NLR0007  | Style    | Info     | NestedTernaryAnalyzer
NLR0008  | Style    | Info     | MergeNestedIfAnalyzer
NLR0009  | Style    | Info     | ElseAfterExitAnalyzer
NLR0010  | Style    | Info     | ExplicitBooleanAnalyzer
NLR0011  | Performance | Info  | CountToAnyAnalyzer
NLR0012  | Style    | Info     | WhereTerminalAnalyzer
NLR0013  | Style    | Info     | IsNullAnalyzer
NLR0014  | Style    | Info     | IsNullOrEmptyAnalyzer
NLR0015  | Performance | Info  | TryGetValueAnalyzer
NLR0016  | Style    | Info     | PatternCastAnalyzer
NLR0017  | Style    | Info     | ForeachWhereAnalyzer
NLR0018  | Style    | Info     | ForeachTakeWhileAnalyzer
NLR0019  | Style    | Info     | SwitchExpressionAnalyzer
NLR0020  | Style    | Info     | IfElseTernaryAnalyzer
NLR0021  | Style    | Info     | StringInterpolationAnalyzer
NLR0022  | Style    | Info     | StringFormatAnalyzer
NLR0023  | Reliability | Warning  | RethrowAnalyzer
NLR0024  | Reliability | Warning  | AsyncVoidAnalyzer
NLR0025  | Reliability | Warning  | BlockingTaskAnalyzer
NLR0026  | Reliability | Warning  | ThreadSleepAnalyzer
NLR0027  | Reliability | Warning  | AsyncWithoutAwaitAnalyzer
NLR0028  | Reliability | Warning  | EmptyCatchAnalyzer
NLR0029  | Reliability | Warning  | SelfAssignmentAnalyzer
NLR0030  | Reliability | Warning  | IgnoredReturnValueAnalyzer
NLR0031  | Reliability | Warning  | LockOnPublicObjectAnalyzer
NLR0032  | Reliability | Warning  | MultipleEnumerationAnalyzer
