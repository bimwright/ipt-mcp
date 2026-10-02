using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>
/// F4-P1 batch iteration logic, factored out of <c>BatchExecuteHandler</c> so it can be
/// unit-tested without a live Inventor document or transaction. The handler owns the
/// <c>TransactionManager.StartTransaction</c> lifecycle and decides End/Abort from
/// <see cref="Outcome.AnyFailed"/>. Ported from rvt-mcp's BatchExecutor (spec: "mirror rvt").
/// </summary>
public static class BatchExecutor
{
    public const int MaxCommands = 20;

    /// <summary>
    /// Commands that must never run inside a batch. OrdinalIgnoreCase: the command registry
    /// compares case-insensitively, so a case-sensitive list here would be bypassable.
    /// </summary>
    public static readonly IReadOnlyCollection<string> BlockedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "batch_execute",   // nesting would re-enter the transaction wrapper
        "send_code",       // escape hatch — its own gate, timeout path, and audit trail
        "run_baked_tool",  // ToolBaker runtime — its own dispatch authorizer
        "apply_bake",      // ToolBaker registration — mutates the tool cache, not the model
        // Document lifecycle: every sub-handler resolves app.ActiveDocument fresh, so these
        // would silently move later steps onto a different document than the transaction's
        // (rolled_back would lie) or fail End/Abort by invalidating the current transaction.
        "new_part", "new_assembly", "open_document", "close_document", "save_document",
        "derive_envelope",  // also creates + activates a new document mid-transaction
        "create_part",      // creates, saves and may close a new part
        "save_all", "open_documents", "close_documents",
        "get_drawing_info", "new_drawing", "add_sheet", "set_title_block", "add_drawing_view",
        "add_section_view", "edit_drawing_view", "add_drawing_dimension", "add_balloon",
        "export_drawing", "capture_sheet",
        "add_drawing_note", "add_drawing_table",
        "add_drawing_symbol", "edit_drawing_annotation",
        "delete_drawing_items", "edit_drawing_table", "set_drawing_styles", "edit_sheet",
    };

    public sealed class Outcome
    {
        public JArray Results { get; } = new();
        public bool AnyFailed { get; set; }
    }

    public sealed class StepResult
    {
        public bool Ok { get; init; }
        public JToken? Data { get; init; }
        public string? Error { get; init; }

        public static StepResult Success(JToken? data) => new() { Ok = true, Data = data };
        public static StepResult Failure(string error) => new() { Error = error };
    }

    public static string TooManyCommandsMessage(int count)
        => $"batch_execute supports at most {MaxCommands} commands (got {count}). Split the batch.";

    public static string BlockedCommandMessage(string name)
        => $"'{name}' cannot run inside batch_execute — call it directly.";

    /// <param name="commands">The <c>commands</c> JArray: items are {command, params}.</param>
    /// <param name="continueOnError">If false, stops at the first failure.</param>
    /// <param name="invoke">Runs one already-validated sub-command; wraps the real dispatcher in the handler.</param>
    public static Outcome Run(JArray commands, bool continueOnError, Func<string, JObject, StepResult> invoke)
    {
        if (commands is null) throw new ArgumentNullException(nameof(commands));   // net48: no ThrowIfNull
        if (invoke is null) throw new ArgumentNullException(nameof(invoke));

        var outcome = new Outcome();

        if (commands.Count > MaxCommands)
        {
            outcome.AnyFailed = true;
            outcome.Results.Add(new JObject
            {
                ["index"] = -1,
                ["ok"] = false,
                ["error"] = TooManyCommandsMessage(commands.Count),
            });
            return outcome;
        }

        for (var i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i] as JObject;
            var cmdName = (string?)cmd?["command"];

            if (string.IsNullOrWhiteSpace(cmdName))
            {
                if (!AddFailure(outcome, i, "missing 'command' field", continueOnError)) return outcome;
                continue;
            }

            if (BlockedCommands.Contains(cmdName))
            {
                if (!AddFailure(outcome, i, BlockedCommandMessage(cmdName), continueOnError)) return outcome;
                continue;
            }

            var paramsToken = cmd!["params"];
            if (paramsToken is not null && paramsToken.Type != JTokenType.Null && paramsToken is not JObject)
            {
                if (!AddFailure(outcome, i, "'params' must be an object", continueOnError)) return outcome;
                continue;
            }
            var subParams = paramsToken as JObject ?? new JObject();
            StepResult r;
            try
            {
                r = invoke(cmdName!, subParams);
            }
            catch (Exception ex)
            {
                if (!AddFailure(outcome, i, ex.Message, continueOnError)) return outcome;
                continue;
            }

            if (r.Ok)
            {
                outcome.Results.Add(new JObject
                {
                    ["index"] = i,
                    ["ok"] = true,
                    ["data"] = r.Data ?? JValue.CreateNull(),
                });
            }
            else if (!AddFailure(outcome, i, r.Error ?? "unknown error", continueOnError))
            {
                return outcome;
            }
        }

        return outcome;
    }

    private static bool AddFailure(Outcome outcome, int index, string error, bool continueOnError)
    {
        outcome.Results.Add(new JObject
        {
            ["index"] = index,
            ["ok"] = false,
            ["error"] = error,
        });
        outcome.AnyFailed = true;
        return continueOnError;
    }
}
