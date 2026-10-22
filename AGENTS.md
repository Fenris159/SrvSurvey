## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `Fenris159/SrvSurvey`. See `docs/agents/issue-tracker.md`.

### Triage labels

Triage uses the five default canonical labels. See `docs/agents/triage-labels.md`.

### Domain docs

Domain documentation uses the single-context layout. See `docs/agents/domain.md`.

## Pull request continuity

Keep ongoing work on the current PR and its branch until that PR is merged,
including new features, fixes, review follow-ups, and RC/release-note updates.
Create a new PR only when the user explicitly asks. A topic or scope change does
not authorize splitting work into another PR; merging the current PR does not
by itself authorize creating the next one.

## Overlay settings design

When adding or changing overlay controls or shortcut assignment, follow
`docs/agents/overlay-settings.md`.

## C# quality checks

`SonarAnalyzer.CSharp` is applied to every C# project from `Directory.Build.props`,
and analyzer warnings fail the build. CSharpier is the repository formatter.
After changing C# or desktop UI markup, run:

```console
pwsh ./tools/Test-ChangedCodeQuality.ps1
```

That script now mirrors the CI pre-build gates: CSharpier formatting, Avalonia
localization catalog freshness (`tools/Generate-AvaloniaLocalization.ps1 -Verify`),
the `.editorconfig` style checks scoped to changed C# lines, a full
SonarCloud-profile build with **all warnings treated as errors**, and SonarCloud's
**80% new-code coverage** gate on changed production C# (line and branch points
from Coverlet OpenCover). Pre-existing Sonar warnings also block validation.
Analyzer-only Sonar builds do not
measure coverage; do not treat a green style check as a coverage pass.
Adding or renaming Desktop source files can change localization `FirstSource`
metadata even when visible strings are unchanged; regenerate with
`pwsh ./tools/Generate-AvaloniaLocalization.ps1` before committing.

Install the local pre-commit hook once per clone so those gates also block
`git commit`:

```console
pwsh ./tools/Install-LocalGitHooks.ps1
```

Do not suppress or fix unrelated analyzer findings unless the user asks; report
them separately.

Tests that assert `IProgress<T>` callbacks must use a synchronous test recorder.
Do not use `System.Progress<T>` for those assertions because it schedules
callbacks asynchronously and can race differently on Windows and Linux.
