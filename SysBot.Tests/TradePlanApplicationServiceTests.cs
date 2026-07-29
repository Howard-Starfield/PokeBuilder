using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SysBot.Pokemon;
using Xunit;

namespace SysBot.Tests;

public class TradePlanApplicationServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeMilliseconds(1_785_283_200_000);

    [Theory]
    [InlineData(ProgramMode.SWSH, """{"link_code":"13333333"}""")]
    [InlineData(ProgramMode.BDSP, """{"link_code":"13333333"}""")]
    [InlineData(ProgramMode.LA, """{"link_code":"13333333"}""")]
    [InlineData(ProgramMode.SV, """{"link_code":"13333333"}""")]
    [InlineData(ProgramMode.LZA, """{"link_code":"13333333"}""")]
    [InlineData(ProgramMode.LGPE, """{"pictocodes":["Pikachu","Eevee","Diglett"]}""")]
    public void Validate_AcceptsTheCorrectAccessShapeForEveryMode(
        ProgramMode mode,
        string accessJson)
    {
        using var database = new TemporaryTradeDatabase();
        var service = database.CreateService();

        var validation = service.Validate(CreateCommand(mode, accessJson));

        validation.IsValid.Should().BeTrue();
        validation.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_RejectsAccessShapeForTheWrongGame()
    {
        using var database = new TemporaryTradeDatabase();
        var service = database.CreateService();

        var lgpeWithLinkCode = service.Validate(CreateCommand(
            ProgramMode.LGPE,
            """{"link_code":"13333333"}"""));
        var lzaWithPictocodes = service.Validate(CreateCommand(
            ProgramMode.LZA,
            """{"pictocodes":["Pikachu","Eevee","Diglett"]}"""));

        lgpeWithLinkCode.IsValid.Should().BeFalse();
        lgpeWithLinkCode.Errors.Select(error => error.Code)
            .Should().OnlyContain(code => code == TradeControlErrorCodes.InvalidRequest);
        lzaWithPictocodes.IsValid.Should().BeFalse();
        lzaWithPictocodes.Errors.Should().Contain(error =>
            Equals(error.Details!["field"], "access.link_code"));
    }

    [Fact]
    public void Validate_RejectsUnprovenEvolutionHandling()
    {
        using var database = new TemporaryTradeDatabase();
        var service = database.CreateService();
        var command = CreateCommand(
            ProgramMode.LZA,
            """{"link_code":"13333333"}""") with
        {
            Policies = new TradePlanPolicies
            {
                Evolution = TradeEvolutionPolicy.AllowAndHandle,
            },
        };

        var validation = service.Validate(command);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle(error =>
            error.Code == TradeControlErrorCodes.EvolutionRequiresAttention);
    }

    [Fact]
    public void CreateDraft_ComputesIdempotencyAndReturnsOriginalPlanOnReplay()
    {
        using var database = new TemporaryTradeDatabase();
        var ids = new DeterministicIds();
        var service = database.CreateService(ids);
        var command = CreateCommand(
            ProgramMode.LZA,
            """
            {
              "link_code": "13333333"
            }
            """);

        var created = service.CreateDraft(command);
        var replayed = service.CreateDraft(command);

        created.Outcome.Should().Be(TradeStoreIdempotencyOutcome.Created);
        replayed.Outcome.Should().Be(TradeStoreIdempotencyOutcome.Replayed);
        replayed.Resource.PlanId.Should().Be(created.Resource.PlanId);
        replayed.Resource.Items.Select(item => item.ItemId)
            .Should().Equal(created.Resource.Items.Select(item => item.ItemId));
        created.Resource.AccessJson.Should().Be("""{"link_code":"13333333"}""");
        created.Resource.CreatedAt.Should().Be(Now);

        var changedPayload = command with
        {
            Items =
            [
                new("raichu", "Raichu\nLevel: 51"),
                new("pikachu", "Pikachu\nLevel: 50"),
            ],
        };
        var conflict = () => service.CreateDraft(changedPayload);
        conflict.Should().Throw<TradeStoreConflictException>();
    }

    [Fact]
    public void CreateDraft_DoesNotAllocateIdsOrWriteWhenValidationFails()
    {
        using var database = new TemporaryTradeDatabase();
        var ids = new DeterministicIds();
        var service = database.CreateService(ids);
        var invalid = CreateCommand(
            ProgramMode.LZA,
            """{"link_code":"not-a-code"}""");

        var create = () => service.CreateDraft(invalid);

        create.Should().Throw<TradePlanValidationException>()
            .Which.Errors.Should().Contain(error =>
                error.Code == TradeControlErrorCodes.InvalidRequest);
        ids.TotalCalls.Should().Be(0);
    }

    private static CreateTradePlanCommand CreateCommand(
        ProgramMode mode,
        string accessJson) =>
        new(
            "local-test",
            mode,
            accessJson,
            new TradePlanPolicies(),
            [
                new("raichu", "Raichu\nLevel: 50"),
                new("pikachu", "Pikachu\nLevel: 50"),
            ],
            "create-service-0001");

    private sealed class FixedClock : ITradeControlClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class DeterministicIds : ITradeControlIdGenerator
    {
        private int _plan;
        private int _item;

        public int TotalCalls => _plan + _item;

        public string NewPlanId() => $"plan_service{++_plan:0000}";

        public string NewItemId() => $"item_service{++_item:0000}";
    }

    private sealed class TemporaryTradeDatabase : IDisposable
    {
        private readonly string _directoryPath =
            Path.Combine(Path.GetTempPath(), $"pokebot-plan-service-tests-{Guid.NewGuid():N}");

        public TemporaryTradeDatabase()
        {
            Directory.CreateDirectory(_directoryPath);
        }

        public string DatabasePath => Path.Combine(_directoryPath, "trade-control.sqlite3");

        public TradePlanApplicationService CreateService(
            DeterministicIds? ids = null)
        {
            var store = new SqliteTradePlanStore(DatabasePath);
            store.Initialize();
            return new(store, new FixedClock(), ids ?? new DeterministicIds());
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(_directoryPath);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a test directory outside the temp root.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }
    }
}
