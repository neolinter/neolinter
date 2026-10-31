## Summary

<!-- What changes and why. One topic per pull request. -->

## Checklist

- [ ] Clean room: nothing here is copied or paraphrased from another analyzer, and no third-party rule id or
      brand is used ([docs/CLEAN_ROOM.md](../docs/CLEAN_ROOM.md)). If I have worked on a competing analyzer's
      internals, I say so below.
- [ ] `dotnet test Neolinter.slnx` and `npm test` (in `src/ide/vscode`) pass; warnings are errors.
- [ ] Tests added or updated; playground examples (`Reported*` / `NotReported*`) for rule changes.
- [ ] Docs updated; no change to frozen contracts (rule ids, `neolinter.json`, exit codes, `.editorconfig` keys)
      without prior discussion.
