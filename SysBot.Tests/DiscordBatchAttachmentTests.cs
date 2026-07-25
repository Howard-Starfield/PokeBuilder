using FluentAssertions;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SysBot.Tests;

public class DiscordBatchAttachmentTests
{
    static DiscordBatchAttachmentTests() => LogConfig.EnablePerBotLogging = false;

    [Fact]
    public void BatchTradeCommand_ExposesAttachmentOverloadsWithoutRequiredShowdownText()
    {
        var commands = typeof(TradeModule<PK9>).GetMethods()
            .Where(method => method.GetCustomAttributes<CommandAttribute>()
                .Any(attribute => attribute.Text == "batchTrade"))
            .ToArray();

        commands.Any(method =>
        {
            var parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool) && parameters[0].HasDefaultValue;
        }).Should().BeTrue("bt with attachments and no text needs an overload with no required parameters");
        commands.Any(method =>
        {
            var parameters = method.GetParameters();
            return parameters.Length == 2 && parameters[0].ParameterType == typeof(int) &&
                parameters[1].ParameterType == typeof(bool) && parameters[1].HasDefaultValue;
        }).Should().BeTrue("bt attachment batches must preserve explicit trade codes and ignoreAutoOT");
        commands.Where(method => method.GetParameters().All(parameter => parameter.HasDefaultValue))
            .Should().OnlyContain(method => method.GetCustomAttributes<AliasAttribute>()
                .Any(attribute => attribute.Aliases.Contains("bt")));
    }

    [Fact]
    public void ProcessDownloads_PreservesAttachmentOrder()
    {
        var downloads = new List<DownloadedTradeAttachment>
        {
            Successful("first.pk9", 1),
            Successful("second.pk9", 4),
            Successful("third.pk9", 7),
        };

        var result = BatchAttachmentHelpers<PK9>.ProcessDownloads(
            downloads,
            pk => TradeRequestValidationResult<PK9>.Valid(pk));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Pokemon.Select(p => p.Species).Should().Equal(1, 4, 7);
    }

    [Fact]
    public void ProcessDownloads_IsAtomicAndIdentifiesInvalidAttachment()
    {
        var downloads = new List<DownloadedTradeAttachment>
        {
            Successful("first.pk9", 1),
            Successful("bad-name.pk9", 4),
            Successful("third.pk9", 7),
        };

        var result = BatchAttachmentHelpers<PK9>.ProcessDownloads(
            downloads,
            pk => pk.Species == 4
                ? TradeRequestValidationResult<PK9>.Invalid("Attachment is not legal.")
                : TradeRequestValidationResult<PK9>.Valid(pk));

        result.IsValid.Should().BeFalse();
        result.Pokemon.Should().BeEmpty("an attachment batch is all-or-nothing");
        result.Errors.Should().ContainSingle();
        result.Errors[0].Index.Should().Be(2);
        result.Errors[0].Filename.Should().Be("bad-name.pk9");
        result.Errors[0].Error.Should().Be("Attachment is not legal.");
    }

    [Fact]
    public void ProcessDownloads_ReportsDownloadFailureAndKeepsQueuePayloadEmpty()
    {
        var downloads = new List<DownloadedTradeAttachment>
        {
            Successful("first.pk9", 1),
            new("broken.pk9", new Download<PKM>
            {
                SanitizedFileName = "broken.pk9",
                ErrorMessage = "broken.pk9: Invalid pkm attachment.",
                Success = false,
            }),
        };

        var result = BatchAttachmentHelpers<PK9>.ProcessDownloads(
            downloads,
            pk => TradeRequestValidationResult<PK9>.Valid(pk));

        result.IsValid.Should().BeFalse();
        result.Pokemon.Should().BeEmpty();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Index.Should().Be(2);
        result.Errors[0].Filename.Should().Be("broken.pk9");
        result.Errors[0].Error.Should().Contain("Invalid pkm attachment");
    }

    [Fact]
    public void ProcessDownloads_ReportsValidatorFailureWithoutExposingException()
    {
        var downloads = new List<DownloadedTradeAttachment>
        {
            Successful("first.pk9", 1),
            Successful("validator-error.pk9", 4),
        };

        var result = BatchAttachmentHelpers<PK9>.ProcessDownloads(
            downloads,
            pk => pk.Species == 4
                ? throw new System.InvalidOperationException("sensitive details")
                : TradeRequestValidationResult<PK9>.Valid(pk));

        result.IsValid.Should().BeFalse();
        result.Pokemon.Should().BeEmpty();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Index.Should().Be(2);
        result.Errors[0].Filename.Should().Be("validator-error.pk9");
        result.Errors[0].Error.Should().Be("Failed to validate attachment.");
        result.Errors[0].Error.Should().NotContain("sensitive details");
    }

    [Fact]
    public void ProcessDownloads_QueuesValidatorReplacementAndTracksHandlerNormalization()
    {
        var downloads = new List<DownloadedTradeAttachment>
        {
            Successful("stale-handler.pk9", 861),
        };
        var replacement = new PK9 { Species = 861, CurrentHandler = 1 };

        var result = BatchAttachmentHelpers<PK9>.ProcessDownloads(
            downloads,
            _ => TradeRequestValidationResult<PK9>.Valid(replacement, handlerNormalized: true));

        result.IsValid.Should().BeTrue();
        result.Pokemon.Should().ContainSingle().Which.Should().BeSameAs(replacement);
        result.NormalizedHandlerCount.Should().Be(1);
    }

    [Fact]
    public void TryNormalizeCurrentHandler_ClonesAndChangesOnlyEligibleTradedPokemon()
    {
        var original = TradedPokemon(currentHandler: 0);
        PK9? observed = null;

        var repaired = TradeRequestValidator<PK9>.TryNormalizeCurrentHandler(
            original,
            handlerFlagRequired: true,
            candidate =>
            {
                observed = candidate;
                return candidate.CurrentHandler == 1;
            },
            out var normalized);

        repaired.Should().BeTrue();
        normalized.Should().NotBeSameAs(original);
        normalized.Should().BeSameAs(observed);
        normalized.CurrentHandler.Should().Be(1);
        normalized.ChecksumValid.Should().BeTrue();
        original.CurrentHandler.Should().Be(0, "the uploaded object must never be mutated");
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, true, false, 0)]
    [InlineData(true, false, true, 0)]
    [InlineData(true, false, false, 1)]
    public void TryNormalizeCurrentHandler_RejectsUnsafeCandidates(
        bool handlerFlagRequired,
        bool isEgg,
        bool clearHandlingTrainer,
        byte currentHandler)
    {
        var original = TradedPokemon(currentHandler);
        original.IsEgg = isEgg;
        if (clearHandlingTrainer)
            original.HandlingTrainerName = string.Empty;

        bool legalityWasCalled = false;
        var repaired = TradeRequestValidator<PK9>.TryNormalizeCurrentHandler(
            original,
            handlerFlagRequired,
            _ =>
            {
                legalityWasCalled = true;
                return true;
            },
            out var normalized);

        repaired.Should().BeFalse();
        normalized.Should().BeSameAs(original);
        legalityWasCalled.Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeCurrentHandler_RejectsCloneThatRemainsIllegal()
    {
        var original = TradedPokemon(currentHandler: 0);

        var repaired = TradeRequestValidator<PK9>.TryNormalizeCurrentHandler(
            original,
            handlerFlagRequired: true,
            _ => false,
            out var normalized);

        repaired.Should().BeFalse();
        normalized.Should().BeSameAs(original);
        original.CurrentHandler.Should().Be(0);
    }

    private static DownloadedTradeAttachment Successful(string filename, ushort species) =>
        new(filename, new Download<PKM>
        {
            Data = new PK9 { Species = species },
            SanitizedFileName = filename,
            Success = true,
        });

    private static PK9 TradedPokemon(byte currentHandler) => new()
    {
        Species = 861,
        OriginalTrainerName = "Event OT",
        HandlingTrainerName = "Handler",
        CurrentHandler = currentHandler,
    };
}
