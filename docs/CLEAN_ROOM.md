# Clean-room policy

Neolinter offers features that commercial and open-source analyzers also offer. To stay clear of their
copyrights and trademarks, every contribution follows these rules.

## Never

- Decompile, disassemble or otherwise reverse-engineer another analysis product, its plugins or its rule packs.
- Copy or paraphrase code, rule texts, messages, descriptions, documentation or examples from another analysis
  product, whatever its license, including open-source ones.
- Reuse another product's rule identifiers or name a rule after them (for example "same as XYZ1234").
- Use a third-party product or brand name in rule names, ids, messages, settings or package names.

## Allowed

- Describing a general coding practice in your own words. Ideas such as "leave early to reduce nesting" are not
  owned by anyone; a particular wording of them is.
- Reading the public documentation of the platforms we plug into (Roslyn, .NET, VS Code, LSP, SARIF, EditorConfig).
- Using a product as an ordinary user, without inspecting its internals, as long as nothing above is copied.

## In practice

- Write the rule description and messages before looking at how others phrase the same idea, and do not look
  afterwards to "align" them.
- Design detection and fixes from the language specification and your own test cases.
- If you have worked on a competing analyzer's internals, say so in your pull request so maintainers can decide
  whether you should work on that particular rule.

Pull requests that do not follow this policy are closed, and the related code is removed.
