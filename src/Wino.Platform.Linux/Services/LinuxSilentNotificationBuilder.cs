using Wino.Core.Domain.Entities.Calendar;
using Wino.Core.Domain.Entities.Mail;
using Wino.Core.Domain.Entities.Shared;
using Wino.Core.Domain.Interfaces;

namespace Wino.Platform.Linux.Services;

/// <summary>
/// Until desktop notifications (GNotification) are built, the Notifications capability is off and nothing is announced:
/// new mail, reminders and attention prompts only appear inside the app. GNOME has no taskbar badge or jump list.
/// </summary>
public sealed class LinuxSilentNotificationBuilder : INotificationBuilder
{
    public Task CreateNotificationsAsync(IEnumerable<MailCopy> newMailItems) => Task.CompletedTask;
    public Task CreateTestNotificationsAsync(IEnumerable<MailCopy> mailItems) => Task.CompletedTask;
    public Task UpdateTaskbarIconBadgeAsync() => Task.CompletedTask;
    public Task UpdateJumpListOptionsAsync() => Task.CompletedTask;
    public Task AddCalendarTaskbarBadgeCountAsync(int newlyDownloadedCount) => Task.CompletedTask;
    public Task ClearCalendarTaskbarBadgeAsync() => Task.CompletedTask;
    public void RemoveNotification(Guid mailUniqueId) { }
    public void CreateAttentionRequiredNotification(MailAccount account) { }
    public void CreateWebView2RuntimeMissingNotification() { }
    public Task CreateCalendarReminderNotificationAsync(CalendarItem calendarItem, long reminderDurationInSeconds) => Task.CompletedTask;
    public Task CreateTestCalendarReminderNotificationAsync(CalendarItem calendarItem) => Task.CompletedTask;
    public Task CreateTestPeopleNotificationAsync(AccountContact contact) => Task.CompletedTask;
    public Task CreateTestTaskReminderNotificationAsync(AccountTask task) => Task.CompletedTask;
}
