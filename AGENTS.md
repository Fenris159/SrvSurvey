## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `Fenris159/SrvSurvey`. See `docs/agents/issue-tracker.md`.

### Triage labels

Triage uses the five default canonical labels. See `docs/agents/triage-labels.md`.

### Domain docs

Domain documentation uses the single-context layout. See `docs/agents/domain.md`.

## C# quality checks

`SonarAnalyzer.CSharp` is applied to every C# project from `Directory.Build.props`,
and analyzer warnings fail the build. CSharpier is the repository formatter.
After changing C# or desktop UI markup, run:

```console
pwsh ./tools/Test-ChangedCodeQuality.ps1
```

That script now mirrors the CI pre-build gates: CSharpier formatting, Avalonia
localization catalog freshness (`tools/Generate-AvaloniaLocalization.ps1 -Verify`),
and the `.editorconfig` / SonarCloud-profile checks scoped to changed C# lines.
Adding or renaming Desktop source files can change localization `FirstSource`
metadata even when visible strings are unchanged; regenerate with
`pwsh ./tools/Generate-AvaloniaLocalization.ps1` before committing.

Do not suppress or fix unrelated analyzer findings unless the user asks; report
them separately.

Tests that assert `IProgress<T>` callbacks must use a synchronous test recorder.
Do not use `System.Progress<T>` for those assertions because it schedules
callbacks asynchronously and can race differently on Windows and Linux.
