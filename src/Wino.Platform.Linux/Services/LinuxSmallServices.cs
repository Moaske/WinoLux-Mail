using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;

namespace Wino.Platform.Linux.Services;

/// <summary>GNOME shortcuts use Ctrl. Modifier state arrives with GTK key events, not as global state, so Shift reads as not pressed.</summary>
public sealed class LinuxShortcutPlatformService : IShortcutPlatformService
{
    public ModifierKeys PrimaryCommandModifier => ModifierKeys.Control;
    public bool IsShiftKeyPressed() => false;
}

/// <summary>
/// The desktop does not tell an app about other apps' presentation state, so "presenting" is never detected. GNOME's
/// Do Not Disturb hides notification banners itself, so the app does not need to suppress them.
/// </summary>
public sealed class LinuxUserPresenceStateProvider : IUserPresenceStateProvider
{
    public bool IsPresenting() => false;
    public bool IsSystemQuietTimeActive() => false;
}

/// <summary>GTK 4 has no event sound API, so completing a task is silent until the head plays a sound through GStreamer.</summary>
public sealed class LinuxTaskCompletionSound : ITaskCompletionSound
{
    public void Play() { }
}
