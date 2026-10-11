using Bimwright.Ipt.Shared.Handlers;
using Bimwright.Ipt.Shared.Handlers.SheetFabrication;
using Bimwright.Ipt.Shared.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Tests;

public sealed class SheetFabricationThicknessRecoveryTests
{
    [Theory]
    [InlineData(false, "transaction abort failed")]
    [InlineData(true, "transaction abort failed")]
    [InlineData(false, "previous thickness could not rebuild")]
    [InlineData(true, "previous thickness could not rebuild")]
    public void Recovery_failure_forces_batch_to_stop(bool continueOnError, string failure)
    {
        var recoveryCalls = 0;
        var laterWrites = 0;
        var result = SheetFabricationThicknessRecovery.Fail(new InventorCommandContext(),
            new InvalidOperationException("thickness update failed"), () =>
            {
                recoveryCalls++;
                throw new InvalidOperationException(failure);
            });
        var outcome = BatchExecutor.Run(JArray.Parse("[{\"command\":\"sheet_fabrication_set_thickness\"},{\"command\":\"extrude\"}]"),
            continueOnError, (command, _) =>
            {
                if (command != "sheet_fabrication_set_thickness")
                {
                    laterWrites++;
                    return BatchExecutor.StepResult.Success(null);
                }
                return BatchExecutor.StepResult.Failure($"{result.Error?.Code}: {result.Error?.Message}",
                    (bool?)(result.Data as JObject)?["rollback_failed"] == true);
            });

        Assert.Equal(1, recoveryCalls);
        Assert.True(outcome.RequiresRollback);
        Assert.Equal(0, laterWrites);
        Assert.Single(outcome.Results);
        Assert.False(result.Ok);
        Assert.Equal("API_ERROR", result.Error!.Code);
        Assert.Contains(failure, result.Error.Message);
        Assert.False((bool)result.Data!["rolled_back"]!);
        Assert.Equal("unknown", (string?)result.Data["mutation_applied"]);
    }

    [Fact]
    public void Successful_recovery_allows_a_continuing_batch()
    {
        var thickness = 2.0;
        var result = SheetFabricationThicknessRecovery.Fail(new InventorCommandContext(),
            new InvalidOperationException("thickness update failed"), () => thickness = 1.0);
        var outcome = BatchExecutor.Run(JArray.Parse("[{\"command\":\"sheet_fabrication_set_thickness\"},{\"command\":\"extrude\"}]"),
            true, (command, _) => command == "sheet_fabrication_set_thickness"
                ? BatchExecutor.StepResult.Failure(result.Error!.Message, (bool)result.Data!["rollback_failed"]!)
                : BatchExecutor.StepResult.Success(null));

        Assert.Equal(1.0, thickness);
        Assert.True((bool)result.Data!["rolled_back"]!);
        Assert.False((bool)result.Data["mutation_applied"]!);
        Assert.False(outcome.RequiresRollback);
        Assert.Equal(2, outcome.Results.Count);
    }

    [Fact]
    public void Failure_before_edit_reports_no_mutation_and_no_rollback()
    {
        var result = SheetFabricationThicknessRecovery.Fail(new InventorCommandContext(),
            new InvalidOperationException("could not read thickness"), null);

        Assert.False(result.Ok);
        Assert.False((bool)result.Data!["rolled_back"]!);
        Assert.False((bool)result.Data["mutation_applied"]!);
        Assert.False((bool)result.Data["rollback_failed"]!);
    }
}
