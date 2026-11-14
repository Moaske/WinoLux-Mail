# Linux development

This is the guide for the Linux application in `src/Wino.Mail.Linux` and its solution `WinoMail.Linux.slnx`, part of the WinoLux Mail fork.

**Status (14 November 2026): milestone 1.** Linux is a build platform, CI builds and packages the head, and the head opens a toolchain window (an empty libadwaita window that loads the shared domain). Sections still marked *planned* describe later milestones.

The app uses the shared .NET services, ViewModels, translations, and editor engine through explicit adapters over GTK 4 and libadwaita (Gir.Core bindings). It does not use Avalonia, Uno, MAUI, or Electron.

Shared rules for every platform are in [AGENTS.md](../../AGENTS.md) and the [implementation rules](implementation-rules.md). Presentation decisions are in [Linux design decisions](../linux-design-decisions.md).

## Requirements

Linux builds run on Linux x64. The reference machine is Ubuntu 26.04 LTS with GNOME 50.

- The .NET SDK selected by `global.json` (10.0.401). On Ubuntu 26.04: `sudo apt install dotnet-sdk-10.0`, or the [Microsoft install script](https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual) when the Ubuntu package lags behind `global.json`.
- Run-time libraries, already present on a standard Ubuntu desktop: GTK 4, libadwaita, WebKitGTK 6.0, and libsecret. On a minimal install: `sudo apt install libgtk-4-1 libadwaita-1-0 libwebkitgtk-6.0-4 libsecret-1-0`.
- A Secret Service for credentials. GNOME Keyring is the Ubuntu default.

No `-dev` packages are needed: Gir.Core loads the libraries at run time.

## Try a build without a terminal

Every push to a branch of the fork runs the **Linux** workflow in GitHub Actions:

1. Open the repository on GitHub, then **Actions**, then the latest **Linux** run.
2. Under **Artifacts**, download `WinoLux-linux-x64`. GitHub delivers it as a `.zip` that holds `WinoLux-linux-x64.tar.gz`; the archive keeps the program's executable permission.
3. In the Files app, double-click the `.zip`, then the `.tar.gz`, and extract the `WinoLux` folder.
4. Open the `WinoLux` folder, right-click an empty area → **Open in Terminal**, and type `./WinoLux`.

The same run offers `WinoLux-linux-x64-aot`: the Native AOT build of the head (a single `WinoLux-aot/WinoLux` program). Start it the same way.

## Platform selection

Linux builds use `WinoTargetPlatform=Linux`. As for Windows and macOS, a Linux host selects it when the value is not passed; an explicit `-p:WinoTargetPlatform=...` wins. `Directory.Build.targets` imports `build/Wino.Linux.targets` when `WinoTargetPlatform` is `Linux`.

The shared Release gate (Native AOT, trimming, warnings as errors) applies unchanged to every shared library built for Linux. GTK projects set `WinoGtkProject`. They keep the gate too unless `WinoGtkAotExempt=true` is passed; whether they need that is L5 in the design decisions, measured by the AOT probe below.

## Projects

See the project map in [Linux design decisions](../linux-design-decisions.md#project-map).

| Project | Status | Role |
| --- | --- | --- |
| `src/Wino.Mail.Linux` | Toolchain window | App head (`io.github.moaske.WinoLux`, program `WinoLux`) |
| `src/Wino.Platform.Linux` | Planned | Platform services; no presentation references |
| `controls/Wino.Presentation.Gtk` | Planned | Units, WinoIcons, libadwaita CSS, zones, backdrops, bindings |
| `controls/Wino.Mail.Controls.Gtk` | Planned | Wino-drawn rows and controls |
| `controls/Wino.Editor.Gtk` | Planned | `WebKitWebView` reader and editor sessions over `Wino.Editor.Core` |

## Build and run

From the repository root:

```sh
dotnet build WinoMail.Linux.slnx
dotnet run --project src/Wino.Mail.Linux/Wino.Mail.Linux.csproj
```

The head publishes self-contained for `linux-x64` unless another `-r` is passed. Release archive and Native AOT build, as the workflow builds them:

```sh
dotnet publish src/Wino.Mail.Linux/Wino.Mail.Linux.csproj -c Release -r linux-x64 -p:WinoGtkAotExempt=true -o artifacts/WinoLux
dotnet publish src/Wino.Mail.Linux/Wino.Mail.Linux.csproj -c Release -r linux-x64 -p:WinoLinuxNativeAot=true -o aot
```

Native AOT needs `clang` and `zlib1g-dev` (`sudo apt install clang zlib1g-dev`). `WinoLinuxNativeAot` sets `PublishAot` on the head only; a global `-p:PublishAot=true` also reaches the netstandard2.0 source generator and fails with NETSDK1207.

GTK's inspector helps with layout checks: run with `GTK_DEBUG=interactive`, or press Ctrl+Shift+D in the running app after enabling it with `gsettings set org.gtk.gtk4.Settings.Debug enable-inspector-keybinding true`.

## Continuous integration

Two workflows run in GitHub Actions:

| Workflow | Job | Runner | When | Checks |
| --- | --- | --- | --- | --- |
| `linux.yml` | Build and package | `ubuntu-latest` | Every push | Debug build of `WinoMail.Linux.slnx`; Release gate for the shared projects (GTK projects exempt in this step); self-contained `linux-x64` publish; uploads `WinoLux-linux-x64` |
| `linux.yml` | Native AOT probe | `ubuntu-latest` | Every push | Release build of the head with the full AOT gate, then a Native AOT publish; uploads `WinoLux-linux-x64-aot`; trim and AOT warnings in the run summary. Informational (L5) |
| `shared-gate.yml` | Windows | `windows-latest` | Pull requests and `main` pushes that touch shared code or build files | Windows Debug x64 build and the Release x64 gate from `AGENTS.md` |
| `shared-gate.yml` | Android | `windows-latest` | As above | Android Debug build, with the workload set pinned to 10.0.401.1 as the Android guide requires |

macOS and iOS builds need Xcode and Apple signing. For shared changes they are reported as a handoff in the pull request, as `AGENTS.md` requires when no Mac is available.

## Sign-in registrations

*(Planned, needed from the Outlook and Gmail milestone.)* WinoLux uses its own OAuth registrations; the repository's hard rules forbid using upstream's clients for another purpose. Until they exist, onboarding offers IMAP and SMTP only (L3).

### Outlook: Microsoft Entra app

1. Sign in at <https://entra.microsoft.com> with a Microsoft account, then **App registrations** → **New registration**.
2. Name `WinoLux Mail`. Supported account types: **Accounts in any organizational directory and personal Microsoft accounts**.
3. Redirect URI: platform **Public client/native (mobile & desktop)**, value `http://localhost`.
4. Under **Authentication**, set **Allow public client flows** to **Yes**. No client secret is created.
5. Under **API permissions**, add the delegated Microsoft Graph permissions the shared authenticator requests (`MailAuthenticatorConfiguration` in `Wino.Authentication`): `email`, `offline_access`, `User.Read`, `Mail.ReadWrite`, `Mail.Send`, `Mail.ReadWrite.Shared`, `Mail.Send.Shared`, `MailboxSettings.ReadWrite`, `Calendars.ReadWrite`, `Calendars.ReadWrite.Shared`, `Contacts.ReadWrite`, `Tasks.ReadWrite`.
6. Copy the **Application (client) ID** from **Overview**.

Personal Outlook.com accounts work with an unverified app. Work and school tenants may block unverified apps until an administrator consents or the app gets publisher verification.

### Gmail: Google Cloud desktop client

1. At <https://console.cloud.google.com>, create a project `WinoLux Mail`.
2. Enable the **Gmail API**, **Google Calendar API**, **People API**, **Google Tasks API**, and **Google Drive API** (the authenticator requests `drive.file`).
3. Under **Google Auth Platform**, configure the consent screen: audience **External**, publishing status **Testing**, and add your own Google address as a test user.
4. Under **Clients**, create an OAuth client of type **Desktop app**, then copy the client ID. Google does not treat a desktop client's secret as confidential.

Gmail's full-mailbox scope (`https://mail.google.com/`) is a restricted scope. In Testing mode only listed test users (up to 100) can sign in, and Google expires their sign-in after 7 days, so the account asks to sign in again weekly. Publishing for everyone requires Google's app verification and a yearly third-party security assessment. For personal use, Gmail also works through IMAP with an app password (needs 2-Step Verification on the Google account).

### Where the IDs go

Client IDs belong to the Linux platform project's configuration, never to shared code. They identify the app but are not secrets; CI builds read them from GitHub Actions repository variables, so forks of WinoLux can use their own registrations.

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
