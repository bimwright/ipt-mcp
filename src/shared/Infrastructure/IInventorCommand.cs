namespace Bimwright.Inventor.Shared.Infrastructure;

using Newtonsoft.Json.Linq;
using Bimwright.Inventor.Shared.Contracts;

/// <summary>
/// A single dispatchable add-in command. Implemented by every handler in
/// <c>shared/Handlers/**</c>. Mirrors nwd's <c>INwdCommand</c>.
/// </summary>
public interface IInventorCommand
{
    /// <summary>Wire command name (snake_case, unprefixed), e.g. <c>extrude</c>.</summary>
    string Name { get; }

    /// <summary>True if the command performs no model mutation (allowed in read-only mode).</summary>
    bool IsReadOnly { get; }

    /// <summary>Executes the command on Inventor's STA thread and returns a DTO envelope.</summary>
    InventorCommandResult Execute(InventorCommandContext context, JObject parameters);
}
