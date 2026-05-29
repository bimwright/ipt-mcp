using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class CodeTools
{
    private readonly PluginClient _client;
    public CodeTools(PluginClient client) => _client = client;
}
