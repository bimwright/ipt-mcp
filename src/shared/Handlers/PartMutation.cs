#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Infrastructure;

namespace Bimwright.Ipt.Shared.Handlers;

/// <summary>Own only this operation's transaction, including when nested in batch_execute.</summary>
internal static class PartMutation
{
    internal static InventorCommandResult Run(InventorCommandContext ctx, Application app,
        PartDocument part, string name, Func<InventorCommandResult> apply)
    {
        Transaction? transaction = null;
        try
        {
            transaction = app.TransactionManager.StartTransaction((global::Inventor._Document)(object)part, "MCP: " + name);
            var result = apply(); // Includes recompute, health and response readback before commit.
            if (!result.Ok) throw new InvalidOperationException("Mutation callback must throw on failure");
            transaction.End();
            transaction = null;
            return result;
        }
        catch (Exception ex)
        {
            var rolledBack = false;
            string? rollbackError = null;
            if (transaction is not null)
            {
                try { transaction.Abort(); rolledBack = true; }
                catch (Exception rollback) { rollbackError = rollback.Message; }
            }
            var result = HandlerBase.FailForSupport(ctx,
                rollbackError is null && ex is ArgumentException ? InventorErrorCodes.INVALID_ARGUMENT : InventorErrorCodes.API_ERROR,
                rollbackError is null ? ex.Message : ex.Message + "; transaction rollback failed: " + rollbackError);
            result.Data = new JObject
            {
                ["rolled_back"] = rolledBack,
                ["mutation_applied"] = rollbackError is null ? new JValue(false) : new JValue("unknown"),
                ["rollback_failed"] = rollbackError is not null,
            };
            return result;
        }
    }

    internal static double Evaluate(global::Inventor.Parameters parameters, string expression, UnitsTypeEnum units, string field)
    {
        try
        {
            var value = Convert.ToDouble(parameters.GetValueFromExpression(expression, units));
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("non-finite result");
            return value;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"{field} expression '{expression}' cannot be evaluated with the expected units: {ex.Message}", ex);
        }
    }
}
#endif
