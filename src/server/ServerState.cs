namespace Bimwright.Ipt.Server;

public sealed class ServerState
{
    public ServerState(InventorMcpConfig config) => Config = config;
    public InventorMcpConfig Config { get; }
    public bool ReadOnly => Config.ReadOnly;
}
