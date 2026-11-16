using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Host busy (family contract FC-BUSY-01..05). When Inventor cannot start a command because a modal
/// dialog is waiting for the user, the STA queue is full, or the STA stayed busy past the deadline,
/// the call returns <c>HOST_BUSY</c> and says the command was not started; that command never runs
/// later (the queue drops cancelled work). A command that started and then ran past its deadline
/// stays <c>TIMEOUT</c> with an unknown outcome. Host-free so the decisions are unit-tested.
/// </summary>
public static class HostBusyPolicy
{
    public const string ModalDialog = "modal_dialog";
    public const string StaBusy = "sta_busy";
    public const string QueueFull = "queue_full";

    private const string NotStarted = " The command was not started and will not run later; it is safe to send it again once Inventor is free.";

    /// <summary>
    /// A modal dialog refuses every command except health (which answers from counters).
    /// Checked before queueing and again on the STA, because a dialog's message loop can run queued work.
    /// </summary>
    public static bool RefusesForModal(string? command, bool modalOpen) => modalOpen && command != "health";

    public static string ModalMessage(string? command, string? dialogTitle)
        => $"{command} refused: a modal dialog is open in Inventor"
           + (string.IsNullOrEmpty(dialogTitle) ? "" : $" (\"{dialogTitle}\")")
           + " and is waiting for the user." + NotStarted
           + " Ask the user to answer or close the dialog, then check inventor_health (host_busy=false) before retrying.";

    public static string QueueFullMessage(string? command)
        => $"{command} refused: Inventor's command queue is full." + NotStarted
           + " Check inventor_health (pending_commands) before retrying.";

    public static string ExpiredMessage(string? command, int timeoutMs)
        => $"{command} refused: Inventor stayed busy for {timeoutMs} ms with earlier work, so the command expired in the queue." + NotStarted
           + " Check inventor_health (sta_busy, modal_dialog) before retrying.";

    /// <summary>Appended to TIMEOUT for a command that started: its outcome is unknown (FC-BUSY-04).</summary>
    public const string StartedTimeoutNote =
        " The command had started, so its outcome is unknown: inspect the document before retrying a write.";

    /// <summary>Health fields (FC-BUSY-05): one flag and the reason, from the <c>modal_dialog</c> probe health already carries.</summary>
    public static JObject WithHostBusy(this JObject data, bool staBusy)
    {
        var modalOpen = data["modal_dialog"]?["open"]?.Type == JTokenType.Boolean && (bool)data["modal_dialog"]!["open"]!;
        data["host_busy"] = staBusy || modalOpen;
        data["busy_reason"] = modalOpen ? ModalDialog : staBusy ? StaBusy : null;
        return data;
    }
}
