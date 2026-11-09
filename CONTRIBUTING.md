# Contributing

This repository holds the package (`com.xpturn.feedback`), its development projects (`tests/`) and the tools that
check it (`tools/`).

## Development projects

`tests/FeedbackTests.6000.0`, `tests/FeedbackTests.6000.3` and `tests/FeedbackTests.6000.6` each open in the Unity
version their `ProjectSettings/ProjectVersion.txt` names. Each reads the package from this repository (`file:` in
`Packages/manifest.json`) and lists it in `testables`, so the package's EditMode and PlayMode tests show in the Test
Runner. A change to their assets, settings or manifest goes into all three.

In each project, `Packages/com.xpturn.feedback.samples-dev` is a symbolic link to the package's `Samples~` folder: the
samples open in the project and are edited in place.

### Windows

Git checks a symbolic link out as a small text file unless symbolic links are turned on, and the development projects
then open without the samples. Turn on Developer Mode and clone with symbolic links:

```
git clone -c core.symlinks=true https://github.com/xpTURN/Feedback.git
```

Installing the package from its git URL does not need this: the link is outside the package's folder.

## Tests without Unity

`com.xpturn.feedback/Tests~` builds the package's engine-free math (`Runtime/Math`) with the .NET SDK and runs its NUnit
tests:

```
dotnet test com.xpturn.feedback/Tests~/Math.Tests -c Debug
dotnet test com.xpturn.feedback/Tests~/Math.Tests -c Release
```

## Tools

The scripts in `tools/` are written for macOS and Unity Hub's editors (`/Applications/Unity/Hub/Editor`, or the folder
in `UNITY_EDITORS`). The checks take Unity versions as arguments (each version's development project must be on that
version) and run in throwaway projects, so the development projects stay as they are.

| Script | What it checks or writes |
|---|---|
| `check-minimal-manifest.sh` | The package compiles with every optional dependency removed. |
| `check-package-roundtrip.sh` | An optional package taken out of a project and put back keeps a player's data. |
| `check-player-gates.sh` | The PlayMode tests in IL2CPP macOS arm64 players, and the build step that keeps UnityEvent targets. |
| `check-recompile-during-play.sh` | A script recompile during Play mode (the editor's window shows for a while). |
| `check-samples.sh` | The samples as a user imports them. |
| `check-skill.sh` | The Claude Code skill and the marketplace pass Claude Code's validation. |
| `write-docs.sh` | Rewrites the documents' generated regions from their sources and runs the documents' tests. |
| `doc-images.sh` | Takes the documents' screenshots again from the editor. |

## Documents

The package's `README.md` is the source of the repository's `README.md`: `tools/root-readme.py` writes the copy, and
`tools/write-docs.sh` runs it. The translations (`README.<lang>.md`) are written by hand;
`tools/root-readme.py --translations` tells when one is behind the English text.
