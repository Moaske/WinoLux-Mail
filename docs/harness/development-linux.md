# Linux development

This is the guide for the Linux application in `src/Wino.Mail.Linux` and its solution `WinoMail.Linux.slnx`, part of the WinoLux Mail fork.

**Status (23 November 2026): milestone 2f.** Linux is a build platform; CI builds and packages the head, which starts the shared runtime through the Linux platform services (data under the XDG folders, the protection key in the Secret Service, the shared startup order). Without accounts the window opens onboarding: Welcome, the provider wizard (provider, account name and colour, what the account is used for), the IMAP sign-in and server pages, and setup progress. Outlook and Gmail stay hidden until WinoLux has its own sign-in registrations (L3). With an account the window shows the shell: the sidebar with the Mail menu (New Mail, accounts, folders with unread counts), the sync button with the folder title, the main menu and the mode switcher, beside the Mail page's list and reader panes in Wino's three size classes. The list shows the folder's mail in date groups (sender picture or initials, sender, subject, preview, date, unread, flag and attachment marks, threads) and loads more as it scrolls. The reading pane shows the selected message: subject, sender with picture, recipients and date, the remote-content bar with Load once, the body through the shared reader engine in a WebKitGTK view, and the attachments with Open, Save and Save all. Without WebKitGTK the pane says which package to install. Mail actions run through the shared ViewModel: the reader column's header bar (Archive, Delete, Move · Flag, Mark read · More) acts on the selection, a row's context menu (secondary click, the Menu key or Shift+F10) offers the shared actions with Pin and the thread toggle, and the shared keyboard shortcuts work in the window. Reply, Reply all, Forward and New Mail wait for the composer (2g). Below 800 px the list and the reader show one at a time; Calendar, Contacts, To Do and Settings show "Coming soon". Sections still marked *planned* describe later milestones.
**Status (22 November 2026): milestone 2c.** Linux is a build platform; CI builds and packages the head, which starts the shared runtime through the Linux platform services (data under the XDG folders, the protection key in the Secret Service, the shared startup order). Without accounts the window opens onboarding: Welcome, the provider wizard (provider, account name and colour, what the account is used for), the IMAP sign-in and server pages, and setup progress. Outlook and Gmail stay hidden until WinoLux has its own sign-in registrations (L3). With an account the window shows the shell: the sidebar with the Mail menu (New Mail, accounts, folders with unread counts), the sync button with the folder title, the main menu and the mode switcher, beside the Mail page's list and reader panes in Wino's three size classes. The list and reader stay empty until milestone 2d; Calendar, Contacts, To Do and Settings show "Coming soon". Sections still marked *planned* describe later milestones. The [roadmap](../linux-roadmap.md) lists every milestone.

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

The shared Release gate (Native AOT, trimming, warnings as errors) applies unchanged to every shared library built for Linux. GTK projects set `WinoGtkProject`. They keep the gate too (L5 in the design decisions), and CI enforces it on every push. `WinoGtkAotExempt=true` exempts only the GTK projects; use it only for a Gir.Core feature that cannot pass, and record why in the design decisions.

## Projects

See the project map in [Linux design decisions](../linux-design-decisions.md#project-map).

| Project | Status | Role |
| --- | --- | --- |
| `src/Wino.Mail.Linux` | Onboarding (IMAP), shell, message list, reader | App head (`io.github.moaske.WinoLux`, program `WinoLux`) |
| `src/Wino.Platform.Linux` | Startup services | Platform services; no presentation references |
| `controls/Wino.Presentation.Gtk` | Planned | Units, WinoIcons, libadwaita CSS, zones, backdrops, bindings |
| `controls/Wino.Mail.Controls.Gtk` | Planned | Wino-drawn rows and controls |
| `controls/Wino.Editor.Gtk` | Reader | `WebKitWebView` reader (and, with 2g, editor) sessions over `Wino.Editor.Core` |

## Build and run

From the repository root:

```sh
dotnet build WinoMail.Linux.slnx
dotnet run --project src/Wino.Mail.Linux/Wino.Mail.Linux.csproj
```

The head publishes self-contained for `linux-x64` unless another `-r` is passed. Release archive and Native AOT build, as the workflow builds them:

```sh
dotnet publish src/Wino.Mail.Linux/Wino.Mail.Linux.csproj -c Release -r linux-x64 -o artifacts/WinoLux
dotnet publish src/Wino.Mail.Linux/Wino.Mail.Linux.csproj -c Release -r linux-x64 -p:WinoLinuxNativeAot=true -o aot
```

Native AOT needs `clang` and `zlib1g-dev` (`sudo apt install clang zlib1g-dev`). `WinoLinuxNativeAot` sets `PublishAot` on the head only; a global `-p:PublishAot=true` also reaches the netstandard2.0 source generator and fails with NETSDK1207.

GTK's inspector helps with layout checks: run with `GTK_DEBUG=interactive`, or press Ctrl+Shift+D in the running app after enabling it with `gsettings set org.gtk.gtk4.Settings.Debug enable-inspector-keybinding true`.

## Pages and the debug bridge

The shell (`Views/Shell/WinoAppShellPage`) follows the Mac `WinoAppShellViewController`: the shared `ShellModeActivator` switches modes, the sidebar draws the shared `ShellMenuRowPresentation` rows (as Android does), and the mode's root page is routed into the content area as `NavigationHostKind.ShellContent`. `ShellWindow` reports Wino's size classes through two `AdwBreakpoint`s (Medium below 1008 px, Small below 641 px). Icons are the WinoIcons Material Rounded font (L1), shipped as `PackagedResources/Fonts/WinoIcons.ttf` and registered with fontconfig before GTK starts (`Presentation/WinoIcons`); the few Wino styles are in `Presentation/WinoStyles`.


The Mail page (`Views/Mail/MailListPage`) follows the Mac `MailListPageViewController`: the shared `MailListProjection` turns the ViewModel's mail into date groups and threads, and the shared `MailRowPresentation` gives each row its text and state. The list is a virtualised `GtkListView` whose model holds only each entry's key in a `GtkStringList`, so it needs no GObject subclass; projection changes are coalesced and applied as one splice of the changed part. Selection goes to the ViewModel as the shared selection snapshot. Sender pictures come from the shared thumbnail service (`LinuxSenderPictureProvider`) and fall back to `AdwAvatar` initials.

Pages follow the Mac head: each routed page is a `LinuxPage<TViewModel>` (in `Infrastructure/LinuxPage.cs`) that builds its widgets once in `BuildView`, binds them to the shared ViewModel through the shared `Wino.Presentation.Bindings` (`GtkForm` adds the GTK widgets), and gets the shared navigation lifecycle from `LinuxNavigationService`. `LinuxPageRegistry` maps each `WinoPage` to a page; the composition root registers every page explicitly, so Native AOT keeps them.

Debug builds start a file bridge, the Mac `MacDebugBridge`'s twin, for checks without screen access. Write one command per line to `~/.cache/WinoLux/debug/cmd`; each result is appended to `~/.cache/WinoLux/debug/out.log`.

| Command | Does |
| --- | --- |
| `commands` | Lists the commands the current pages registered |
| `page [WinoPage]` | Without a page: the page in the window. With one: navigates there |
| `focus NAME` | Gives a registered field keyboard focus, so a test types into it like a person (`imap-password`) |
| `welcome-start` | Starts setup from Welcome |
| `provider NAME\|TYPE`, `provider-name NAME`, `provider-capability AREA MODE`, `provider-continue`, `provider-state` | Drive the provider wizard |
| `imap-identity NAME ADDRESS`, `imap-incoming HOST PORT SECURITY AUTH`, `imap-outgoing ...`, `imap-continue`, `imap-state` | Drive the IMAP pages (security and authentication are drop-down positions; `imap-state` lists them) |
| `setup-state` | Setup progress |
| `shell-state` | Mode, selected folder, pane title and subtitle, sidebar state, size class, content page, and the sidebar rows |
| `folder NAME`, `mode MODE`, `sidebar-toggle`, `sync` | Click a sidebar folder, pick a mode segment, open or close the sidebar, press the sync button |
| `mail-state` | Mail rows, entries (rows and date headers), selection count, folder, and the first entries (`*` marks unread) |
| `mail-select N...` or `mail-select TEXT` | Selects mail rows by position among the mail rows (date headers not counted), or the first row whose subject contains the text |
| `mail-command [archive\|delete\|move\|flag\|read\|more]` | Without an argument: the reader header's commands and their tooltips. With one: clicks that command |
| `mail-context N` | Opens the context menu on the Nth mail row and answers its items (disabled items in parentheses) |
| `shortcut KEY [Control] [Shift] [Alt]` | Runs the Mail shortcut for that key as if pressed in the list (shared policy applies) |
| `reader-state`, `reader-body` | The reader's subject, sender, recipients, attachments and remote-content state; the first part of the rendered body text |

The bridge only selects, navigates and focuses. No command fills a password: a test focuses the field and types, as a person would. Release builds do not contain the bridge.

## Continuous integration

Two workflows run in GitHub Actions:

| Workflow | Job | Runner | When | Checks |
| --- | --- | --- | --- | --- |
| `linux.yml` | Build and package | `ubuntu-latest` | Every push | Debug build of `WinoMail.Linux.slnx`; Release gate for the shared and GTK projects; self-contained `linux-x64` publish; uploads `WinoLux-linux-x64` |
| `linux.yml` | Native AOT build | `ubuntu-latest` | Every push | Native AOT publish of the head; uploads `WinoLux-linux-x64-aot` (the release build, L5); trim and AOT warnings in the run summary |
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

## Where WinoLux keeps its files

| What | Where |
| --- | --- |
| Database, logs (`Logs/`), pictures | `~/.local/share/WinoLux/data` |
| Protected credentials and tokens | `~/.local/share/WinoLux/secure` |
| Settings file | `~/.config/WinoLux` |
| Temporary files | `~/.cache/WinoLux/temp` |
| Protection key | The login keyring: Passwords and Keys (Seahorse) shows it as "WinoLux Mail protection key" |

Starting over: quit WinoLux, delete the three `WinoLux` folders, then the keyring item. The secure items cannot be read without that key, so delete them together.

Debug builds log at Debug level. Release builds, including the Native AOT build, log only fatal errors unless logging is turned on in Settings.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| `DllNotFoundException` for `libadwaita-1.so.0` or `libwebkitgtk-6.0.so.4` | A run-time library is missing; install the packages under [Requirements](#requirements) |
| The window shows "The Secret Service is unavailable" at startup | No Secret Service is running (common on minimal or non-GNOME sessions); start GNOME Keyring or install another provider. Nothing is stored without it |
