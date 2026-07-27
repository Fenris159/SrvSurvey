# Install SrvSurvey on Windows

These instructions apply to the portable Avalonia package named
`SrvSurvey-Avalonia-<version>-win-x64.zip`. It is self-contained and does not
use `setup.exe` or require a separate .NET installation.

## Download the package

You can download the Windows ZIP directly from a tagged GitHub release. To
build the latest branch yourself:

1. Open the repository's
   [Build Windows and Linux packages workflow](https://github.com/Fenris159/SrvSurvey/actions/workflows/manual-release-packages.yml).
2. Select **Run workflow**, optionally enter a three- or four-part version, and
   start the run.
3. Open the completed run and download the `SrvSurvey-package-win-x64`
   artifact.
4. Extract the downloaded Actions artifact. It contains the actual
   `SrvSurvey-Avalonia-<version>-win-x64.zip` package and its SPDX bill of
   materials.

GitHub Actions artifacts expire after the retention period shown on the run.

## Extract and run it

The ZIP is a portable application, so its extracted directory is its
**container folder**. In this guide, that means the ordinary Windows folder
that contains `SrvSurvey.Desktop.exe`, its DLLs, and its runtime files; it does
not mean Docker or Windows Sandbox.

1. Create a dedicated folder, such as
   `C:\Users\<you>\AppData\Local\Programs\SrvSurvey-Avalonia\<version>`.
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
