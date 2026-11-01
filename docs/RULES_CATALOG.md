# Rules catalog

Candidate rules for Neolinter. This is a backlog of ideas, not a promise: the maintainer picks what gets built
(see WP-12 in `docs/ROADMAP.md`). Rule ids are assigned when a rule is implemented (`NLR*` real time, `NLC*`
command line, one global numbering).

## How to use this file

- One rule per pull request, following `docs/ADDING_A_RULE.md`. Tick the box when the rule is merged and note its id.
- **Clean room** (`docs/CLEAN_ROOM.md`): these are general practices. Write the id, message and description from
  scratch; never copy or paraphrase another analyzer's catalog.
- Legend: **F** safe code fix (with fix all), **O** `.editorconfig` option, **CLI** command-line analysis,
  **△** overlaps what the .NET SDK analyzers or `.editorconfig` naming styles already provide: prefer a preset
  and a settings screen entry over a new analyzer, or skip.
- Defaults: suggestion for style and simplification, warning for probable bugs; every rule can be tuned with
  `dotnet_diagnostic.<ID>.severity`.
- Quality bar for a new rule: no false positives on the repository itself, negative tests (`NotReported*`), and
  playground examples.

## Already built

- [x] NLR0001 guard clause (early exit)
- [x] NLC0002 dead code (CLI)
- [x] NLC0003 duplicated code (CLI)
- [x] NLR0004 null-coalescing assignment `??=` (F)

## 1. Naming and conventions

- [ ] `Async` suffix on methods returning `Task` or `ValueTask` (F, O)
- [ ] No `Async` suffix on methods that do not return a task (F)
- [ ] Interfaces `I…`, type parameters `T…`, attributes `…Attribute`, exceptions `…Exception` (F) △
- [ ] Private fields `_camelCase`, constants, types and members PascalCase, parameters and locals camelCase (F, O) △
- [ ] Acronym casing (`Id`, `Url`) (F, O)
- [ ] Boolean members prefixed `Is`/`Has`/`Can` (O)
- [ ] Event handler naming (`On…`, `…Handler`) (O)
- [ ] `[Flags]` enums plural, other enums singular
- [ ] File name matches the type, one public type per file, namespace matches the folder (CLI, F)
- [ ] No Hungarian notation, no underscores in public names, no names hiding a BCL type
- [ ] Test method naming (O)

## 2. Asynchronous code

- [ ] `ConfigureAwait(false)` in libraries, or its absence in applications (O)
- [ ] `async void` outside event handlers
- [ ] Blocking `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`
- [ ] Fire-and-forget task, ignored task
- [ ] `async` method without `await`
- [ ] `CancellationToken`: last parameter, propagated to inner calls
- [ ] `Thread.Sleep` in asynchronous code, use `Task.Delay` (F)
- [ ] `await` inside a `lock`
- [ ] Needless `Task.Run` around code that is already asynchronous
- [ ] `ValueTask` awaited twice, `Task.Factory.StartNew` instead of `Task.Run`
- [ ] Missing `await using` / `IAsyncDisposable`, `TaskCompletionSource` without `RunContinuationsAsynchronously`
- [ ] Return the task directly instead of `async`/`await` when trivial (O)

## 3. Probable bugs

- [ ] `throw ex;` instead of `throw;` (F)
- [ ] Self-assignment, floating-point `==`, comparison with `NaN`
- [ ] Always-true or always-false conditions, unreachable code, `<=` against `Length` (off by one)
- [ ] `string.Format` argument mismatch
- [ ] `Equals` without `GetHashCode`, mutable struct △
- [ ] Collection modified during `foreach`
- [ ] Ignored return value (`string.Replace`, LINQ, immutable types)
- [ ] Multiple enumeration of an `IEnumerable`
- [ ] `lock(this)`, lock on a string or `typeof`, double-checked locking
- [ ] `new Random()` in a loop, `DateTime.Now` instead of `UtcNow` (O)
- [ ] Too broad `catch (Exception)` (O), swallowed exception, useless `catch { throw; }` (F), empty `finally`
- [ ] Empty `catch` block (decide whether a comment-only block is accepted)
- [ ] Undisposed `IDisposable`, disposable field without `Dispose` △
- [ ] `switch` on an enum missing cases, integer division used in a floating-point calculation

## 4. Simplification and readability (most safe code fixes)

- [ ] `x == true` / `x == false` (F; skip `bool?`)
- [ ] `if (x) return true; else return false;` (F)
- [ ] `x ? true : false` (F)
- [ ] Nested `if` without `else` merged with `&&` (F)
- [ ] `else` after a branch that always exits (F; check overlap with NLR0001 first)
- [ ] `is null` / `is not null` instead of `== null` / `!= null` (F, O)
- [ ] `Count() > 0` to `Any()` (F), `Where(p).Any()` to `Any(p)` (F), `Where(p).First()` to `First(p)` (F)
- [ ] `s == null || s == ""` to `string.IsNullOrEmpty` (F)
- [ ] `ContainsKey` then indexer to `TryGetValue` (F)
- [ ] `as` plus null check to a pattern (F), `is` plus cast to a pattern (F)
- [ ] `switch` statement to `switch` expression (F, O), `if`/`else` assignment to ternary (F, O)
- [ ] Concatenation and `string.Format` to interpolation (F), interpolation without expression (F)
- [ ] Redundant cast, `ToString()` or parentheses (F), redundant `this.` (F, O), trailing `return;` (F)
- [ ] `ToList()` before a `foreach` (F), target-typed `new()` (F, O)
- [ ] Collection expressions, primary constructors, file-scoped namespaces, `using` declarations (F, O) △
- [ ] `readonly`, `sealed`, `static`, `const` when possible (F) △

## 5. Design and maintainability (metrics)

- [ ] Cyclomatic and cognitive complexity, method length, nesting depth (CLI, thresholds in `neolinter.json`)
- [ ] Too many parameters, too many constructor dependencies (O), oversized class, oversized file, deep inheritance
- [ ] Boolean flag parameter, `out`/`ref` parameters, public field, public mutable collection
- [ ] Method returning `null` for a collection, too many `return`s, huge `switch`
- [ ] Magic numbers (O), duplicated string literals, commented-out code, `#region`
- [ ] Unused parameters (CLI), unused private members (covered by NLC0002)
- [ ] `TODO`, `FIXME`, `HACK` inventory (CLI)

## 6. Public API design

- [ ] Missing XML documentation (O), `[Obsolete]` without message, `[Flags]` values that are not powers of two
- [ ] Exposed `List<T>` instead of an interface, ambiguous overloads with optional parameters
- [ ] Unsafe event invocation, use `?.Invoke` (F)
- [ ] Missing nullability annotations, `ArgumentNullException.ThrowIfNull` (F)
- [ ] API diff between two versions to detect breaking changes (CLI, large effort)

## 7. Performance (information level, handle with care)

- [ ] String concatenation in a loop, use `StringBuilder` (F)
- [ ] `Contains` on a `List` in a loop, `Regex` built in a loop or not static
- [ ] Boxing, `Array.Empty`, explicit `StringComparison` (F), `string.Contains(char)` (F)
- [ ] `await` in a loop instead of `Task.WhenAll`, `Substring` where a `Span` works

## 8. Basic security

- [ ] Hard-coded secrets, passwords or connection strings (CLI), `http://` URLs
- [ ] MD5 and SHA1, `Random` used for security, `BinaryFormatter`, disabled certificate validation
- [ ] SQL built by concatenation, `Process.Start` with concatenated input, path traversal, external XML entities △

## 9. Test quality

- [ ] Test without assertion, `Assert.True(a == b)` to `Assert.Equal` (F), swapped `expected`/`actual`
- [ ] Ignored test without reason, `Thread.Sleep` in a test, `async void` test
- [ ] Too many assertions in one test (O), static state shared between tests

## 10. Project and solution (CLI analyses)

- [ ] Unused NuGet packages and project references, circular project dependencies
- [ ] Different versions of one package across projects, vulnerable packages
- [ ] Missing `Nullable` and `TreatWarningsAsErrors`, outdated target frameworks
- [ ] Packable project without license or README, inconsistent encoding and line endings

## 11. Entity Framework Core

Detected by type name (`Microsoft.EntityFrameworkCore.DbSet<T>`), without referencing the package.

- [x] NLR0006 LINQ operators applied after the query was loaded (`ToList().Select(...)`, `(await q.ToListAsync()).Where(...)`)
- [ ] Read-only query without `AsNoTracking` (O)
- [ ] N+1 queries: a query or lazy navigation inside a loop over another query's result
- [ ] Loaded result stored in a variable, then filtered or projected in memory (extension of NLR0006, more false positives)
- [ ] `Include` chain without `AsSplitQuery` on several collections, or `Include` followed by a projection that ignores it
- [ ] `SaveChanges` inside a loop instead of once after the loop
- [ ] Synchronous `ToList`, `First`, `SaveChanges` in an `async` method where an `Async` form exists
- [ ] `Contains` on an in-memory list inside a query that grows with the data (large `IN` clause)
- [ ] Raw SQL built by string interpolation or concatenation instead of the parameterized forms

## Out of scope

- Pure formatting (spacing, braces, `using` order): `dotnet format` and `.editorconfig` already do it. It can be
  exposed in the settings screens, not rebuilt as analyzers.
- Copying rules that the .NET SDK already ships (marked △): ship presets instead (for example "recommended",
  "strict", "library", "application") that set severities and naming styles in `.editorconfig`.

## Suggested order

1. Safe code fixes of section 4 (few false positives, high value).
2. Asynchronous code (section 2).
3. Probable bugs (section 3).
4. Presets, including naming, in the settings screens.
5. CLI metrics and project analyses (sections 5 and 10).
6. Security and performance last: they produce the most false positives.
