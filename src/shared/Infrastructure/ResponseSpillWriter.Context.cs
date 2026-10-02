namespace Bimwright.Ipt.Shared.Infrastructure;

internal static class ResponseSpillWriterFactory
{
    public static ResponseSpillWriter ForContext(InventorCommandContext ctx)
        => new(ResponseSpillWriter.DefaultDirectory, ctx.SpillRetentionHours);
}
