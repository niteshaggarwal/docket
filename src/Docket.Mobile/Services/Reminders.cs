using Docket.Core;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace Docket.Mobile.Services;

/// <summary>
/// Turns Vikunja reminders into Android notifications. Every refresh reschedules the reminders of the
/// next 14 days, so changes made on the PC or by Claude reach the phone the next time Docket opens.
/// </summary>
public static class Reminders
{
    public static async Task<bool> EnsurePermissionAsync()
    {
        if (await LocalNotificationCenter.Current.AreNotificationsEnabled()) return true;
        return await LocalNotificationCenter.Current.RequestNotificationPermission();
    }

    public static async Task RescheduleAsync(DocketService svc)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var due = await svc.DueRemindersAsync(now, now.AddDays(14));
            LocalNotificationCenter.Current.CancelAll();
            foreach (var (task, at) in due.Take(60))
            {
                var t = task.Task;
                await LocalNotificationCenter.Current.Show(new NotificationRequest
                {
                    NotificationId = unchecked((int)(t.Id * 31 + at.ToUnixTimeSeconds() % 100000)),
                    Title = t.Title,
                    Description = t.Due is { } d ? $"{task.Project} · due {d.ToLocalTime():ddd d MMM, HH:mm}" : task.Project,
                    ReturningData = t.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Schedule = new NotificationRequestSchedule { NotifyTime = at.LocalDateTime },
                });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException) { /* try again next refresh */ }
    }
}
