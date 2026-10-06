namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// Marker for commands that touch no Inventor API and therefore never need the STA thread. The
/// add-in dispatches them on the transport listener thread, so they still answer while the STA is
/// jammed behind a timed-out <c>send_code</c> script. Only mark a handler when its
/// <see cref="IInventorCommand.Execute"/> provably ignores <c>InventorCommandContext.Application</c>.
/// </summary>
public interface IStaIndependentCommand : IInventorCommand
{
}
