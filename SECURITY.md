# Security policy

## Supported versions

Neolinter is pre-1.0: only the latest release receives security fixes.

## Reporting a vulnerability

Please do not open a public issue. Use GitHub's private reporting:
[Report a vulnerability](https://github.com/neolinter/neolinter/security/advisories/new).

Include the affected component (analyzers, CLI, VS Code extension), the version and a way to reproduce. We aim to
acknowledge a report within a week and to agree on a disclosure date with you once a fix is available.

## Known advisories in development dependencies

`npm audit` in `src/ide/vscode` reports two moderate findings (`minimatch` denial of service through
`@vscode/vsce`). Reviewed on 2026-10-31 and accepted: `@vscode/vsce` is a dev dependency used only to build the
`.vsix`; neither it nor `minimatch` is bundled into the extension, and it only processes our own file patterns
on a developer or CI machine. No fixed version exists upstream; Dependabot will propose one when it does.
