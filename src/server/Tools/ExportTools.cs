using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class ExportTools
{
    private readonly PluginClient _client;
    public ExportTools(PluginClient client) => _client = client;
}
