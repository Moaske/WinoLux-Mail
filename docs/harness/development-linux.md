# Linux development

This is the guide for the planned Linux application in `src/Wino.Mail.Linux` and its solution `WinoMail.Linux.slnx`, part of the WinoLux Mail fork.

**Status (6 November 2026): planned.** None of the Linux projects exist yet. This guide records the commands and checks they will use, so each milestone lands against a known target. Sections are marked *planned* until the step that makes them true is merged.

The app uses the shared .NET services, ViewModels, translations, and editor engine through explicit adapters over GTK 4 and libadwaita (Gir.Core bindings). It does not use Avalonia, Uno, MAUI, or Electron.

Shared rules for every platform are in [AGENTS.md](../../AGENTS.md) and the [implementation rules](implementation-rules.md). Presentation decisions are in [Linux design decisions](../linux-design-decisions.md).

## Requirements

Linux builds run on Linux x64. The reference machine is Ubuntu 26.04 LTS with GNOME 50.

- The .NET SDK selected by `global.json` (10.0.401). On Ubuntu 26.04: `sudo apt install dotnet-sdk-10.0`, or the [Microsoft install script](https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual) when the Ubuntu package lags behind `global.json`.
- Run-time libraries, already present on a standard Ubuntu desktop: GTK 4, libadwaita, WebKitGTK 6.0, and libsecret. On a minimal install: `sudo apt install libgtk-4-1 libadwaita-1-0 libwebkitgtk-6.0-4 libsecret-1-0`.
- A Secret Service for credentials. GNOME Keyring is the Ubuntu default.

No `-dev` packages are needed: Gir.Core loads the libraries at run time.

## Try a build without a terminal

Every push to a branch of the fork runs the Linux workflow in GitHub Actions *(planned, milestone 1)*:

1. Open the repository on GitHub, then **Actions**, then the latest **Linux** run.
2. Under **Artifacts**, download `WinoLux-linux-x64`.
3. Extract it in the Files app, right-click `WinoLux` → **Properties** → enable **Executable as Program**, then double-click it.

## Platform selection

Linux builds use `WinoTargetPlatform=Linux` *(planned, milestone 1)*. As for Windows and macOS, a Linux host selects it when the value is not passed; an explicit `-p:WinoTargetPlatform=...` wins. `Directory.Build.targets` imports `build/Wino.Linux.targets` when `WinoTargetPlatform` is `Linux`.

The shared Release gate (Native AOT, trimming, warnings as errors) applies unchanged to every shared library built for Linux. Whether the GTK projects keep it is open item L5 in the design decisions.

## Projects

*(Planned.)* See the project map in [Linux design decisions](../linux-design-decisions.md#project-map).

| Project | Role |
| --- | --- |
| `src/Wino.Mail.Linux` | App head (`io.github.moaske.WinoLux`) |
| `src/Wino.Platform.Linux` | Platform services; no presentation references |
| `controls/Wino.Presentation.Gtk` | Units, WinoIcons, libadwaita CSS, zones, backdrops, bindings |
| `controls/Wino.Mail.Controls.Gtk` | Wino-drawn rows and controls |
| `controls/Wino.Editor.Gtk` | `WebKitWebView` reader and editor sessions over `Wino.Editor.Core` |

## Build and run

*(Planned, milestone 2.)* From the repository root:

```sh
dotnet build src/Wino.Mail.Linux/Wino.Mail.Linux.csproj
dotnet run --project src/Wino.Mail.Linux/Wino.Mail.Linux.csproj
```

Release archive, as the workflow builds it:

```sh
dotnet publish src/Wino.Mail.Linux/Wino.Mail.Linux.csproj -c Release -r linux-x64 -o artifacts/linux-x64
```

GTK's inspector helps with layout checks: run with `GTK_DEBUG=interactive`, or press Ctrl+Shift+D in the running app after enabling it with `gsettings set org.gtk.gtk4.Settings.Debug enable-inspector-keybinding true`.

## Continuous integration

*(Planned, milestone 1.)* `.github/workflows/linux.yml` runs on every push and pull request:

| Job | Runner | Checks |
| --- | --- | --- |
| Linux | `ubuntu-latest` | Debug build of the shared projects with `WinoTargetPlatform=Linux`, then of the Linux head; Release publish for `linux-x64`; uploads the `WinoLux-linux-x64` artifact |
| Windows gate | `windows-latest` | For changes to shared code or build files: the Windows Debug x64 build and the Release x64 gate from `AGENTS.md` |
| Android | `windows-latest` | For changes to shared code or build files: the Android Debug build |

macOS and iOS builds need Xcode and Apple signing. For shared changes they are reported as a handoff in the pull request, as `AGENTS.md` requires when no Mac is available.

## Sign-in registrations

*(Planned.)* Outlook and Gmail need WinoLux's own registrations; the repository's hard rules forbid using upstream's OAuth clients for another purpose.

- **Outlook:** a Microsoft Entra public-client app with the redirect `http://localhost` (MSAL's system-browser flow on Linux).
- **Gmail:** a Google Cloud OAuth client of type "Desktop app", for the shared loopback flow.

Client IDs live in the Linux platform project's configuration, never in shared code. Until they exist, onboarding offers IMAP and SMTP only.

## Verification

| Change | Required evidence |
| --- | --- |
| GTK views, navigation, activation, or controls | Linux Debug build in CI and manual checks on Ubuntu 26.04: light and dark, a backdrop theme, the three size classes (resize the window across 1008 and 640 px), keyboard only, Large Text, and Orca for new controls |
| Linux platform services | Linux Debug build and a manual check of the affected integration (Secret Service, notifications, portals, `mailto:`) |
| Shared production code, dependencies, or build configuration | Everything in the `AGENTS.md` table, through the CI jobs above, plus the macOS and iOS handoff |
| Documentation | Valid links |

A passing build does not prove UI behavior. A UI check counts as done only when someone performed it on the reference machine; the pull request says who.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| `DllNotFoundException` for `libadwaita-1.so.0` or `libwebkitgtk-6.0.so.4` | A run-time library is missing; install the packages under [Requirements](#requirements) |
| Account setup stops with "No secret service" | No Secret Service is running (common on minimal or non-GNOME sessions); start GNOME Keyring or install another provider |
