using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class PropertyTools
{
    private readonly PluginClient _client;
    public PropertyTools(PluginClient client) => _client = client;
}
