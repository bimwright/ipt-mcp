using Bimwright.Ipt.Server;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Tests;

/// <summary>Fake add-in targets that support document_target_v1 and a gateway that already confirmed a document.</summary>
internal static class DocumentTargetFixture
{
    internal const string DocumentId = "doc-fixture";

    internal static string[] Capabilities => new[] { DocumentTargetPolicy.Protocol };

    /// <summary>Binds the only live target and pins a document, as inventor_switch_target(verify=true) does.</summary>
    internal static PluginClient Confirmed(this PluginClient client)
    {
        var target = client.ResolveTarget();
        Assert.True(client.PinDocument(target, DocumentId, "Fixture.ipt"));
        return client;
    }
}
