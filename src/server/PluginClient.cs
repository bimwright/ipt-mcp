// BOOTSTRAP PLACEHOLDER — replaced in Phase 1 by WS1-B
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Inventor.Shared.Contracts;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Server;

public sealed class InventorGatewayException : Exception
{
    public string Code { get; }
    public InventorGatewayException(string code, string message) : base(message) => Code = code;
}

public sealed class PluginClient
{
    private readonly InventorMcpConfig _config;

    public PluginClient(InventorMcpConfig config) => _config = config;

    public IReadOnlyList<TargetDescriptor> ListTargets() => Array.Empty<TargetDescriptor>();

    public TargetDescriptor? CurrentTarget => null;

    public bool SwitchTarget(string targetId) => false;

    public Task<JToken> SendAsync(string command, object parameters, CancellationToken ct)
        => throw new NotImplementedException("PluginClient.SendAsync is implemented in Phase 1 WS1-B.");
}
