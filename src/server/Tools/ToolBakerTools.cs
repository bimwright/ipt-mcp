using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class ToolBakerTools
{
    private readonly PluginClient _client;
    public ToolBakerTools(PluginClient client) => _client = client;
}
