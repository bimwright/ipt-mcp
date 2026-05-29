// These tests exercise WS1-B's CommandDispatcher / IInventorCommand / InventorCommandContext,
// which live in src/shared/Infrastructure (globbed into this test assembly). While WS1-B is
// still in flight those files may be absent; the HAS_INVENTOR_DISPATCHER constant (defined in
// the .csproj only once CommandDispatcher.cs exists) keeps this file compiling to nothing so
// the rest of the suite stays green. It activates automatically at integration.
#if HAS_INVENTOR_DISPATCHER
using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Inventor.Shared.Contracts;
using Bimwright.Inventor.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Inventor.Tests;

/// <summary>
/// CommandDispatcher behavior with a fake in-memory IInventorCommand:
///   unknown command → INVALID_ARGUMENT;
///   write command while read-only → READ_ONLY (handler never runs);
///   send_code without EnableSendCode → SEND_CODE_DISABLED;
///   handler exception → sanitized API_ERROR;
///   successful response preserves the envelope id.
/// </summary>
public sealed class DispatcherTests
{
    private sealed class FakeCmd : IInventorCommand
    {
        public string Name { get; init; } = "";
        public bool IsReadOnly { get; init; }
        public Func<InventorCommandResult>? Body { get; init; }
        public InventorCommandResult Execute(InventorCommandContext ctx, JObject p) => Body!();
    }

    private static CommandDispatcher Make(params IInventorCommand[] cmds)
        => new(cmds.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase), 10 * 1024 * 1024);

    [Fact]
    public void UnknownCommandIsInvalidArgument()
    {
        var d = Make();
        var r = d.Dispatch(new InventorCommandContext(), new InventorCommandEnvelope { Command = "nope" });
        Assert.Equal(InventorErrorCodes.INVALID_ARGUMENT, r.Error!.Code);
    }

    [Fact]
    public void WriteCommandBlockedInReadOnly()
    {
        var d = Make(new FakeCmd { Name = "extrude", IsReadOnly = false, Body = () => throw new Exception("should not run") });
        var r = d.Dispatch(new InventorCommandContext { ReadOnly = true }, new InventorCommandEnvelope { Command = "extrude" });
        Assert.Equal(InventorErrorCodes.READ_ONLY, r.Error!.Code);
    }

    [Fact]
    public void ReadOnlyCommandAllowedInReadOnly()
    {
        var d = Make(new FakeCmd
        {
            Name = "list_parameters",
            IsReadOnly = true,
            Body = () => InventorCommandResult.Success(Guid.Empty, new JObject { ["ok"] = true }, new InventorResponseMeta()),
        });
        var r = d.Dispatch(new InventorCommandContext { ReadOnly = true }, new InventorCommandEnvelope { Command = "list_parameters" });
        Assert.True(r.Ok);
    }

    [Fact]
    public void SendCodeBlockedUnlessEnabled()
    {
        var d = Make(new FakeCmd { Name = "send_code", IsReadOnly = false, Body = () => throw new Exception("should not run") });
        var r = d.Dispatch(new InventorCommandContext { EnableSendCode = false }, new InventorCommandEnvelope { Command = "send_code" });
        Assert.Equal(InventorErrorCodes.SEND_CODE_DISABLED, r.Error!.Code);
    }

    [Fact]
    public void HandlerExceptionBecomesSanitizedApiError()
    {
        var d = Make(new FakeCmd
        {
            Name = "boom",
            IsReadOnly = true,
            Body = () => throw new InvalidOperationException(@"fail at C:\secret\path.cs"),
        });
        var r = d.Dispatch(new InventorCommandContext(), new InventorCommandEnvelope { Command = "boom" });

        Assert.Equal(InventorErrorCodes.API_ERROR, r.Error!.Code);
        Assert.DoesNotContain(@"C:\secret", r.Error!.Message);   // path stripped by ErrorSanitizer
    }

    [Fact]
    public void SuccessfulHandlerResponseKeepsEnvelopeId()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var d = Make(new FakeCmd
        {
            Name = "health",
            IsReadOnly = true,
            Body = () => InventorCommandResult.Success(Guid.Empty, new JObject { ["ok"] = true }, new InventorResponseMeta()),
        });

        var r = d.Dispatch(new InventorCommandContext(), new InventorCommandEnvelope { Id = id, Command = "health" });

        Assert.Equal(id, r.Id);
        Assert.True(r.Ok);
    }
}
#endif
