# Install SrvSurvey on Windows

Current release candidate version: **SrvSurvey-XP 2.1.3.0-rc.2**.

These instructions apply to the portable cross-platform package named
`SrvSurvey-XP-<version>-win-x64.zip`. It is self-contained and does not
use `setup.exe` or require a separate .NET installation.

## Download the package

Download `SrvSurvey-XP-<version>-win-x64.zip` from the relevant
[SrvSurvey-XP release](https://github.com/Fenris159/SrvSurvey/releases).

Repository maintainers can build and publish a new release as follows:

1. Open the repository's
   [Build and publish SrvSurvey-XP release workflow](https://github.com/Fenris159/SrvSurvey/actions/workflows/build-srvsurvey-xp.yml).
2. Select **Run workflow**, choose the source branch/tag/commit and release
   channel, then enter a three- or four-part base version. Development builds
   also require an RC number.
3. After all builds and tests pass, the workflow creates an `xp-v<version>`
   release. Development builds append `-rc.<number>` and are GitHub
   pre-releases; stable builds use the base version and are explicitly not
   assigned GitHub's **Latest** badge.

## Extract and run it

The ZIP is a portable application, so its extracted directory is its
**container folder**. In this guide, that means the ordinary Windows folder
that contains `SrvSurvey.Desktop.exe`, its DLLs, and its runtime files; it does
not mean Docker or Windows Sandbox.

1. Create a dedicated folder, such as
   `C:\Users\<you>\AppData\Local\Programs\SrvSurvey-XP\<version>`.
2. Extract the complete package ZIP into that folder. Do not run the executable
   from inside the ZIP preview.
3. Open that folder and run `SrvSurvey.Desktop.exe` from there.

Keep every extracted file together. Do not move `SrvSurvey.Desktop.exe` by
itself to the Desktop or another directory: the executable requires the DLLs
and self-contained runtime beside it. If you want desktop access, create a
shortcut to the executable and leave the executable in its container folder.
Set the shortcut's **Start in** field to that same folder.

The review build is unsigned. If Windows marks the download as coming from the
Internet, right-click the package ZIP, select **Properties**, and use
**Unblock** before extracting when that option is present. Windows SmartScreen
may still show a warning; only choose **More info** and **Run anyway** when the
file came from this repository and you trust the build.

## Update or remove it

Extract a newer version into a new container folder and start it there. Keep
the previous folder until the new version starts successfully, then delete the
old application folder if you no longer need it. Do not overwrite a populated
folder because files removed between versions could otherwise remain behind.

Removing the portable application folder does not intentionally remove the
SrvSurvey profile stored in your user-data location. Use SrvSurvey's backup and
import tools before making broader profile changes.

## Troubleshooting

- A missing DLL or runtime error usually means the package was only partially
  extracted or the executable was moved. Extract the full ZIP again and run
  `SrvSurvey.Desktop.exe` from its container folder.
- If antivirus software quarantines a package file, verify that the ZIP came
  from this repository before restoring it. Do not download replacement DLLs
  from third-party sites.
- Start Elite Dangerous and SrvSurvey as the same Windows user. Running one of
  them elevated and the other normally can interfere with window tracking and
  global input.
