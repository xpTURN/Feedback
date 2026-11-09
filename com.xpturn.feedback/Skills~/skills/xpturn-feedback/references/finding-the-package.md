# Finding the package

The package's files — its documentation, its presets — sit in a different folder for each way it was installed. Read
`Packages/packages-lock.json`: its `com.xpturn.feedback` entry has a `source` and a `version`.

| `source` | The package's folder |
|---|---|
| `git` | `Library/PackageCache/com.xpturn.feedback@<hash>/` — one folder whose name ends in a hash |
| `registry` | `Library/PackageCache/com.xpturn.feedback@<hash>/`, as for git |
| `local-tarball` | `Library/PackageCache/com.xpturn.feedback@<hash>/`, as for git |
| `local` | The path after `file:` in `version` — relative to the `Packages` folder unless it is absolute |
| `embedded` | `Packages/com.xpturn.feedback/` |

`Library/` exists once the editor has opened the project. When it is missing, or the folder is not there, ask the person
to open the project in the editor once, or to give the package's folder.

In the package's folder:

- `package.json` — the version.
- `Documentation~` — `Documentation~/index.md` and the pages this skill points at, such as `Documentation~/Feedbacks.md`.
- `Presets/` — the presets. The GUID a scene or a prefab uses to link one is in its `.asset.meta` file; the table in
  [Writing files](authoring-files.md#the-presets) lists them.
- `README.md` — the requirements, the install, the samples and what to know before a build (`README.md#good-to-know`).

The editor's Project window shows the package under `Packages/com.xpturn.feedback` whatever its source. For a git,
registry or tarball install that path is not a folder on disk.
