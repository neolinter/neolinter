# Releasing

A release is a Git tag `vX.Y.Z` or `vX.Y.Z-<label>.<N>` (for example `v0.1.0-preview.1`). Pushing it runs
`.github/workflows/release.yml`, which builds, tests and packs everything, then publishes:

| Artifact | Published to | Version |
|---|---|---|
| `Neolinter` (analyzers) | nuget.org | the tag, `0.1.0-preview.1` |
| `Neolinter.Cli` (.NET tool) | nuget.org | the tag |
| VS Code extension `.vsix` | VS Code Marketplace (publisher `neolinter`), pre-release channel for previews | see below |
| Visual Studio extension `.vsix` | GitHub release only (not the Visual Studio Marketplace) | see below |

The GitHub release carries the two NuGet packages and both `.vsix` files and is marked pre-release for previews.
Nothing in the repository holds a version to bump: `Directory.Build.props` keeps `0.1.0` only as the local
development default, and the workflow overrides it with `-p:Version=`; the VS Code `package.json` version is likewise
a development value, replaced at package time.

## Versions

NuGet takes the tag as is (SemVer, pre-release suffixes included). The VS Code Marketplace and the Visual Studio
manifest only take numeric `major.minor.patch`, so for a pre-release `X.Y.Z-label.N` the extensions get
`X.Y.(Z*1000+N)`:

| Tag | NuGet | Extensions |
|---|---|---|
| `v0.1.0` | `0.1.0` | `0.1.0` (stable) |
| `v0.1.0-preview.1` | `0.1.0-preview.1` | `0.1.1` (VS Code `--pre-release`) |
| `v0.1.0-preview.2` | `0.1.0-preview.2` | `0.1.2` |
| `v0.2.0-rc.1` | `0.2.0-rc.1` | `0.2.1` |

Consequences: the pre-release label must end with `.N` (`-preview.1`, not `-beta`), and N is at most 999. Previews of
`X.Y.Z` sort above the stable `X.Y.Z` in the extensions, so **after previews of `0.1.0` the first stable tag must
raise the minor or major (`v0.2.0` or `v1.0.0`)**. Marketplace versions cannot be reused either. The conversion lives
in `.github/scripts/release-version.sh`.

## One-time setup (maintainer)

1. **nuget.org**: the `neolinter` organization exists. Create an API key
   (<https://www.nuget.org/account/apikeys>), owner `neolinter`, scope "Push new packages and package versions",
   glob pattern `Neolinter*`, an expiry you will remember. Add it as the repository secret `NUGET_API_KEY`
   (Settings > Secrets and variables > Actions). Trusted publishing (OIDC, no stored key) exists on nuget.org but was not
   checked against an organization-owned, not yet existing package ID, so the key is used.
2. **ID prefix reservation**: request the `Neolinter.` prefix at <https://www.nuget.org/account/requestidprefix>
   (owner `neolinter`). It protects `Neolinter.*` ids from look-alikes. `Neolinter` itself must be pushed once by the
   key's owner to exist; the first release does that.
3. **VS Code Marketplace**: sign in to <https://marketplace.visualstudio.com/manage> with the Microsoft account that will
   own it, create the publisher with the id `neolinter` (it must equal `publisher` in `package.json`). Then create a
   Personal Access Token at <https://dev.azure.com> (User settings > Personal access tokens): organization "All
   accessible organizations", scope **Marketplace > Manage**. Add it as the repository secret `VSCE_PAT`. The PAT expires
   (at most one year): calendar the renewal.
4. **Protect tags** (Settings > Rules > Rulesets > New tag ruleset): target tag pattern `v*`, restrict creation to
   maintainers, block deletion and updates. A pushed `v*` tag publishes, so only trusted people may create one.
5. Optional: create a GitHub environment `release` with required reviewers and use it in the `publish` job for a manual
   approval before anything is pushed (not enabled by default).

## Dry run

Actions > Release > Run workflow, leave **dry-run** checked, pick a branch and a version (default `0.1.0-preview.1`).
It builds, tests and packs the NuGet packages, the VS Code extension and the Visual Studio extension, prints their
contents in the log and uploads them as workflow artifacts; the `publish` job is skipped and no secret is read. A run
with `dry-run` unchecked on a branch fails on purpose: a real release needs a tag.

## Cut a release

1. `main` is green and `docs/ROADMAP.md`/README are up to date.
2. From an up-to-date `main`:

   ```bash
   git tag -s v0.1.0-preview.1 -m "v0.1.0-preview.1"
   git push origin v0.1.0-preview.1
   ```

3. Watch the Release workflow. `publish` runs only if the commit is reachable from `main` and all build jobs pass. It pushes
   to nuget.org (`--skip-duplicate`), publishes the VS Code extension, then creates the GitHub release.
4. If a step after the NuGet push fails, fix the cause and re-run the failed job: already pushed packages are skipped; a
   Marketplace version that was already published makes `vsce` fail, in which case publish the remaining step by hand or cut
   the next version. A NuGet version can never be reused, so do not delete and re-push a tag after a successful push.
5. New packages appear on nuget.org after validation (a few minutes; the first `Neolinter.` id may wait for the reservation).

## Bad release

- **NuGet**: versions cannot be deleted. On the package page, Manage package > Listing: unlist the version (still
  restorable by exact version, hidden from search). Publish a fixed version above it. For a vulnerability, also deprecate
  the version (Manage package > Deprecation) and open a security advisory.
- **VS Code Marketplace**: Manage the extension at <https://marketplace.visualstudio.com/manage>, **Unpublish** the version
  (or `npx vsce unpublish neolinter.neolinter` removes the whole extension: avoid), then publish a higher version.
- **GitHub release**: edit it to mark it pre-release or delete it. Do not move or delete the tag.

## Not verified here

The workflow could not be run on GitHub from the session that wrote it. Verified locally: the version script, build, tests,
`dotnet pack` with the version override (package contents, metadata, icon, installing the tool from the local nupkg),
`vsce package` with the converted version and `--pre-release`, and the Visual Studio `.vsix` build with the converted version
on Windows. Not verified: the Actions run itself (dry run to do first), the nuget.org and Marketplace pushes, the
`neolinter` publisher and API key scopes, and the `Neolinter.` prefix reservation.

## Visual Studio extension on the Visual Studio Marketplace (not done)

The `.vsix` is attached to the GitHub release only. Publishing it on the Visual Studio Marketplace still needs: a Marketplace
publisher (a separate account from the VS Code one, at the same Manage page) with a PAT or the publish page's manual upload;
an overview (`README.md`/`publish-manifest.json` with categories, `repo` and `identity.internalName`); the generated manifest
lists only the `Microsoft.VisualStudio.Community` install target, so check that Professional and Enterprise can install it
(add the targets if not); the extension's unverified items listed in `docs/ROADMAP.md` (WP-08); and a decision on
Windows-only publishing from CI (`VsixPublisher.exe` is Windows only).
