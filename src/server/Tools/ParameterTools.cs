using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class ParameterTools
{
    private readonly PluginClient _client;
    public ParameterTools(PluginClient client) => _client = client;
}
