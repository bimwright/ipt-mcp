using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class FeatureTools
{
    private readonly PluginClient _client;
    public FeatureTools(PluginClient client) => _client = client;
}
