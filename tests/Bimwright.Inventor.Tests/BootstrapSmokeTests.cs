// BOOTSTRAP PLACEHOLDER — expanded in Phase 1 by WS1-C (full registration/read-only/discovery suite)
using Bimwright.Inventor.Server;
using Bimwright.Inventor.Server.Tools;

namespace Bimwright.Inventor.Tests;

public class BootstrapSmokeTests
{
    [Fact]
    public void Default_registration_includes_meta_and_excludes_code()
    {
        var cfg = InventorMcpConfig.Load(System.Array.Empty<string>());
        var types = Program.ResolveToolTypesForRegistration(cfg);

        Assert.Contains(typeof(MetaTools), types);
        Assert.DoesNotContain(typeof(CodeTools), types);   // code off by default
        Assert.Contains(typeof(ToolBakerTools), types);
    }

    [Fact]
    public void DocumentTools_registered_once_despite_query_and_document_toolsets()
    {
        var cfg = InventorMcpConfig.Load(System.Array.Empty<string>());
        var types = Program.ResolveToolTypesForRegistration(cfg);

        Assert.Single(types, t => t == typeof(DocumentTools));
    }
}
