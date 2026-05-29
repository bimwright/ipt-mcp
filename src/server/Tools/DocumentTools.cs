using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class DocumentTools
{
    private readonly PluginClient _client;
    public DocumentTools(PluginClient client) => _client = client;
}
