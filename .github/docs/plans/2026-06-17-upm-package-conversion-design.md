# horizOn SDK for Unity: UPM Git URL package conversion

Date: 2026-06-17
Status: implemented on `feat/upm-package`, pending Unity import verification

## Goal

Make `horizOn-SDK-Unity` installable in the Unity Package Manager via a bare git
URL, exactly like the reference package `Unity-PmPrefs` ("pm press"), from a
single branch that also keeps producing a downloadable `.unitypackage`. The SDK
code itself is not rewritten; only the packaging scaffolding around it is built.

## Decision

The package now lives at the repository root (the PmPrefs layout). A consumer
adds it with:

```
https://github.com/ProjectMakersDE/horizOn-SDK-Unity.git
```

Pinning a release works by appending a tag, e.g. `...git#v1.5.1`.

One branch (`master`) serves both distribution channels:
1. UPM git URL install (package.json at repo root).
2. A `.unitypackage` attached to each GitHub Release.

A second branch was explicitly avoided.

## What changed

### Layout (move, GUIDs preserved)
Everything under `Assets/Plugins/ProjectMakers/horizOn/` moved to the repo root,
each file carried together with its `.meta` so GUIDs are unchanged:
- `CloudSDK/` runtime and editor code.
- `Documentation~/` (was `Documentation/`): hidden from the Unity importer.
- `Samples~/Examples` and `Samples~/ExampleUI` (were `CloudSDK/Examples` and
  `CloudSDK/ExampleUI`): importable on demand via the Package Manager Samples UI,
  declared in `package.json` `samples[]`. They no longer compile into every
  consumer project.
- `QUICKSTART.md`, the canonical `README.md`, `CHANGELOG.md`.
The empty `Assets/` tree and its wrapper folder metas were removed. The redundant
in-package `README.md` was dropped in favour of the root README.

### New package files
- `package.json` (`com.projectmakers.horizon`, SemVer, `unity: 2023.3`, samples,
  author, license, doc and changelog URLs) plus its `.meta`.
- `LICENSE.md` (MIT; the README already declared MIT but the file was missing and
  the link was dead).
- Two assembly definitions, required because code under a package is not part of
  the predefined assemblies:
  - `CloudSDK/ProjectMakers.Horizon.asmdef` (runtime, minimal, autoReferenced).
  - `CloudSDK/Editor/ProjectMakers.Horizon.Editor.asmdef`
    (`includePlatforms: ["Editor"]`, GUID reference to the runtime assembly).

### Missing `.meta` backfill (pre-existing bug fix)
21 tracked source files had no committed `.meta` (9 runtime, among them
`CrashManager.cs`, `EmailSendingManager.cs`, `CrashType.cs`, plus the crash and
email request/response types, 11 example scripts, and one doc). The previous
`.unitypackage` build listed assets via `find -name "*.meta"`, so those 21 files
were silently dropped from every released `.unitypackage`, which therefore could
not compile on import. `.github/scripts/generate_missing_meta.py` created the missing
`.meta` (canonical importer blocks, collision-checked GUIDs).

### `.unitypackage` build (license-free, locally verifiable)
`.github/scripts/build-unitypackage.py` replaces the unpinned third party
`pCYSl5EDgo/create-unitypackage@master` action. It reads the repo-root package and
emits a `.unitypackage` (gzip tar of `<guid>/{pathname,asset.meta,asset}`) whose
assets import into the historical `Assets/Plugins/ProjectMakers/horizOn/...`
paths, so existing `.unitypackage` users keep the same layout. It needs no Unity
license and runs locally, so the artifact is verifiable in CI and on a developer
machine.

### CI and semantic-release
- `.releaserc.json`: version is now bumped in `package.json` (so the committed
  manifest matches each `vX.Y.Z` tag), in the README version badge, and in the API
  reference; all three plus `CHANGELOG.md` are committed by `@semantic-release/git`.
- `.github/workflows/release.yml`: the build job now runs the Python exporter.
  The release job (semantic-release, changelog sync dispatch, example-repo update
  dispatch) is unchanged.
- `.gitignore`: the gitignored consumer-config `Resources/` path was repointed to
  `/CloudSDK/Resources/`; `*.unitypackage` is now ignored.

## Why no SDK code change was needed
- The editor config importer writes the config asset to a fixed `Assets/...` path
  (the consumer's writable project), not relative to the script, so it works even
  when the SDK is installed read-only under `Packages/`.
- `HorizonConfig.Load()` uses `Resources.Load("horizOn/HorizonConfig")`, which
  scans `Resources` folders across both `Assets` and packages.
- Adding asmdefs moves scripts into named assemblies, but Unity binds serialized
  assets by the script GUID (preserved in `.meta`), not by assembly, so existing
  `HorizonConfig.asset` references keep resolving.

## Verification performed (no Unity required)
- `package.json`, both asmdefs and `.releaserc.json` parse as JSON; the editor
  asmdef references the runtime asmdef GUID.
- Every shipped asset has a `.meta`; no duplicate GUIDs anywhere in the repo.
- The exporter builds a valid gzip `.unitypackage`: every pathname imports under
  `Assets/Plugins/ProjectMakers/horizOn/`, `HorizonApp.cs` bytes match source, and
  the formerly-missing files (`CrashManager`, `EmailSendingManager`, examples) are
  now included; `package.json` is not leaked into the `.unitypackage`.
- Runtime code (CloudSDK outside `Editor/`) references no `UnityEditor` API, so it
  is player-safe under the runtime asmdef.
- All three semantic-release replacement targets contain a matching version string.

## Known risks and follow-ups
1. Unity compilation cannot be checked headlessly. The one thing to confirm in a
   real editor is that the editor assembly compiles for an iOS target (the
   `#if UNITY_IOS` post-processor uses `UnityEditor.iOS.Xcode`, expected to be
   auto-referenced). Final acceptance is: open a project, add the git URL, confirm
   it compiles, import a sample, run the Config Importer.
2. The 9 runtime files that received backfilled GUIDs had no canonical GUID before
   (each prior install generated its own), so committing canonical GUIDs is the
   correct fix; users who placed those specific scripts as serialized scene
   components in an older manual install may need to reassign them once. Practically
   near zero, since those types are used as singletons or plain data.
3. Merging to `master` triggers the semantic-release pipeline (new version,
   `.unitypackage`, changelog and example-repo dispatches). Run the Unity import
   check before merging.
