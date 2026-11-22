#nullable enable
using System;
using System.Globalization;

namespace Bimwright.Ipt.Shared.Views.Toast;

/// <summary>
/// "Pause for today": activity toasts stay silent in every Inventor on this machine until the next local midnight.
/// The pause is the local date saved in iptmcp.config.json (toastSnoozedDate), read on each check, so a pause
/// set in one Inventor applies to the others without a timer.
/// </summary>
internal static class ToastSnooze
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Raised after this process paused or resumed toasts (Status refreshes its switch).</summary>
    public static event Action? Changed;

    /// <summary>Test hook for the local clock.</summary>
    internal static Func<DateTime> Now { get; set; } = () => DateTime.Now;

    public static bool IsActive(string? configPath) =>
        !string.IsNullOrWhiteSpace(configPath) && IsActive(ToastConfigStore.LoadToastSnoozedDate(configPath), Now());

    public static bool IsActive(string? snoozedDate, DateTime now) =>
        DateTime.TryParseExact(snoozedDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && date.Date == now.Date;

    /// <summary>Pauses toasts for the rest of today. Returns whether the choice was saved.</summary>
    public static bool PauseForToday(string? configPath) =>
        Save(configPath, Now().ToString(DateFormat, CultureInfo.InvariantCulture));

    /// <summary>Ends a pause early. Returns whether the choice was saved.</summary>
    public static bool Resume(string? configPath) => Save(configPath, null);

    /// <summary>
    /// Turning toasts on means showing them now, so an active pause ends too. Returns false only when a pause
    /// was active and its removal could not be saved.
    /// </summary>
    public static bool ResumeIfPaused(string? configPath) =>
        !IsActive(configPath) || Resume(configPath);

    private static bool Save(string? configPath, string? date)
    {
        if (string.IsNullOrWhiteSpace(configPath)) return false;
        var saved = ToastConfigStore.SaveToastSnoozedDate(configPath, date);
        try { Changed?.Invoke(); } catch { }
        return saved;
    }
}
