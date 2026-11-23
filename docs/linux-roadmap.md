# WinoLux roadmap

> **Stopped.** WinoLux ended after milestone 2e because Wino Mail now has an official Linux app; see the [README](../README.md). The milestones below that are not done will not be built.

The milestones of the Linux port (WinoLux Mail fork). Each milestone is one pull request that Marco tests on Ubuntu and merges. Finished milestones link to their pull request. The order of the milestones that have not started can still change. The scope of each mode follows [Modes and first-pass scope](linux-design-decisions.md#modes-and-first-pass-scope) in the Linux design decisions.

## Done

| Milestone | What it gives | Pull request |
| --- | --- | --- |
| 0 | The plan: Linux design decisions and the development guide | [#1](https://github.com/Moaske/WinoLux-Mail/pull/1) |
| 1 | Linux as a build platform: the GTK head builds, packages and starts in CI, with a Native AOT build | [#2](https://github.com/Moaske/WinoLux-Mail/pull/2) |
| 2a | The shared runtime on Linux: data in the XDG folders, the protection key in the GNOME keyring, the shared startup and shutdown order | [#3](https://github.com/Moaske/WinoLux-Mail/pull/3) |
| 2b | Adding an IMAP account: Welcome, provider wizard, sign-in and server settings, setup progress | [#4](https://github.com/Moaske/WinoLux-Mail/pull/4) |
| 2c | The three-pane window: sidebar with accounts and folders, mode switcher, Wino's icon font, three window sizes | [#5](https://github.com/Moaske/WinoLux-Mail/pull/5) |
| 2d | The message list: sender picture or initials, sender, subject, preview and date; unread, flag and attachment marks; date groups; threads; selecting messages | [#7](https://github.com/Moaske/WinoLux-Mail/pull/7), [#8](https://github.com/Moaske/WinoLux-Mail/pull/8), [#9](https://github.com/Moaske/WinoLux-Mail/pull/9) |
| 2e | The reading pane: the selected message through WebKitGTK, with its attachments and the remote-content bar | [#10](https://github.com/Moaske/WinoLux-Mail/pull/10) |

## Mail, first pass

| Milestone | What it gives | Status |
| --- | --- | --- |
| 2f | Working with mail: read and unread, flag, delete, archive, Move, the context menu and keyboard shortcuts | Stopped; [#11](https://github.com/Moaske/WinoLux-Mail/pull/11) closed unmerged |
| 2g | Writing mail: new mail, reply and forward in the WebKitGTK editor, drafts, attachments, sending | Planned |
| 2h | Search | Planned |

## After the mail first pass

| Milestone | What it gives | Status |
| --- | --- | --- |
| 3 | Settings: General, Personalization, Message List, Reader & Composer, Manage Accounts (including editing server settings) and About | Planned |
| 4 | Desktop integration: notifications, starting in the background, opening `mailto:` links, printing and PDF | Planned |
| 5 | Installing: a desktop entry and app icon, and a package for Ubuntu (Flatpak or .deb, to be decided) | Planned |
| 6 | Calendar, Contacts and To Do: their layout frames on the Windows layouts | Planned |
| 7 | Outlook and Gmail accounts, once WinoLux has its own sign-in registrations (L3) | Planned |
