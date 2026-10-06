#nullable disable
using System;

namespace Bimwright.Ipt.Shared.Views.Toast
{
    // ipt-mcp additions to the card state machine shared with rvt-mcp. Kept out of
    // ActivityAggregator.cs so that file stays a straight copy of the rvt one.
    public sealed partial class ActivityAggregator
    {
        /// <summary>Lifetime of an explicit agent report (<c>report_task_result</c>) card.</summary>
        public const int TaskResultSeconds = 8;

        // Card ids are never reused, so this marks the current card as an agent report without
        // touching StartCard.
        private long _resultCardId;

        /// <summary>
        /// Feeds one finished command into the card. An explicit agent report replaces the slot
        /// (it is not a tool count and is never inferred from activity); anything else is counted.
        /// Returns whether the caller must post a render.
        /// </summary>
        public bool Record(ToastModel model, bool frameUsable)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.TaskId != null)
            {
                var body = "Agent reported · " + model.Summary + " · " + model.TaskId;
                return ShowResult(model.Title, body, model.Success, TaskResultSeconds, frameUsable);
            }

            var vm = new McpToastViewModel { Title = model.Title, Summary = model.Summary, Detail = model.Detail };
            return RecordResult(model.Title, vm.Body, model.Success, model.ThumbnailPath, frameUsable,
                isCapture: model.Command == "capture_view" || model.Command == "capture_sheet", durationMs: model.DurationMs);
        }

        /// <summary>
        /// An explicit agent report. Unlike <see cref="ShowStatus"/> it replaces an open activity card:
        /// the agent states the final outcome of the whole job, so it must not be refused by the counters
        /// of the tools that led up to it. Shown as a status card (no counters); red when the report is
        /// a failure. Parked while the frame is unusable.
        /// </summary>
        public bool ShowResult(string title, string body, bool success, int seconds, bool frameUsable)
        {
            lock (_gate)
            {
                StartCard(isStatus: true, frameUsable ? Phase.Visible : Phase.Pending);
                _resultCardId = _cardId;
                _statusSeconds = seconds > 0 ? seconds : DefaultIdleSeconds;
                _title = title;
                _body = body;
                _latestSuccess = success;
                _hasFailure = !success;
                if (_phase == Phase.Visible)
                    Rearm();
                return RequestRender();
            }
        }

        /// <summary>
        /// Like <see cref="ShowStatus"/>, which never covers an open activity card, and also never
        /// covers an open agent report: a report is the answer the user is waiting for.
        /// </summary>
        public bool ShowConnectionStatus(string title, string body, int seconds)
        {
            lock (_gate)
            {
                var reportOpen = _cardId == _resultCardId && (_phase == Phase.Visible || _phase == Phase.Pending);
                return !reportOpen && ShowStatus(title, body, seconds);
            }
        }

        /// <summary>
        /// <see cref="RecordResult(string, string, bool, string, bool)"/> for a capture that has no
        /// displayable file (inline <c>capture_view</c>): still counted under Capture, never a thumbnail.
        /// </summary>
        public bool RecordResult(string title, string body, bool success, string imagePath, bool frameUsable,
            bool isCapture, long? durationMs = null)
        {
            lock (_gate)
            {
                var render = RecordResult(title, body, success, imagePath, frameUsable, durationMs);
                if (success && isCapture && string.IsNullOrEmpty(imagePath))
                    _images++;
                return render;
            }
        }
    }
}
