# Wino Mail for Linux: design decisions

Proposed on 6 November 2026 for the WinoLux Mail fork (`Moaske/WinoLux-Mail`), an unofficial Linux port of Wino Mail by bkaankose. Nothing here is decided upstream. These decisions fix the shell, workflow, control, and background choices for the planned GNOME app in `src/Wino.Mail.Linux`, its platform project `src/Wino.Platform.Linux`, and the GTK control libraries under `controls/`. Shared ViewModels, services, sync, storage, and the HTML reader/editor engine are unchanged by them.

Design canvas: <https://claude.ai/artifact/9vYvjoR5uAgLLo2cVvXeyK> (boards: main window light; dark with search open; medium width with no message selected; small width list and drill-in reader; compose in the reading pane; Settings window on Message List; dialogs, popovers, toasts, and info bars; a backdrop theme with floating zones). The canvas is private until its owner shares it. Where the canvas and this document disagree, this document wins.

Build, CI, and verification commands are in [Linux development](harness/development-linux.md). The [macOS design decisions](macos-design-decisions.md) and the [Android design decisions](android-design-decisions.md) apply where this document is silent; shared naming and order come from the [Wino design guideline](wino-design-guideline.md).

## Relation to upstream

The fork only adds. Linux code lives in new projects and documents; upstream files change only where a new platform must be registered (`Directory.Build.props`, `Directory.Build.targets`, the platform tables in `AGENTS.md`, `docs/README.md`, `docs/harness/development.md`, and the tracked-document list in `.gitignore`). Every such change is listed in the pull request that makes it, so a later merge into `bkaankose/Wino-Mail` can take the Linux layer as it is. The repository's `AGENTS.md`, implementation rules, and verification scope apply unchanged.

## Principles

1. **Same place.** Folders on the left, list in the middle, reader on the right. Mode switcher at the sidebar foot. Settings groups, page names, and card order are identical to Windows.
2. **Native first.** C# on .NET with GTK 4 and libadwaita through the Gir.Core bindings. No Avalonia, Uno, MAUI, Electron, or XAML layer. The GNOME Human Interface Guidelines decide the chrome: split header bars, adaptive layouts, preference rows, toasts, and dialogs. Wino draws only what GTK lacks: mail rows, sidebar rows, avatars, unread pills, chips, the recipient token field, the info bar with severities, zone backdrops, and calendar surfaces.
3. **Wino's spirit.** The Windows app sets the layout, control set, commands and their order, icons, and themes. GNOME sets the look of each control. Pixel parity with Windows is not a goal; layout, naming, and command-order parity are.
4. **Shared backend.** Views bind through explicit C# adapters to the shared ViewModels. When a Linux screen needs a rule that exists only in another head, it moves to shared code first; never write another copy.

## Settled decisions

| Topic | Decision |
| --- | --- |
| Name and identity | Display name WinoLux Mail. Application ID `io.github.moaske.WinoLux` (D-Bus name, `.desktop` file, GSettings schema, icon names, Flatpak ID). It must not change after the first public release. Upstream branding replaces it only on a merge. |
| Baseline | Ubuntu 26.04 LTS (GNOME 50) on x64. GTK 4 and libadwaita from the GNOME 50 platform; WebKitGTK 6.0 (`webkitgtk-6.0`). `linux-arm64` follows once x64 is stable. |
| Bindings | `GirCore.Gtk-4.0`, `GirCore.Adw-1`, and `GirCore.WebKit-6.0` (0.8.x), centrally versioned in `Directory.Packages.props`. Gir.Core loads the system libraries at run time; builds need no `-dev` packages. |
| Runtime | CoreCLR, `net10.0`. The shared Release gate (Native AOT, trimming, warnings as errors) stays on for every shared library. The head tries to keep it too (instant startup, as on Windows); if Gir.Core blocks it, `build/Wino.Linux.targets` exempts only the GTK projects, as the Apple and Android heads are exempt. See L5. |
| Size classes | Wino's breakpoints in app-window pixels, through `AdwBreakpoint`. Large (1008 px and wider): sidebar, list, and reader side by side. Medium (641 to 1007 px): the sidebar becomes an overlay (`AdwOverlaySplitView` collapsed); list and reader stay side by side. Small (640 px and narrower): list and reader become a drill-in (`AdwNavigationSplitView` collapsed). |
| Modes | Mail, Calendar, Contacts, and To Do, plus Settings as the fifth segment, as on Windows and macOS. |
| Look | Stock libadwaita: system light or dark with a Light/Dark override, system accent colour, system font (Adwaita Sans on GNOME, Ubuntu Sans on Ubuntu), libadwaita type classes and radii. Without a backdrop theme, panes run edge to edge with GNOME's pane separators (macOS Revision 3). A Wino theme sets the accent and an optional wallpaper or gradient behind list and reader; then panes sit in rounded Wino zones with gutters (macOS Revision 2). No Mica or Acrylic: GNOME offers no wallpaper-tinted or blurred window material. |
| Icons | Every Wino concept glyph comes from the WinoIcons fonts. GTK stock symbolic icons only for window chrome: the sidebar toggle, back, the main menu, close, and the search toggle. Which WinoIcons set the Linux head uses is open (L1). |
| New mail | The Windows "New Mail" row at the top of the sidebar, plus Ctrl+N. In the small layout, a button in the list header bar. |
| Commands | GNOME has no unified toolbar. Each pane has its own header bar inside an `AdwToolbarView`. Sidebar header: sync button with the folder title and subtitle, then the main menu. List header: search toggle, Focused/Other, filter, selection mode. Reader header: Reply, Reply all, Forward · Archive, Delete, Move · Flag, Mark read, then More, in the Windows command order. Daily briefing, What's New, and the Wino account sit at the end of the sidebar header when their capability is on. |
| Sheets and dialogs | Prompts are `AdwAlertDialog` with consequence-led responses ("Delete permanently", destructive appearance). Forms are `AdwDialog` with the Windows field order. Move mail is a `GtkPopover` anchored to the Move button, with a filtered folder tree. Undo and short status are `AdwToast`s. |
| Settings | Its own `AdwWindow` opened with Ctrl+, or the Settings segment: an `AdwNavigationSplitView` with the Windows groups and order in the sidebar (Home, Manage Accounts, Wino Account, Wino Intelligence; General: General, Notifications, Personalization, Privacy & security, Keyboard Shortcuts, Backup and restore, About; Mail: Preferences, Message List, Unread badges, Reader & Composer, Signature and Encryption, Email Templates, Storage; Calendar, People, To Do). Cards render as libadwaita rows (see [Settings rows](#settings-rows)). Pages hidden by capability stay out of the sidebar. |
| Keyboard | The shared keyboard-shortcut definitions, with Ctrl where Windows uses Ctrl and the Mac uses Cmd. GNOME conventions win on conflict: Ctrl+F searches, Ctrl+, opens Settings, Ctrl+? shows the shortcuts dialog (`AdwShortcutsDialog`), F10 opens the main menu. |
| Storage | XDG directories: data in `$XDG_DATA_HOME/WinoLux`, configuration in `$XDG_CONFIG_HOME/WinoLux`, cache in `$XDG_CACHE_HOME/WinoLux`; the Flatpak sandbox maps them. |
| Secrets | Every credential and token is protected by the shared AES-GCM envelope with a key held in the Secret Service (`org.freedesktop.secrets`: GNOME Keyring, KWallet, KeePassXC) through libsecret. No plaintext fallback: without a Secret Service, account setup stops with an info bar that names the missing service. |
| Notifications | `GNotification` through `GApplication` (portal-aware), with the two configured shared mail actions and tap routing through `INotificationResponseService`. |
| Attachments | Open through `GtkFileLauncher` and save through `GtkFileDialog`; both use the desktop portals. Magika classification stays on: ONNX Runtime ships `linux-x64`. |
| Single instance | `GApplication` uniqueness on the session bus. A second launch activates the running window; `mailto:` links open compose in it. |
| Distribution | Flatpak on the GNOME 50 runtime as the release format; a `linux-x64` archive from GitHub Actions for testing. Flathub and a `.deb` come later. |

## Control mapping

| Screen | Widgets |
| --- | --- |
| Window | `AdwApplicationWindow` with an `AdwToastOverlay`; `AdwOverlaySplitView` (sidebar 260 px) around an `AdwNavigationSplitView` (list 380 px, reader fills); a trailing `AdwOverlaySplitView` (360 px) for the daily briefing |
| Sidebar | `GtkListView` with the `navigation-sidebar` style: account header rows (`WinoAccountIcon`, nickname, address in the tooltip), folder rows with special-folder glyphs and unread pills, "More folders", attention row, linked accounts. Mode switcher in the bottom bar of its `AdwToolbarView`: an `AdwToggleGroup` of five icon segments |
| Mail list | `GtkListView` with a `GtkSignalListItemFactory` over the shared projection; date groups as list section headers; the Wino mail row (avatar, sender, subject, preview, date, unread dot and accent subject, flag, attachment, category chips, thread disclosure with count); hover actions over the date; context menu through `GtkPopoverMenu`; multi-select through selection mode in the header bar |
| Reader | Header (subject, sender row with avatar, Reply group, More), body in a `WebKitWebView` over `Wino.Editor.Core`, attachment strip with Save all; empty state as an `AdwStatusPage` with keyboard hints; loading as a skeleton with "Downloading message from the server" |
| Compose | Opens in the reading pane as on Windows; "Open in new window" detaches it into its own `AdwWindow`. Recipients in the Wino token field (chips in a `GtkFlowBox` plus a `GtkText` with a completion popover), Cc and Bcc reveal, subject entry, editor toolbar with Format, Insert, and Options as an `AdwInlineViewSwitcher` above the `WebKitWebView` body. Send is the only `suggested-action` button |
| Search | `GtkSearchBar` under the list header bar with a `GtkSearchEntry`, Local/Online as an `AdwToggleGroup`, filter chips (account, folder, unread, has attachment, flagged), recent searches and contact suggestions in a popover; results replace the list |
| Info bars | Shell-wide single messages as `AdwBanner`. Page status with severity (informational, caution, critical, success) as the Wino-drawn `WinoInfoBar` in libadwaita colours; never a dialog for a status |
| Settings | See [Settings rows](#settings-rows) |
| Calendar | Date picker (`GtkCalendar`) and calendars in the sidebar, Day · Week · Work week · Month as an `AdwToggleGroup` in the header bar, Wino week and month surfaces over the shared layout calculator, event details as the reader pane |
| Contacts | Letter-indexed list, filters in the sidebar, contact card in the reader pane |
| To Do | Lists in the sidebar, task list with round Wino check rows, task details in the reader pane |
| Onboarding | The shared Windows flow: Welcome, provider, credentials (IMAP basic and advanced setup), setup progress, then the inbox, as an `AdwNavigationView` in a dialog |

## Settings rows

Settings pages keep describing Windows cards and expanders (`WinoSettingsCard`, `WinoSettingsExpander`). The GTK head renders them as libadwaita rows in `AdwPreferencesGroup`s, one group per Windows section:

| Windows control | GTK row |
| --- | --- |
| Card with a toggle | `AdwSwitchRow` |
| Card with a ComboBox, RadioButtons, or segmented choice | `AdwComboRow` |
| Card with a number box or stepper | `AdwSpinRow` |
| Card with a text box | `AdwEntryRow` (`AdwPasswordEntryRow` for secrets) |
| Clickable or navigation card | `AdwActionRow` with a chevron that pushes the sub-page |
| Card with a button | `AdwButtonRow` (destructive style for consequences) |
| Expander | `AdwExpanderRow` with its header control as a suffix; nested expanders push a sub-page instead |
| Read-only value | `AdwActionRow` with the value as a dimmed suffix label |

Header icons are WinoIcons glyphs as row prefixes. Descriptions become the row subtitle. Pages with bespoke content (theme gallery, filter editor, signature and template editors, Wino Account panels) keep that content in an `AdwPreferencesGroup` without rows.

## Modes and first-pass scope

| Mode | First pass | Later |
| --- | --- | --- |
| Mail | Full fidelity: shell, list, reader, compose, search, Move, narrow widths, notifications | Daily briefing, intelligence header, printing, S/MIME |
| Settings | Full fidelity for General, Personalization, Message List, Reader & Composer, Manage Accounts, About | Pages that depend on later capabilities |
| Onboarding | IMAP and SMTP accounts | Outlook and Gmail, once the fork has its own sign-in registrations (L3) |
| Calendar, Contacts, To Do | Layout frames on the Windows layouts | Full workflows |

## Capabilities

`PlatformCapabilities` for the Linux head, as first proposed:

| On | Off, with reason |
| --- | --- |
| `Printing` and `PdfExport` (through WebKitGTK, later in the first pass), `AdditionalWindows`, `Notifications`, `NotificationActions`, `StartupIntegration` (Background portal autostart), `GeneralActivation` (`mailto:` and files), `KeyboardShortcuts`, `FileBackup`, `PointerHover`, `MailListDensity`, `ConfigurableSyncIntervals`, `AttachmentSecurityCheck` | `Tray` (GNOME has no tray), `GlobalHotkeys` (Wayland has no global key grabs), `MicrosoftStore`, `AppleAppStore`, `Purchases`, `RemoteNotifications` (push relay is upstream's service), `WidgetsAndLiveTiles`, `AppModeTaskbarIdentity`, `PresentationDetection`, `BadgeLaunchNavigation`, `Smime` (deferred) |

Wino Account, Wino Intelligence cloud features, and the daily briefing rely on upstream's API and entitlements. They stay hidden in WinoLux until the upstream owner agrees otherwise (L4).

## Background model

| Part | Decision |
| --- | --- |
| Foreground | The shared runtime starts with the first window and runs as on desktop: IMAP IDLE, sync loops, reminders. |
| Closing the window | Follows the shared "when you close it" setting in General. Keeping it running needs the XDG Background portal permission, requested the first time the user chooses it; the app then holds the `GApplication` and has no window. |
| Autostart | Through the Background portal (`StartupIntegration`), never by writing to `~/.config/autostart` from inside the sandbox. |

## Deferred

S/MIME, the custom theme editor, Companion, push-triggered sync, the daily briefing, widgets, and Flathub publication.

## Open for the owner's review

| # | Item | Recommendation |
| --- | --- | --- |
| L1 | WinoIcons set for Linux: reuse Material Rounded (Apache-2.0, already built for Android) or add a GNOME-style symbolic set | Material Rounded for the first pass: its outline style sits close to GNOME's symbolic icons and costs nothing. Revisit once the shell runs. |
| L2 | Settings as its own window or as a mode inside the main window | Own window, as on macOS and in GNOME apps; the Settings segment opens it |
| L3 | Outlook and Gmail sign-in registrations | Register WinoLux's own Microsoft Entra app and Google desktop client. The repository rules forbid repurposing upstream's clients, and a fork that ships builds must not use them |
| L4 | Wino Account, intelligence cloud features, daily briefing | Hidden until the upstream owner agrees |
| L5 | Native AOT for the GTK head | Try it in milestone 1; exempt the GTK projects only if Gir.Core requires it |
| L6 | Release format | Flatpak first, then Flathub; a `.deb` only on demand |

## Project map

| Project | Target | Role |
| --- | --- | --- |
| `src/Wino.Mail.Linux` | `net10.0` (Exe) | Head: `Program`, the `AdwApplication`, `LinuxComposition`, navigation over the shared `NavigationCoordinator`, dialog service, views mirroring the Windows names and feature folders (`Views/Settings/AboutPage.xaml` maps to `Views/Settings/AboutPage.cs`, bound to the real `AboutPageViewModel`) |
| `src/Wino.Platform.Linux` | `net10.0` | XDG paths, Secret Service key provider and secure item store, GLib dispatcher, launcher, clipboard, notifications, reminder scheduler, Background portal, reader runtime, presence, authentication hosts; no presentation references |
| `controls/Wino.Presentation.Gtk` | `net10.0` | GTK twin of `Wino.Presentation.AppKit` and `.Android`: units, WinoIcons, colours as libadwaita CSS, theme surfaces, zones, backdrops, typed bindings |
| `controls/Wino.Mail.Controls.Gtk` | `net10.0` | Mail and group rows, sidebar rows, avatars, pills, chips, info bar, token field, settings row renderer, calendar surfaces, contact and task rows |
| `controls/Wino.Editor.Gtk` | `net10.0` | `WebKitWebView` reader and editor sessions over `Wino.Editor.Core`: assets through a registered URI scheme, a script message handler bridge, remote-content policy on resource loads |
| `build/Wino.Linux.targets` | | Linux build policy, imported when `WinoTargetPlatform` is `Linux` |
| `WinoMail.Linux.slnx` | | Two folders, `/Linux/` and `/Shared/`, as the Android solution |

`Wino.Platform.Linux` never references presentation projects. Control logic that another head also needs goes to `controls/Wino.Mail.Controls.Core` or `Wino.Presentation.Core`.

## Verification

A compiled view does not establish parity. For each canvas board, manual acceptance on Ubuntu 26.04 compares pane proportions, density, row content, command order, and selected states against Windows, then checks light and dark, a backdrop theme, the three size classes, keyboard traversal, text scaling, and the Orca screen reader. See [Linux development](harness/development-linux.md#verification).
