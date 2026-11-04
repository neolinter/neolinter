# Neolinter

A lightweight, open-source code analyzer: real-time rules with quick fixes while you type, plus on-demand
solution-wide analyses (dead code, duplicated code). It starts with C#; more languages will follow.

Neolinter does not ship its own engine or background process. Real-time rules are Roslyn analyzers loaded by
the C# tooling you already use, configuration lives in `.editorconfig`, and the expensive analyses run from a
command line only when you ask for them.

> Status: early development (0.1). Thirty-five real-time rules (NLR0001, NLR0004, NLR0005, NLR0006, NLR0007, NLR0008, NLR0009, NLR0010, NLR0011, NLR0012, NLR0013, NLR0014, NLR0015, NLR0016, NLR0017, NLR0018, NLR0019, NLR0020, NLR0021, NLR0022, NLR0023, NLR0024, NLR0025, NLR0026, NLR0027, NLR0028, NLR0029, NLR0030, NLR0031, NLR0032, NLR0033, NLR0034, NLR0035, NLR0036, NLR0037); the command line analyses are not implemented yet.

## Real-time rules vs. command line

| | Real-time rules | Command line (`neolinter analyze`) |
|---|---|---|
| What | Local, per-file rules with code fixes | Solution-wide analyses: dead code, duplicated code |
| Where it runs | Inside the C# compiler and language server, as you type and on `dotnet build` | On demand, locally or in CI |
| Delivered as | NuGet package `Neolinter` | .NET tool `Neolinter.Cli` |
| Configured in | `.editorconfig` | `neolinter.json` |
| Output | Editor diagnostics, build warnings | SARIF 2.1.0 |
| Rule ids | `NLR0001`… | `NLC0002` (dead code), `NLC0003` (clones)… |

## Installation

### Real-time rules

Reference the package in each project, or once for all projects in `Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="Neolinter" Version="0.1.0-preview.1" PrivateAssets="all" />
</ItemGroup>
```

Then configure the rules in `.editorconfig`:

```ini
[*.cs]
# none | silent | suggestion | warning | error
dotnet_diagnostic.NLR0001.severity = warning
nlr0001.min_statements = 3
# optional, off by default: !(a && b) becomes !a || !b when the fix negates a condition
nlr0001.simplify_logical_operators = false
# always | never: braces around the return/continue/break/throw/yield break the fix inserts
nlr0001.guard_braces = always
```

NLR0001 options (all optional; the values above are the defaults):

| Key | Values | Effect |
|---|---|---|
| `nlr0001.min_statements` | integer >= 1 | Smallest `if` body (without `else`) that is reported. |
| `nlr0001.simplify_logical_operators` | `true` / `false` | The fix rewrites `!(a && b)` as `!a || !b` and `!(a || b)` as `!a && !b`. Only for the built-in operators on `bool`; nullable and user-defined operators are never rewritten. |
| `nlr0001.guard_braces` | `always` / `never` | `never` writes the inserted exit as one statement without braces (braces that carry comments are kept). |

NLR0004 has no option. It reports `if (x == null) x = v;` (also `null == x`, `x is null`, with or without braces) and
`x = x ?? v;`, and the fix writes `x ??= v;`. Exact rules, all meant to keep behaviour identical:

- needs C# 8 or later (read from the project's language version);
- the target is a local, a parameter, a field (not `volatile`) or a property, reached at most through `this`, type
  names and other fields, locals or parameters (no call, indexer or property in the middle of the path), so evaluating
  it more than once has no effect;
- the type is a reference type or a nullable value type (not a non-nullable value type, `dynamic` or an unconstrained
  type parameter);
- `x == null` is skipped when `==` is a user-defined operator (`x is null` is fine, `??=` never calls it);
- the `if` has no `else`, a single assignment as its body and no comment or `#if` inside; same for `x = x ?? v;`;
- the value is not `null`, `default` or a `throw` expression;
- `x = x ?? v;` on a property is reported only for a plain auto-property that is not `virtual`/`override`, because the
  long form calls the setter even when the value is not null and `??=` does not.

NLR0005 has no option. It reports a `bool` compared with a literal: `x == true`, `true == x` and `x != false` (fix: `x`),
`x == false`, `false == x` and `x != true` (fix: `!x`). The fix adds parentheses only where the meaning needs them
(`o is string == false` becomes `!(o is string)`, `a && b == false` becomes `a && !b`) and removes a double negation
(`!y == false` becomes `y`). Not reported: a `bool?` operand (its comparison has another meaning), a type that only
converts to `bool` or defines its own `==`, `true == false`, and a comparison with a comment or `#if` inside.

NLR0006 has no option and no code fix (moving an operator before the loading call may not translate to SQL). Its default
severity is `warning`. It reports a LINQ operator called directly on a loaded Entity Framework Core query, because the
database then returns every row and column and the application does the work:

- operators: `Select`, `SelectMany`, `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `GroupBy`,
  `Count`, `LongCount`, `Any`, `All`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`,
  `Skip`, `Take`, `Distinct`, `Sum`, `Min`, `Max`, `Average`;
- loading calls: `ToList`, `ToArray`, `ToDictionary`, `AsEnumerable` and `ToListAsync`, `ToArrayAsync`, `ToDictionaryAsync`,
  also written `(await q.ToListAsync()).Select(...)`, with `ConfigureAwait`, parentheses or `!` around the call;
- the query must start from a `Microsoft.EntityFrameworkCore.DbSet<T>` (a context property or `Set<T>()`), possibly
  followed by query operators such as `Include`, `AsNoTracking` or `Where`. Entity Framework Core is recognized by type
  name, so the analyzer needs no reference to it;
- known limits, on purpose: a loaded result stored in a variable and processed later is not followed, an `IQueryable`
  received as a parameter is not traced to its source, `Enumerable.Select(list, ...)` written as a static call is
  skipped, and only the first operator after the loading call is reported. Lists that did not come from such a query
  are never reported.

NLR0007 has no option (it reads the standard `csharp_prefer_braces` for the fix). It reports a conditional expression that
contains another one, in its condition, true branch or false branch (`a ? x : b ? y : z` included), once per outermost
expression, on the whole expression. A lambda, anonymous method or local function is a boundary. The fix is cautious and
only for `return <expr>;`, `x = <expr>;` and `T x = <expr>;` directly in a block or switch section; it writes an
`if` / `else if` / `else` chain of `return`s or assignments (`T x = ...` becomes `T x;` then the chain), evaluating the
conditions in the same order. No fix when:

- the nesting is not along the false branches (condition or true branch), or a branch contains a lambda;
- a branch does not have exactly the type of the whole expression (`c ? 1 : null`, `c ? 1 : 2.5`, branches of related
  types): converting the branches one by one could pick another conversion or overload;
- the declaration uses `var`, `const`, `using`, `ref` or several declarators, or the assignment target is not a name
  (`items[i++] = ...`, `Next().F = ...`, compound assignment): the target would be evaluated after the conditions;
- `x = ...` or `T x = ...` declares a variable in a condition (`o is string s`): its scope would shrink (`return` is fine);
- a comment or `#if` sits next to `?` or `:` (comments inside operands are kept), or the statement does not start its line.

Braces follow `csharp_prefer_braces` (`true`, `false`, `when_multiline`); without it the file's existing `if` / `else`
style decides. Brace placement, indentation and line endings follow the enclosing code.

NLR0008 has no option. It reports an `if` without `else` whose only statement is another `if` without `else`
(`if (a) { if (b) { ... } }`, `if (a) { if (b) ...; }`, `if (a) if (b) ...;`), on the outermost `if`, once for a chain of
three levels or more. The fix writes `if (a && b) ...` (`&&` keeps the evaluation order and the short-circuit), keeps the
inner body with its braces or lack of them, moves it up one indentation level per level removed, and adds parentheses
only where needed (`a || c` gives `(a || c) && b`). A pattern or `out` variable of the outer condition stays in scope for
the body. Not reported:

- either `if` has an `else`, or the outer `if` holds another statement;
- a condition is not exactly `bool` (a type with its own `true`, `false` and `&` could behave differently, and `dynamic`);
- the inner condition declares a variable named like an identifier of the outer condition (the merged condition would
  not compile: the variable is in scope in the whole condition);
- a comment or `#if` sits between the two `if` statements (the fix would drop it; comments above the outer `if`, inside
  the body, after the inner `)` and after the outer closing brace are kept).

With `nlr0001.min_statements = 1` the guard clause rule NLR0001 can also report the same outer `if` at the end of a
method or loop body. The two suggestions are alternatives: the NLR0001 fix leaves `if (!a) return;` then `if (b) ...`,
which NLR0008 does not report; the NLR0008 fix leaves one `if`, which NLR0001 may then turn into a single guard. They never
undo each other. With the default of 3 statements there is no overlap on the outer `if`, whose body is one statement.

NLR0009 has no option. It reports the `else` of `if (c) <exit> else <statement>` and `if (c) <exit> else { ... }` when the
`if` branch always leaves: it ends with `return`, `throw`, `break`, `continue`, `goto` or `yield break`, or is an
`if`/`else` whose branches all do. The fix removes the `else` and its braces and writes the statements after the `if`,
one level up, at the indentation of the `if`; comments inside the `else` and around the statements are kept, and so is
the file's own layout (tabs or spaces, line endings; a `} else {` or a one-line `if (c) return; else Run();` ends up on
separate lines). Not reported:

- `else if` (the chain is left alone) and an `if` that is the body of another statement without braces (dangling `else`)
  or that sits directly in a `switch` section (inside a block of a section it is reported);
- an empty `else`, a directive inside the `if`, a comment on the `else` keyword or next to its braces, or a statement
  of the `else` (after the first) that shares its line with another;
- the `if` does not start its line;
- a name declared in the `else` appears anywhere else in the enclosing block (a clash, or a later use of a field of the
  same name that would bind to the new local), or a `using` declaration in the `else` while code follows the `if` (its
  disposal would move later);
- an `else` that is a single exit after a longer branch (`if (c) { A(); B(); return; } else { return; }`): NLR0001
  reports it, and its fix removes the `else` by inverting the condition.

After the NLR0009 fix, NLR0001 can still suggest a guard for what is left (an `if` followed by a `return`); the
suggestions chain, they never undo each other.

NLR0010 has no option. It reports a `bool` condition that is spelled out as `true` and `false`: `c ? true : false`
(fixed to `c`), `c ? false : true` (`!c`), `if (c) return true; else return false;` and `if (c) return true;` followed
by `return false;` (`return c;`), and the same with the literals swapped (`return !c;`). The branches may have braces;
parentheses are added only where the meaning needs them, and comments and layout around the construct are kept. Not
reported:

- a condition that is not exactly `bool` (`dynamic`, or a type with its own `true` and `false` operators);
- a branch that does more than return the literal, a returned `(true)` or any other expression, two equal literals;
- a comment or directive inside the construct (between the `if`, its branches and the final `return`), since the fix
  would drop it;
- the final `return` that is not the statement right after the `if` (code or a label in between).

NLR0009 stays silent on `if (c) return true; else return false;` because this rule takes it and its fix leaves no
`else`.

NLR0011 has no option. It reports the LINQ `Count()` compared with 0 or 1 when only emptiness matters:
`x.Count() > 0`, `!= 0` and `>= 1` (fixed to `x.Any()`), `x.Count() == 0` and `< 1` (`!x.Any()`), the same with the number
on the left (`0 < x.Count()`), and `x.Count(p)` (`x.Any(p)`). `Queryable` (Entity Framework Core included) is covered
too. Not reported:

- the `Count` property, `Length`, and a `Count()` method of the type itself (only `System.Linq` is concerned);
- another number (`> 1`, `== 1`), `LongCount()`, a receiver with its own member called `Any`;
- a comment or directive next to the number or the operator, since the fix would drop it.

`Where(p).Any()` is taken by NLR0012.

NLR0012 has no option. It reports `x.Where(p)` followed by `Any()`, `First()`, `FirstOrDefault()`, `Single()`,
`SingleOrDefault()`, `Last()`, `LastOrDefault()`, `Count()` or `LongCount()` without argument, and writes the predicate
into that operator (`x.Any(p)`). `Queryable` (Entity Framework Core included) is covered too: the query stays
translatable. Not reported:

- the index overload `Where((x, i) => ...)`, and a terminal call that already has an argument;
- a receiver with its own member of the operator's name, or its own `Where`;
- a comment or directive on the part the fix drops (between `Where(...)` and the terminal call).

With NLR0011: `x.Where(p).Count() > 0` is reported by NLR0011 only. Its fix gives `x.Where(p).Any()`, which NLR0012
then offers to write `x.Any(p)`, so the two never offer competing fixes on the same code.

NLR0013 has no option. It reports `x == null` and `null == x` (fix: `x is null`) and `x != null` and `null != x`
(`x is not null`), which test for null whatever `==` does for the type. With an older language version only what can be
written is reported: nothing before C# 7, only `== null` for C# 7 and 8 (`is not null` needs C# 9). Not reported:

- a type that defines its own `==` or `!=` (`is null` would bypass it); `string`, nullable value types and plain
  reference types are fine;
- a value that cannot be null (`int`, `dynamic`, pointers);
- an expression tree: a lambda converted to `Expression<...>`, or a query over `IQueryable` (Entity Framework Core
  included), where `is null` is not allowed;
- a comment or directive inside the comparison, since the fix would drop it.

NLR0014 has no option. It reports `s == null || s == ""` (also `s is null`, `string.Empty`, `s.Length == 0`, either
operand order of `==`) as `string.IsNullOrEmpty(s)`, and `s != null && s != ""` (also `s is not null`, `s.Length != 0`,
`s.Length > 0`) as `!string.IsNullOrEmpty(s)`. The null test comes first, both tests are on the same string, written as a
name or a chain of member accesses, and inside a longer `||` or `&&` chain only the pair is replaced. Not reported:
`string.IsNullOrWhiteSpace`, a value that is not a `string`, a call or an indexer as the string, an expression tree
(Entity Framework Core included), and a comment or directive inside the pair. NLR0013 may also report the `s == null`
inside the pair; the two fixes can be applied in either order and give the same code.

NLR0015 has no option. It reports `ContainsKey(k)` followed by the indexer with the same key, in `if (d.ContainsKey(k))`
(without `else`), in `if (!d.ContainsKey(k)) ... else` (the reads are in the `else`) and in `d.ContainsKey(k) && ...` when
that chain is a whole `if` condition, a `return` value or an expression body, and rewrites it with
`d.TryGetValue(k, out var value)` and `value` in place of `d[k]` (`value1`, ... when the name is taken). The dictionary
and the key must be a name or a chain of member accesses (the key may be a constant) written the same way each time,
and the type needs an instance `TryGetValue(TKey, out TValue)` (`Dictionary`, `IDictionary`, `IReadOnlyDictionary`,
`ConcurrentDictionary`, ...). Not reported: C# 6 and older, an expression tree or a query clause, an indexer that is
assigned, incremented or passed by reference, a read inside a lambda or local function, a comment inside the indexer
brackets, a dictionary or key that is assigned, passed to a call or called (`Remove`, `Clear`, ...) or an `await`/`yield`
between the check and the last read, and a check that holds another `ContainsKey` (the inner one is reported first).
Calls that change the dictionary without naming it are not detected.

NLR0016 has no option. It reports `var x = e as T;` followed by `if (x != null)` (also `null != x`, `x is not null`, and
a longer `&&` chain that starts with the test) when `x` is not used after the `if` or in its `else`, and rewrites it as
`if (e is T x)`. From C# 9 it also reports the guard clause `var x = e as T; if (x == null) return;` with `x` used
afterwards and writes `if (e is not T x) return;`. It also reports `if (e is T) { ... (T)e ... }` when `e` is a local, a
parameter or a field and the guarded code casts it to the same type, and writes `if (e is T t)` with `t` in place of
every cast (named after the type in camel case, `dog1`, ... when the name is taken). Not reported: C# 6 and older, an
expression tree, a value type or type parameter target, a declaration with a comment or several variables, a type with its
own `==`/`!=` (`string` and delegates excepted), a value that is assigned or passed by reference in the guarded code, a
call, assignment or `await` before the last cast on a field, a cast inside a lambda or with a comment, a user-defined or
numeric cast, and guarded code that holds another `as` or `is` (the inner one is reported first).

NLR0017 has no option. It reports `if (c) continue;` (also `if (!c) continue;`, with or without braces, no `else`) at the very start of a
`foreach` body and moves it, with the guards that follow it, into `Where` on the source: `foreach (var x in items.Where(x => !c))`
(conditions joined with `&&`, `using System.Linq;` added when missing). The source must be an `IEnumerable<T>` (not `IQueryable`) of the
loop variable's type. Not reported: `await foreach`, a deconstruction, a multidimensional array or non-generic collection, nullable
elements, a source with its own `Where`, a condition that calls anything but `string` or primitive type methods or that assigns,
declares variables or holds a lambda, a condition that reads something the loop body assigns, a loop in a struct, a guard that is the
whole rest of the body, and a guard with a comment. NLR0001 produces `if (!c) continue;` in loops; NLR0017 then moves it.

NLR0018 has no option. It is the `break` twin of NLR0017: a leading `if (c) break;` (and the `break` guards right after it) becomes `foreach (var x in items.TakeWhile(x => !c))`, under the same conditions. Only a run of `break` guards at the start is moved; a `continue` guard before a `break` guard is not reported (the order of the two filters matters), while a `continue` guard after the moved `break` guards stays for NLR0017.

NLR0019 has no option. A `switch` statement whose sections each `return` a value (or `throw`), or each assign the same variable and `break`, becomes `return e switch { ... };` or `x = e switch { ... };`, one arm per line. It needs a `default` section as the last one (a `bool` switch with `case true` and `case false` also qualifies): without it the switch expression would throw where the statement does nothing. Labels, patterns and `when` guards are kept, `default` becomes `_`, and stacked labels become `a or b` (C# 9; before C# 9 such sections are not reported, and arms need a natural type since there is no target typing; before C# 8 nothing is reported). Nothing is reported either for sections with several statements, fallthrough or `goto case`, comments, multi-line parts, assignments to anything but a name or `this.` member, or when the arms would get another type.

NLR0020 has no option. `if (c) x = a; else x = b;` becomes `x = c ? a : b;`, and `if (c) return a; else return b;` (or `return b;` right after the `if`) becomes `return c ? a : b;`. The target must be a name, a `this.` member or a chain of members, the same on both sides; a branch longer than 60 characters or on several lines, comments, an `else if`, an `out var` or pattern variable in the condition of an assignment and a changed typing (the branches must convert to the target type, or, from C# 9, have no common type and be target-typed) are not reported. The shapes of two other rules are left to them: `true` and `false` in both branches (NLR0010), and a branch that is already a conditional expression (NLR0007, since the result would be nested). NLR0009 also reports the `else` of `if (c) return a; else return b;`; both fixes lead to the same place (NLR0020 gives `return c ? a : b;`, NLR0009 gives the `if (c) return a; return b;` form that NLR0020 then reports), so apply either first.

NLR0021 has no option. `"a" + x + "b"` becomes `$"a{x}b"`: a concatenation of at least two operands with at least one plain string literal and one value, from C# 6. Braces of a literal are doubled, and an operand with a top-level `:` or `,` (a conditional expression, a generic call) gets parentheses. Not reported: constants, verbatim, raw and UTF-8 literals, an interpolated string as operand, an operand with braces or on several lines, comments, a chain over several lines, expression trees and queries, user-defined operators. A chain that holds another chain in an operand is reported once the inner one is fixed.

NLR0022 has no option. `string.Format("a{0}b{1}", x, y)` becomes `$"a{x}b{y}"`, from C# 6, for a plain string literal as format. Placeholders may come in any order; an alignment and a format specifier are copied into the hole (`{0,10:F2}` gives `{x,10:F2}`), `{{` and `}}` stay, and an argument with a top-level `:` or `,` gets parentheses. Every argument must be used and every placeholder must have one; an argument used several times must be an identifier, a member access chain or a literal, and calls must be used once each, in order. Not reported: culture overloads, a format that is a constant, verbatim, raw or UTF-8 string or holds a unicode escape, an array passed as the arguments, named arguments, comments, calls over several lines, expression trees and queries. A call that holds another reportable call in an argument is reported once the inner one is fixed.

NLR0023 has no option and a warning severity. `throw e;` becomes `throw;` when `e` is the variable of the catch clause that directly encloses the statement: `throw e;` restarts the stack trace, `throw;` keeps the original one. Exception filters are fine. Not reported: a throw inside a lambda, anonymous method or local function, inside a `finally` within the catch, inside a nested catch clause, a variable assigned in the catch block, `throw new X(..., e)` and any other thrown expression.

NLR0024 has no option, no code fix and a warning severity. An `async` method, local function, lambda or anonymous method that returns `void` is reported: the caller can neither await it nor catch its exceptions. Return `Task` (`Func<Task>` for a lambda) instead. Not reported: a method with an `(object, EventArgs)` signature (the second parameter derives from `EventArgs`), an override, an implementation of an interface member, a method named as a delegate or event handler somewhere in the compilation (`button.Click += OnClick;`), and a lambda added to an event or converted to a delegate with an `(object, EventArgs)` signature.

NLR0025 has no option and a warning severity. Inside an `async` method, local function, lambda or anonymous method, `t.Result`, `t.Wait()` and `t.GetAwaiter().GetResult()` (also after `ConfigureAwait`) become `await t`, or `await t;` for a statement, with parentheses when the result is accessed (`(await t).Length`). `Task.WaitAll`, `Task.WaitAny` and `t.Wait(timeout)` are reported without a fix, as is any call inside a `lock`, an exception filter, a query expression, unsafe code or a method holding a ref struct local, where `await` is not possible. Not reported: code that is not itself async (a non-async lambda or local function inside an async method, a constructor, an accessor), a task awaited earlier in the same function (also through `Task.WhenAll` or `Task.WhenAny`), `Task.FromResult(...)`, `Task.CompletedTask`, and a `Result` member of another type.

NLR0026 has no option and a warning severity. Inside an `async` method, local function, lambda or anonymous method, `Thread.Sleep(int)` and `Thread.Sleep(TimeSpan)` become `await Task.Delay(...)` with the same argument; no cancellation token is added, so the behavior is unchanged. A call inside a `lock`, unsafe code or a method holding a ref struct local, where `await` is not possible, is reported without a fix. Not reported: code that is not itself async (a non-async lambda or local function inside an async method, a constructor, an accessor) and a zero delay (`Thread.Sleep(0)`, `Thread.Sleep(TimeSpan.Zero)`).

NLR0027 has no option and a warning severity. An `async` method, local function, lambda or anonymous method with no `await`, `await foreach` or `await using` of its own is reported (an await inside a nested lambda or local function does not count). For a method or local function returning `Task` or `Task<T>` the fix removes `async` and returns the task by hand (`Task.CompletedTask`, or `Task.FromResult<T>(...)` for each `return`); it is only offered when nothing in the body throws and there is no `using` or `try`, since without `async` an exception reaches the caller instead of the task. Lambdas, anonymous methods, `ValueTask` and those other bodies are reported without a fix. Not reported: `async void` (NLR0024), iterators, an empty body and a body that only throws.

NLR0028 has a warning severity and one option, `nlr0028.allow_comment_only` (default `true`). A `catch` clause whose block has no statement is reported, with or without a type or a `when` filter. A block holding only a comment is accepted because the comment documents the intent; with `false` it is reported too. A catch of `OperationCanceledException` or a subtype (`TaskCanceledException`) is never reported, and an empty `finally` is a separate rule. There is no code fix.

NLR0029 has no option and a warning severity. A simple assignment (`=`) whose two sides name the same local, parameter, field or auto-implemented property through the same receiver is reported (`x = x`, `this.a = a` where `a` is the field, `o.a = o.a`). The fix removes the statement; it is only offered when the assignment is a statement of a block. Not reported: compound assignments, different members or receivers (`this.a = a` where `a` is a parameter), receivers with a call or an indexer, and properties with a body or that can be overridden, whose getter or setter may have effects. Floating-point `==` and comparison with `NaN` are a separate, later rule.

NLR0030 has no option and a warning severity. An expression statement that calls a method known to only compute a value and drops the result is reported: an instance method of `string`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly` or of an immutable collection, a method of `Math`, `Convert`, `Path` or `System.Linq`, and any method marked `[Pure]` or `[MustUseReturnValue]`. The fix assigns the result back (`s = s.Replace(...)`); it is only offered when the receiver is a local, a parameter or a field that can be assigned and the call returns its type (so not for LINQ). Not reported: a result that is used or discarded with `_ =`, methods with an `out` or `ref` parameter, and user methods without the attributes.

NLR0031 has no option and a warning severity. A `lock` statement on an object that other code can also lock on is reported: `this` (unless the class is sealed or a private nested type), a string (literal, constant or any string expression), `typeof(T)` or any `System.Type` such as `GetType()`, and a value cast to `object` (a new box each time, so nothing is excluded). Report only, there is no code fix. Not reported: private object fields, other reference types, and locks on public fields. Double-checked locking is a separate, later rule.

NLR0032 has no option and a warning severity. A parameter, or a local initialized from a sequence, of type `IEnumerable`, `IEnumerable<T>`, `IQueryable` or `IQueryable<T>` that is enumerated more than once is reported once, on the first repeat: `foreach`, a consuming LINQ call (`Count`, `Any`, `First`, `ToList`...) or `GetEnumerator()`, also through deferred operators such as `Where`. Different branches, or a branch that returns first, are not multiple; an enumeration in a loop of a variable declared outside the loop is. The fix appends `.ToList()` to the initializer of an `IEnumerable<T>` local (the query then runs once, at the declaration); not for parameters or `IQueryable`. Not reported: lists, arrays and other collections, fields and properties, variables that are reassigned, passed as `ref` or `out` or tested with `is` or `as`, enumerations inside lambdas, and a variable only passed on to another method.

NLR0033 has no option and a warning severity. A class or struct that overrides `Equals(object)`, declares `==` or `!=`, or implements `IEquatable<T>` without overriding `GetHashCode` is reported on its name, and so is the `GetHashCode` override of a type that defines no equality at all. A base class (other than `object` and `ValueType`) that already overrides the missing method covers the derived type. Report only, there is no code fix. Not reported: records, abstract and static classes, interfaces, types that override both, and `Equals` overloads that are not the `object` override.

NLR0034 has no option and a suggestion severity. A struct with a non-`readonly` instance field or a property with a `set` accessor is reported on its name, with the first offending member in the message. Report only, there is no code fix. Not reported: `readonly struct`, `record struct`, static and `const` members, and `init` properties; `ref struct` and interop structs are reported like any other.

NLR0035 has no option and a warning severity. A call to a mutating method (`Add`, `Insert`, `Remove`, `RemoveAt`, `RemoveAll`, `Clear`, `Sort`, `Reverse`, `Push`, `Pop`, `Enqueue`, `Dequeue` and relatives) on the local, parameter or field that a `foreach` enumerates is reported on the call, for `List`, `LinkedList`, `Queue`, `Stack`, sorted collections, `Collection`, `Dictionary` and `HashSet` (and types derived from them, `IList`, `ICollection`, `IDictionary`, `ISet`); on `Dictionary` and `HashSet` only additions are reported. Report only, there is no code fix. Not reported: iterating a copy (`list.ToList()`), another collection, indexer assignments, `for` loops, concurrent and immutable collections, calls inside a lambda or local function, and a call directly followed by `break` (of that `foreach`), `return`, `throw` or `goto`.

NLR0036 has no option and a warning severity. A `new Random()` or target-typed `new()` without arguments inside the body or condition of a `for`, `foreach`, `while` or `do` loop is reported, since instances created in quick succession can start from the same clock seed. Report only, there is no code fix. Not reported: an explicit seed, an instance created outside the loop, in the collection of a `foreach` or the initializer of a `for`, inside a lambda or local function, `Random.Shared` and `RandomNumberGenerator`.

NLR0037 has no option and a warning severity. A `catch` clause without `when` filter whose block is only `throw;` is reported on the `catch` keyword. The fix removes the clause; when no catch clause is left and there is no `finally`, the `try` is replaced by its block. Not reported: a clause that a later clause of the same `try` relies on (the same type or a base type, or a general `catch`), a `when` filter, any other statement, `throw ex;` (NLR0023), an empty body (NLR0028).

The rules then show up wherever Roslyn analyzers are loaded:

| Host | Diagnostics | Code fixes |
|---|---|---|
| Visual Studio 2022 and later | yes | yes |
| VS Code (C# extension / C# Dev Kit) | yes | yes |
| JetBrains Rider | yes | yes |
| Editors using the Roslyn language server or OmniSharp (Neovim, Helix, Zed, Emacs…) | yes | yes, as LSP code actions |
| `dotnet build` / CI | yes | — |

The package, `Neolinter` on nuget.org, is published by the release workflow (see [Releasing](docs/RELEASING.md)).
Until the first `v*` tag exists, build it with `dotnet pack` (see [CONTRIBUTING](docs/CONTRIBUTING.md)) and use a
local package source. Pre-releases are versioned like `0.1.0-preview.1`: pick one explicitly.

### Settings screen

Each IDE gets a native settings screen that edits the same `.editorconfig` keys. Available today: VS Code
(`src/ide/vscode`, command **Neolinter: Open Settings**, with a "Solution analysis" tab that runs the command
line, see below), and Visual Studio 2022 17.14 / 2026 (`src/ide/visualstudio`, **Extensions > Neolinter Settings**,
"Solution analysis" tab disabled for now). Planned: Rider, then Zed.

The VS Code extension (`neolinter.neolinter` on the Marketplace) is published by the same release workflow, and every
release also attaches both `.vsix` files to its
[GitHub release](https://github.com/neolinter/neolinter/releases) (VS Code: `code --install-extension <file>`;
Visual Studio: double-click the file). The Visual Studio extension is also published on the Visual Studio Marketplace by the release workflow, from the first release that includes that step.

### Command line

```bash
dotnet tool install --global Neolinter.Cli
neolinter analyze dead-code MySolution.slnx --output dead-code.sarif
neolinter analyze duplicates MySolution.slnx
```

The path is optional. Without it the CLI uses the single `.sln`/`.slnx` of the current directory; when there is none,
it looks in the subfolders (3 levels deep at most, skipping `node_modules`, `bin`, `obj` and hidden folders) and uses
the only one found. With several, it lists them and asks for one to be passed. In VS Code the setting
`neolinter.solution.path` passes it for you.

Exit codes: `0` nothing to report, `1` findings reported, `2` error. Restore the solution first (`dotnet restore`;
`duplicates` only reads source files, `dead-code` needs the framework references).

`duplicates` reports sequences of at least `duplicates.minStatements` statements (default 8) and
`duplicates.minTokens` tokens (default 50) that appear more than once in the solution, in one file or across
projects, whatever the identifier names and literal values. One result per group; the other copies are its
related locations. Both keys, `excludePaths` and `excludeGenerated` live in the `duplicates` section of
`neolinter.json` (see [Architecture](docs/architecture.md)).

Options of every analysis:

| Option | Effect |
|---|---|
| `--output <file>` | Write the report to a file instead of the standard output. |
| `--format sarif\|text` | `sarif` (default) or one line per finding: `path(line,column): level RULEID: message`. |
| `--fail-on note\|warning\|error` | Exit code `1` only for findings at or above this level (default `warning`). Lower findings are still reported. |
| `--baseline <file.sarif>` | Skip findings already present in a previous SARIF log (matched by `partialFingerprints`), in the report and in the exit code. |

Before `--fail-on` existed, any finding gave exit code `1`; the default `warning` keeps that behaviour for every
current rule, which all report at `warning`.

### Continuous integration (GitHub code scanning)

```yaml
- run: dotnet restore
- run: dotnet tool install --global Neolinter.Cli
- run: neolinter analyze dead-code MySolution.slnx --output dead-code.sarif
  continue-on-error: true   # exit code 1 = findings, still upload them
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: dead-code.sarif
    category: neolinter-dead-code
```

To fail a pull request only on new findings, keep the SARIF of the main branch (artifact or cache) and pass it with
`--baseline main.sarif`.

## Rules

| Id | Title | Kind | Default |
|---|---|---|---|
| [NLR0001](rules/NLR0001.yaml) | Invert 'if' to reduce nesting | real-time, code fix | suggestion |
| [NLR0004](rules/NLR0004.yaml) | Use null-coalescing assignment | real-time, code fix | suggestion |
| [NLR0005](rules/NLR0005.yaml) | Simplify comparison with a boolean literal | real-time, code fix | suggestion |
| [NLR0006](rules/NLR0006.yaml) | Keep LINQ operators inside the Entity Framework Core query | real-time | warning |
| [NLR0007](rules/NLR0007.yaml) | Avoid nested conditional expressions | real-time, code fix | suggestion |
| [NLR0008](rules/NLR0008.yaml) | Merge nested 'if' statements | real-time, code fix | suggestion |
| [NLR0009](rules/NLR0009.yaml) | Remove 'else' after a branch that exits | real-time, code fix | suggestion |
| [NLR0010](rules/NLR0010.yaml) | Use the condition instead of 'true' and 'false' | real-time, code fix | suggestion |
| [NLR0011](rules/NLR0011.yaml) | Use 'Any()' instead of comparing 'Count()' with zero | real-time, code fix | suggestion |
| [NLR0012](rules/NLR0012.yaml) | Fold 'Where' into the following LINQ operator | real-time, code fix | suggestion |
| [NLR0013](rules/NLR0013.yaml) | Use 'is null' instead of comparing with 'null' | real-time, code fix | suggestion |
| [NLR0014](rules/NLR0014.yaml) | Use 'string.IsNullOrEmpty' | real-time, code fix | suggestion |
| [NLR0015](rules/NLR0015.yaml) | Use 'TryGetValue' instead of 'ContainsKey' and the indexer | real-time, code fix | suggestion |
| [NLR0016](rules/NLR0016.yaml) | Use a type pattern instead of a cast | real-time, code fix | suggestion |
| [NLR0017](rules/NLR0017.yaml) | Move the 'continue' guard into 'Where' | real-time, code fix | suggestion |
| [NLR0018](rules/NLR0018.yaml) | Move the 'break' guard into 'TakeWhile' | real-time, code fix | suggestion |
| [NLR0019](rules/NLR0019.yaml) | Use a switch expression | real-time, code fix | suggestion |
| [NLR0020](rules/NLR0020.yaml) | Use a conditional expression | real-time, code fix | suggestion |
| [NLR0021](rules/NLR0021.yaml) | Use string interpolation | real-time, code fix | suggestion |
| [NLR0022](rules/NLR0022.yaml) | Use string interpolation instead of string.Format | real-time, code fix | suggestion |
| [NLR0023](rules/NLR0023.yaml) | Rethrow with throw; instead of throw ex; | real-time, code fix | warning |
| [NLR0024](rules/NLR0024.yaml) | Avoid async void outside event handlers | real-time | warning |
| [NLR0025](rules/NLR0025.yaml) | Do not block on a task inside an async method | real-time, code fix | warning |
| [NLR0026](rules/NLR0026.yaml) | Use await Task.Delay instead of Thread.Sleep in async code | real-time, code fix | warning |
| [NLR0027](rules/NLR0027.yaml) | Async method without await | real-time, code fix | warning |
| [NLR0028](rules/NLR0028.yaml) | Empty catch block | real-time | warning |
| [NLR0029](rules/NLR0029.yaml) | Self-assignment | real-time, code fix | warning |
| [NLR0030](rules/NLR0030.yaml) | Ignored return value | real-time, code fix | warning |
| [NLR0031](rules/NLR0031.yaml) | Lock on a publicly visible object | real-time | warning |
| [NLR0032](rules/NLR0032.yaml) | Multiple enumeration of an IEnumerable | real-time, code fix | warning |
| [NLR0033](rules/NLR0033.yaml) | Equals without GetHashCode | real-time | warning |
| [NLR0034](rules/NLR0034.yaml) | Mutable struct | real-time | suggestion |
| [NLR0035](rules/NLR0035.yaml) | Collection modified during foreach | real-time | warning |
| [NLR0036](rules/NLR0036.yaml) | new Random() created in a loop | real-time | warning |
| [NLR0037](rules/NLR0037.yaml) | Useless catch that only rethrows | real-time | warning |
| [NLC0002](rules/NLC0002.yaml) | Unused member | command line | warning |
| [NLC0003](rules/NLC0003.yaml) | Duplicated code | command line | warning |

## Documentation

- [Roadmap](docs/ROADMAP.md): work packages and rules for picking them up
- [Releasing](docs/RELEASING.md): tags, versions, secrets, unlisting a bad release
- [Architecture](docs/architecture.md), including the `neolinter.json` format
- [Contributing](docs/CONTRIBUTING.md)
- [Adding a rule](docs/ADDING_A_RULE.md)
- [Clean-room policy](docs/CLEAN_ROOM.md)

## License

[MIT](LICENSE). See also [NOTICE](NOTICE).
