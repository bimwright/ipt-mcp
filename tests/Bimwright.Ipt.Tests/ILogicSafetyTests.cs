using Bimwright.Ipt.Shared.Contracts;
using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class ILogicSafetyTests
{
    private sealed class NeverRun : IInventorCommand
    {
        public string Name => "ilogic_upsert_rule";
        public bool IsReadOnly => false;
        public InventorCommandResult Execute(InventorCommandContext ctx, JObject p)
            => throw new InvalidOperationException("The source gate must run before the handler.");
    }

    [Theory]
    [InlineData("ilogic_upsert_rule")]
    [InlineData("ILOGIC_UPSERT_RULE")]
    public void Upsert_respects_the_plugin_source_kill_switch(string command)
    {
        var dispatcher = new CommandDispatcher(new Dictionary<string, IInventorCommand>(StringComparer.OrdinalIgnoreCase)
        {
            ["ilogic_upsert_rule"] = new NeverRun(),
        }, 1000000);
        var result = dispatcher.Dispatch(new InventorCommandContext { EnableSendCode = false },
            new InventorCommandEnvelope { Command = command });

        Assert.False(result.Ok);
        Assert.Equal(InventorErrorCodes.SEND_CODE_DISABLED, result.Error!.Code);
    }

    [Theory]
    [InlineData("ilogic_run_rule")]
    [InlineData("ilogic_run_external_rule")]
    [InlineData("ilogic_upsert_rule")]
    [InlineData("ilogic_run_snippet")]
    [InlineData("run_command")]
    [InlineData("undo")]
    public void Arbitrary_rule_and_command_execution_is_rejected_before_batch_invocation(string command)
    {
        foreach (var name in new[] { command, command.ToUpperInvariant() })
        {
            var invoked = 0;
            var result = BatchExecutor.Run(new JArray(new JObject { ["command"] = name }), false, (_, _) =>
            {
                invoked++;
                return BatchExecutor.StepResult.Success(null);
            });
            Assert.True(result.AnyFailed);
            Assert.Equal(0, invoked);
        }
    }
}
