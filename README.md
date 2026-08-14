# UPPM - Unity Publisher Package Manager

UPPM is a Unity Editor toolkit for preparing, managing, and publishing assets as UPM packages.

The toolkit focuses on a practical workflow:

- keep `Samples~` and `Documentation~` hidden or visible when needed;
- build an embedded package under `Packages/<Package Id>`;
- safely return the package to the project layout;
- preserve stable GUIDs so imported samples keep their references;
- synchronize selected `package.json` fields and `PlayerSettings.bundleVersion`.

## Features

### Toggle Samples and Documentation folders

- Renames `Samples~` <-> `Samples` and `Documentation~` <-> `Documentation`.
- Uses the `SAMPLES_RENAMED` scripting define to keep compilation state consistent.
- Provides commands under the `SCG/` menu.

### Optional root mirrors for hidden folders

- Adds `SCG/Enable Samples and Documentation root sync`.
- When enabled, maintains editable mirrors at `Assets/~Samples~` and `Assets/~Documentation~` while the package folders remain hidden.
- Copies mirror changes back with their `.meta` files intact.
- Removes mirrors only when GUIDs and contents still match; otherwise it reports a conflict instead of overwriting data.

### Preserve stable GUIDs for Samples

Missing `.meta` files can change GUIDs and break references when samples are imported through Package Manager.

`SamplesMetaBaker`:

- temporarily imports assets through `Assets/__SamplesMetaBake__` so Unity generates missing `.meta` files;
- copies generated metadata back into `Samples~`;
- never overwrites existing metadata.

UPPM also preserves the folder GUIDs for `Samples` and `Documentation` while their tilde-suffixed forms are hidden. Hidden folders never retain adjacent `.meta` files that would trigger Unity warnings.

### Build and return an embedded UPM package

`UpmPackageBuilder` switches between project and embedded-package layouts.

Build for UPM:

- hides `Samples~` and `Documentation~` before building;
- stages the package and moves it under `Packages/<Package Id>`;
- ensures that an effective `package.json` exists in the embedded package root;
- bakes missing sample metadata;
- resolves Package Manager and adds the `UPM_PACKAGE` define after registration.

Return to project:

- moves the package out of `Packages` before resolving Package Manager;
- returns the folder and its root `.meta` to `Assets`;
- restores the Samples visibility state changed by the build workflow;
- imports the returned folder and removes the `UPM_PACKAGE` define.

Build and return requests survive compilation and AppDomain reloads. Invalid or failed persisted operations are cleared to prevent retry loops.

### `package.json` utilities

`PackageJsonUtility` reads and writes `name`, `version`, `displayName`, and `description` through `TextAsset` references or filesystem paths.

### Settings asset

`UppmSettings` is the central editor configuration. It stores the package root, package manifest reference, package ID, and optional synchronized metadata.

### Define symbol management

`DefineSymbolsManager` adds, removes, and checks scripting defines. It supports Unity 2021.2+ through `NamedBuildTarget` and falls back to the legacy APIs when required.

## Installation

### Git URL

In Unity, open `Window > Package Manager`, click `+`, select **Add package from git URL**, and enter:

```text
https://github.com/SpaceCatGames/UnityPublisherPackageManager.git?path=Assets/SCG
```

The same URL can be used in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "scg.unity.uppm": "https://github.com/SpaceCatGames/UnityPublisherPackageManager.git?path=Assets/SCG"
  }
}
```

### Local package

Choose **Add package from disk...** in Package Manager and select `Assets/SCG/package.json` from this repository.

## Setup

1. Create the settings asset through `Assets > Create > SCG > UppmSettings`.
2. Place it in a `Resources` folder so `UppmSettings.Instance` can load it. For an editor-only asset, use `Assets/Editor/Resources/`.
3. Configure:
   - **Asset Root Folder**: package folder name under `Assets` in project mode;
   - **Package Id**: identifier from `package.json` and embedded folder name under `Packages`;
   - **Base Folder**: package root folder asset;
   - **Package Asset**: `package.json` as a `TextAsset`;
   - optional version, display name, and description synchronization.

After switching layouts, **Base Folder** can temporarily appear as `Missing (Object)` because its folder is physically moving. UPPM resolves the configured Assets or Packages location after the editor refreshes.

## Menu commands

All commands are located under `SCG/`:

- **Show Samples and Documentation folders** / **Hide Samples and Documentation folders** toggles the tilde-suffixed folder names.
- **Enable Samples and Documentation root sync** toggles editable root mirrors. The setting is stored in `UppmSettings` and disabled by default.
- **Build for UPM Package** converts the configured folder into an embedded package.
- **Return from UPM Package (to project)** restores the project layout.
- **Enable UNITY_ASTOOLS_EXPERIMENTAL Define** adds the optional experimental define.

## Completion events

Editor tools can continue after UPPM finishes an operation without polling `EditorApplication.update` or counting frames. Completion notifications are stored under the project's `Temp` directory so they survive compilation and AppDomain reloads while that directory remains intact. They are published after the editor becomes idle, but are not guaranteed to survive an editor restart, `Temp` cleanup, or project backup and restoration.

Static event subscriptions do not survive an AppDomain reload. Subscribing immediately before `BuildOrReturn()`, `EnsureVisible()`, or `EnsureHidden()` is valid only when that particular operation does not trigger compilation. For reliable delivery after a scripting-define change, restore the subscription from `InitializeOnLoadMethod` as shown below. Removing the handler before adding it makes repeated initialization idempotent.

```csharp
[InitializeOnLoadMethod]
private static void Initialize()
{
    UpmPackageBuilder.ReturnCompleted -= OnReturnCompleted;
    UpmPackageBuilder.ReturnCompleted += OnReturnCompleted;
    SamplesRenamer.VisibilityChangeCompleted -= OnVisibilityChangeCompleted;
    SamplesRenamer.VisibilityChangeCompleted += OnVisibilityChangeCompleted;
}

private static void OnReturnCompleted(string returnedRootPath)
{
    // Continue work that requires the package under Assets.
}

private static void OnVisibilityChangeCompleted(
    SamplesVisibility visibility,
    string packageRootPath)
{
    // Continue work after Samples and Documentation finish importing.
}
```

Available package events:

- `UpmPackageBuilder.ActionCompleted` for any completed package action;
- `UpmPackageBuilder.BuildCompleted` after conversion to an embedded package;
- `UpmPackageBuilder.ReturnCompleted` after return to the project;
- `SamplesRenamer.VisibilityChangeCompleted` after a visibility request completes.

`EnsureVisible()` and `EnsureHidden()` publish completion even when the requested state is already applied. Concurrent visibility requests are serialized in FIFO order. Persist consumer-specific intent before starting an operation because UPPM events describe completed library operations, not product-specific follow-up work.

## Notes

- UPPM is editor-only.
- Folder moves can fail while project files are locked by another application.
- `SamplesMetaBaker` removes its temporary `Assets/__SamplesMetaBake__` folder after use.

## License

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
