using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class SketchTools
{
    private readonly PluginClient _client;
    public SketchTools(PluginClient client) => _client = client;
}
