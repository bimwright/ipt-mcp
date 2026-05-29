using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class ToolBakerWriteTools
{
    private readonly PluginClient _client;
    public ToolBakerWriteTools(PluginClient client) => _client = client;
}
