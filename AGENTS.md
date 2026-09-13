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
After changing C# code, run `dotnet csharpier check .` and the appropriate
Release build. Do not suppress or fix unrelated analyzer findings unless the
user asks; report them separately.
