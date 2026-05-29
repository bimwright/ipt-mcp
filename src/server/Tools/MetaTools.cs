// BOOTSTRAP PLACEHOLDER — replaced in Phase 1 by WS1-C
using ModelContextProtocol.Server;

namespace Bimwright.Inventor.Server.Tools;

[McpServerToolType]
public sealed class MetaTools
{
    private readonly PluginClient _client;
    public MetaTools(PluginClient client) => _client = client;
}
