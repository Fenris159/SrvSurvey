# SrvSurvey-XP 2.1.3.0-rc.77.19

RC77.19 updates Raven Colonial connections for its server migration and refreshes
an application dependency.

## What's changed

- **Raven Colonial server migration:** SrvSurvey now uses
  `https://ravencolonial.net` for construction projects, system sites and body
  catalogs, Fleet Carrier and cargo updates, quests, and Green Gas Giant
  submissions. Commander nickname downloads use the new host too. This replaces
  the server-specific Azure address that Raven Colonial plans to retire.
- **Dependency maintenance:** Updated the Windows drawing compatibility package
  to .NET 10.0.15. The release's direct and transitive NuGet dependencies pass
  the vulnerability audit.
