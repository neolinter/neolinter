# Releasing

A release is a Git tag `vX.Y.Z` or `vX.Y.Z-<label>.<N>` (for example `v0.1.0-preview.1`). Pushing it runs
`.github/workflows/release.yml`, which builds, tests and packs everything, then publishes:

| Artifact | Published to | Version |
|---|---|---|
| `Neolinter` (analyzers) | nuget.org | the tag, `0.1.0-preview.1` |
| `Neolinter.Cli` (.NET tool) | nuget.org | the tag |
| VS Code extension `.vsix` | VS Code Marketplace (publisher `neolinter`), pre-release channel for previews | see below |
| Visual Studio extension `.vsix` | Visual Studio Marketplace (trusted publishing) and the GitHub release | see below |

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

1. **GitHub environment**: Settings > Environments > New environment `release` (optionally add required reviewers: the
   `publish` job then waits for your approval before anything is pushed). The nuget.org policy below matches this name.
2. **nuget.org trusted publishing policy** (no API key, no NuGet secret): sign in to nuget.org as a member of the
   `neolinter` organization, open your username menu > **Trusted Publishing** > add a policy:
   - **Policy owner**: the `neolinter` organization (the policy then covers the packages that organization owns; you must
     stay an active member, otherwise the policy goes inactive);
   - **Repository owner**: `neolinter`, **Repository**: `neolinter`;
   - **Workflow file**: `release.yml` (file name only, no path);
   - **Environment**: `release`;
   - **Scope**: allow publishing new packages and new versions, package glob `Neolinter*`.

   The workflow calls `NuGet/login` (pinned to the commit of v1.2.0), which swaps the job's GitHub OIDC token for a
   one-hour API key (`id-token: write` is set on the `publish` job only). Its `user:` must be the nuget.org username of the
   person who **created the policy**, not the organization that owns it (the first release attempt failed with a 401 on
   that point). It is read from the repository secret `NUGET_USER` (Settings > Secrets and variables > Actions > New repository secret): add it with that username; update it if another maintainer recreates the policy.
   If the policy shows "temporarily active for 7 days" (private repositories), the first successful publish within 7
   days makes it permanent; it can be restarted.
   **Bootstrap, not verified**: the nuget.org documentation does not say whether a policy can be created before the
   package id `Neolinter` exists. The policy scope offers "new packages", so it should work. If nuget.org refuses, push the
   first release by hand once: temporary API key (<https://www.nuget.org/account/apikeys>, owner `neolinter`, glob
   `Neolinter*`, short expiry), `dotnet nuget push` of the two `.nupkg` of the dry run, delete the key, then use the
   workflow from the next version on.
3. **ID prefix reservation** (optional, can wait for the first release): it protects `Neolinter.*` ids from look-alikes. The
   documented way is an e-mail to account@nuget.org with the nuget.org display name of the owner (`neolinter`) and the
   prefix `Neolinter.` (<https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation>, a page last updated in 2022;
   there is no web form). Whether an organization can own it and whether packages must exist first is not stated there:
   ask in the same e-mail, and push the first release before if they require it.
4. **VS Code Marketplace** (trusted publishing, no token): sign in to <https://marketplace.visualstudio.com/manage> with
   the Microsoft account that will own it and create the publisher with the id `neolinter` (it must equal `publisher` in
   `package.json`). Open the publisher > **Trusted Publishing** > **Create**: a policy name (free), repository owner
   `neolinter`, repository `neolinter`, workflow `release.yml`, environment `release`. The workflow runs
   `vsce publish --oidc`; the Marketplace credential lasts 15 minutes. If the policy is created while the repository is
   private, use it within 48 hours: the first successful publish activates it. No `VSCE_PAT` secret exists anymore (Azure
   DevOps retires global personal access tokens on 2026-12-01). Not verified: the exact form of the policy and whether it
   can be created before the first publication of the extension.
5. **Protect tags** (Settings > Rules > Rulesets > New tag ruleset): target tag pattern `v*`, restrict creation to
   maintainers, block deletion and updates. A pushed `v*` tag publishes, so only trusted people may create one.

## Dry run

Actions > Release > Run workflow, leave **dry-run** checked, pick a branch and a version (default `0.1.0-preview.1`).
It builds, tests and packs the NuGet packages, the VS Code extension and the Visual Studio extension, prints their
contents in the log and uploads them as workflow artifacts; the `publish` job is skipped: no secret is read and the OIDC exchange is not exercised (only a real tag proves it). A run
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
`neolinter` publisher, the trusted publishing policy (and whether it can precede the first push) and the `Neolinter.` prefix reservation.

## Visual Studio Marketplace

The `publish-visualstudio` job (Windows) runs after `publish` with `VsixPublisher.exe publish ... -oidc` and
`src/ide/visualstudio/publishManifest.json` (category, internal name `Neolinter`, publisher `neolinter`, overview
`overview.md`). A failure there leaves the NuGet packages, the VS Code extension and the GitHub release published: fix the
cause and re-run only that job.

Maintainer setup: the publisher `neolinter` is the same one as for VS Code
(<https://marketplace.visualstudio.com/manage/publishers>). Check that its Trusted Publishing policy (repository
`neolinter/neolinter`, workflow `release.yml`, environment `release`) also allows the Visual Studio Marketplace; if
it is a separate list, add the same policy there.

**Known problem (2026-11-01)**: the first run (`v0.1.0-preview.3`) failed in `VsixPublisher.exe` 18.11 with
`VsixPub0047 ... No api-version was supplied for the "POST" request`: the Marketplace refuses the tool's OIDC token
exchange (the same exchange works for `vsce publish --oidc`). It is a mismatch between the tool and the service, not a
workflow setting, and the runner already has the newest tool. The step is therefore `continue-on-error`: the run stays green,
and the job shows a warning. **Publish by hand until Microsoft fixes the tool**: download `Neolinter.VisualStudio.vsix`
from the GitHub release, go to <https://marketplace.visualstudio.com/vs> > Publish extensions > publisher `neolinter` >
New extension > Visual Studio, upload the `.vsix`, paste `src/ide/visualstudio/overview.md` as the overview, pick the
category and tags, Save & Upload, then Make Public. An alternative is `-personalAccessToken` in the workflow (a PAT is
subject to the retirement of global Azure DevOps tokens on 2026-12-01).

Not verified: the install on Professional and Enterprise (the manifest lists the `Microsoft.VisualStudio.Community`
target, which Visual Studio is expected to apply to every edition; check on one). The window writing to a real solution's `.editorconfig` is also still unverified (WP-08 in `docs/ROADMAP.md`).

**Temporary test job**: `republish-visualstudio` (remove it afterwards). Run Actions > Release > Run workflow with
`visualstudio-release-tag` set to an existing release (for example `v0.1.0-preview.3`) and `dry-run` unchecked is not
needed: when that input is filled, every other job is skipped and only this one runs, on `windows-2022` (Visual Studio 2022
17.14 and its older `VsixPublisher.exe`). It prints the tool version and its options, fails if `-oidc` is missing, then
publishes the `.vsix` of that release **for real** to the Visual Studio Marketplace.
