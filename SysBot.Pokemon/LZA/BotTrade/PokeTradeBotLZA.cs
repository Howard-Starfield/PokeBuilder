using PKHeX.Core;
using PKHeX.Core.Searching;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsLZA;

namespace SysBot.Pokemon;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class PokeTradeBotLZA(PokeTradeHub<PA9> Hub, PokeBotState Config) : PokeRoutineExecutor9LZA(Config), ICountBot
{
    private readonly TradeSettings TradeSettings = Hub.Config.Trade;
    private readonly TradeAbuseSettings AbuseSettings = Hub.Config.TradeAbuse;

    public ICountSettings Counts => TradeSettings;

    /// <summary>
    /// Folder to dump received trade data to.
    /// </summary>
    /// <remarks>If null, will skip dumping.</remarks>
    private readonly IDumper DumpSetting = Hub.Config.Folder;

    /// <summary>
    /// Synchronized start for multiple bots.
    /// </summary>
    public bool ShouldWaitAtBarrier { get; private set; }

    /// <summary>
    /// Tracks failed synchronized starts to attempt to re-sync.
    /// </summary>
    public int FailedBarrier { get; private set; }

    // Cached offsets that stay the same per session.
    private ulong BoxStartOffset;

    // Cached offsets that stay the same after connecting online.
    private ulong TradePartnerNIDOffset;
    private ulong TradePartnerTIDOffset;

    // Cached offsets that stay the same per trade.
    private ulong TradePartnerStatusOffset;

    // Stores whether we returned all the way to the overworld, which repositions the cursor.
    private bool StartFromOverworld = true;

    public override async Task MainLoop(CancellationToken token)
    {
        try
        {
            await InitializeHardware(Hub.Config.Trade, token).ConfigureAwait(false);

            Log("Identifying trainer data of the host console.");
            var sav = await IdentifyTrainer(token).ConfigureAwait(false);
            RecentTrainerCache.SetRecentTrainer(sav);
            await InitializeSessionOffsets(token).ConfigureAwait(false);
            // It's possible to start off already connected.
            if (await IsConnected(token).ConfigureAwait(false))
                await InitializeOnlineOffsets(token).ConfigureAwait(false);
            StartFromOverworld = true;

            Log($"Starting main {nameof(PokeTradeBotLZA)} loop.");
            await InnerLoop(sav, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown; no-op.
        }
        catch (Exception e)
        {
            Log(e.Message);
        }

        Log($"Ending {nameof(PokeTradeBotLZA)} loop.");
        await HardStop().ConfigureAwait(false);
    }

    public override Task HardStop()
    {
        UpdateBarrier(false);
        return CleanExit(CancellationToken.None);
    }

    public override async Task RebootAndStop(CancellationToken t)
    {
        await Task.Delay(2_000, t).ConfigureAwait(false);
        await ReOpenGame(Hub.Config, t).ConfigureAwait(false);
        await HardStop().ConfigureAwait(false);
        await Task.Delay(2_000, t).ConfigureAwait(false);
        if (!t.IsCancellationRequested)
        {
            Log("Restarting the main loop.");
            await MainLoop(t).ConfigureAwait(false);
        }
    }

    private async Task InnerLoop(SAV9ZA sav, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Config.IterateNextRoutine();
            var task = Config.CurrentRoutineType switch
            {
                PokeRoutineType.Idle => DoNothing(token),
                _ => DoTrades(sav, token),
            };
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (SocketException e)
            {
                if (e.StackTrace != null)
                    Connection.LogError(e.StackTrace);
                var attempts = Hub.Config.Timings.ReconnectAttempts;
                var delay = Hub.Config.Timings.ExtraReconnectDelay;
                var protocol = Config.Connection.Protocol;
                if (!await TryReconnect(attempts, delay, protocol, token).ConfigureAwait(false))
                    return;
            }
        }
    }

    private async Task DoNothing(CancellationToken token)
    {
        int waitCounter = 0;
        while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.Idle)
        {
            if (waitCounter == 0)
                Log("No task assigned. Waiting for new task assignment.");
            waitCounter++;
            if (waitCounter % 10 == 0 && Hub.Config.AntiIdle)
                await Click(B, 1_000, token).ConfigureAwait(false);
            else
                await Task.Delay(1_000, token).ConfigureAwait(false);
        }
    }

    private async Task DoTrades(SAV9ZA sav, CancellationToken token)
    {
        var type = Config.CurrentRoutineType;
        int waitCounter = 0;
        await SetCurrentBox(0, token).ConfigureAwait(false);
        while (!token.IsCancellationRequested && Config.NextRoutineType == type)
        {
            var (detail, priority) = GetTradeData(type);
            if (detail is null)
            {
                await WaitForQueueStep(waitCounter++, token).ConfigureAwait(false);
                continue;
            }
            waitCounter = 0;

            detail.IsProcessing = true;
            string tradetype = $" ({detail.Type})";
            Log($"Starting next {type}{tradetype} Bot Trade. Getting data...");
            Hub.Config.Stream.StartTrade(this, detail, Hub);
            Hub.Queues.StartTrade(this, detail);

            await PerformTrade(sav, detail, type, priority, token).ConfigureAwait(false);
        }
    }

    private Task WaitForQueueStep(int waitCounter, CancellationToken token)
    {
        if (waitCounter == 0)
        {
            // Updates the assets.
            Hub.Config.Stream.IdleAssets(this);
            Log("Nothing to check, waiting for new users...");
        }

        const int interval = 10;
        if (waitCounter % interval == interval - 1 && Hub.Config.AntiIdle)
            return Click(B, 1_000, token);
        return Task.Delay(1_000, token);
    }

    protected virtual (PokeTradeDetail<PA9>? detail, uint priority) GetTradeData(PokeRoutineType type)
    {
        if (Hub.Queues.TryDequeue(type, out var detail, out var priority, Connection.Name))
            return (detail, priority);

        // If we're doing FlexTrade, also check the Batch queue
        if (type == PokeRoutineType.FlexTrade)
        {
            if (Hub.Queues.TryDequeue(PokeRoutineType.Batch, out detail, out priority, Connection.Name))
                return (detail, priority);
        }

        if (Hub.Queues.TryDequeueLedy(out detail))
            return (detail, PokeTradePriorities.TierFree);
        return (null, PokeTradePriorities.TierFree);
    }

    private static void ApplyTrainerInfo(PA9 pokemon, TradePartnerStatusLZA partner)
    {
        pokemon.OriginalTrainerGender = (byte)partner.Gender;
        pokemon.TrainerTID7 = partner.DisplayTID;
        pokemon.TrainerSID7 = partner.DisplaySID;
        pokemon.OriginalTrainerName = partner.OT;
    }

    private async Task<PA9> ApplyAutoOT(PA9 toSend, TradePartnerStatusLZA tradePartner, SAV9ZA sav, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(tradePartner.OT))
            return toSend;

        if (toSend.Version == GameVersion.GO)
        {
            var goClone = toSend.Clone();
            goClone.OriginalTrainerName = tradePartner.OT;
            ClearOTTrash(goClone, tradePartner);

            if (!toSend.ChecksumValid)
                goClone.RefreshChecksum();

            await SetBoxPokemonAbsolute(BoxStartOffset, goClone, token, sav).ConfigureAwait(false);
            return goClone;
        }

        if (toSend is IHomeTrack pk && pk.HasTracker)
            return toSend;

        if (toSend.Generation != toSend.Format)
            return toSend;

        bool isMysteryGift = toSend.FatefulEncounter;
        var clone = toSend.Clone();

        ApplyTrainerInfo(clone, tradePartner);

        if (!isMysteryGift)
        {
            int language = tradePartner.Language;
            if (language < 1 || language > 12)
                language = (int)LanguageID.English;
            clone.Language = language;
        }

        ClearOTTrash(clone, tradePartner);
        clone.Version = GameVersion.ZA;

        if (!toSend.IsNicknamed)
            clone.ClearNickname();

        clone.CurrentHandler = 0;

        if (toSend.IsShiny)
            clone.PID = (uint)((clone.TID16 ^ clone.SID16 ^ (clone.PID & 0xFFFF) ^ toSend.ShinyXor) << 16) | (clone.PID & 0xFFFF);

        clone.RefreshChecksum();

        var legality = new LegalityAnalysis(clone);
        if (!legality.Valid)
        {
            if (toSend.Species != 0)
                await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);
            return toSend;
        }

        await SetBoxPokemonAbsolute(BoxStartOffset, clone, token, null).ConfigureAwait(false);
        return clone;
    }

    private static void ClearOTTrash(PA9 pokemon, TradePartnerStatusLZA tradePartner)
    {
        Span<byte> trash = pokemon.OriginalTrainerTrash;
        trash.Clear();
        string name = tradePartner.OT;
        int maxLength = trash.Length / 2;
        int actualLength = Math.Min(name.Length, maxLength);
        for (int i = 0; i < actualLength; i++)
        {
            char value = name[i];
            trash[i * 2] = (byte)value;
            trash[(i * 2) + 1] = (byte)(value >> 8);
        }

        if (actualLength < maxLength)
        {
            trash[actualLength * 2] = 0x00;
            trash[(actualLength * 2) + 1] = 0x00;
        }
    }

    private async Task PerformTrade(SAV9ZA sav, PokeTradeDetail<PA9> detail, PokeRoutineType type, uint priority, CancellationToken token)
    {
        PokeTradeResult result;
        try
        {
            result = await PerformLinkCodeTrade(sav, detail, token).ConfigureAwait(false);
            if (result == PokeTradeResult.Success)
                return;
        }
        catch (OperationCanceledException)
        {
            // Let cancellation bubble so outer loop can exit promptly.
            throw;
        }
        catch (SocketException socket)
        {
            Log(socket.Message);
            result = PokeTradeResult.ExceptionConnection;
            HandleAbortedTrade(detail, type, priority, result);
            throw; // let this interrupt the trade loop. re-entering the trade loop will recheck the connection.
        }
        catch (Exception e)
        {
            Log(e.Message);
            result = PokeTradeResult.ExceptionInternal;
        }

        HandleAbortedTrade(detail, type, priority, result);
    }

    private void HandleAbortedTrade(PokeTradeDetail<PA9> detail, PokeRoutineType type, uint priority, PokeTradeResult result)
    {
        detail.IsProcessing = false;
        if (result.ShouldAttemptRetry() && detail.Type != PokeTradeType.Random && !detail.IsRetry)
        {
            detail.IsRetry = true;
            Hub.Queues.Enqueue(type, detail, Math.Min(priority, PokeTradePriorities.Tier2));
            detail.SendNotification(this, "Oops! Something happened. I'll requeue you for another attempt.");
        }
        else
        {
            detail.SendNotification(this, $"Oops! Something happened. Canceling the trade: {result}.");
            detail.TradeCanceled(this, result);
        }
    }

    private async Task<PokeTradeResult> PerformLinkCodeTrade(SAV9ZA sav, PokeTradeDetail<PA9> poke, CancellationToken token)
    {
        // Update Barrier Settings
        UpdateBarrier(poke.IsSynchronized);
        poke.TradeInitialize(this);
        Hub.Config.Stream.EndEnterCode(this);

        // If we're expected to be on the overworld and we aren't, recover there.
        if (StartFromOverworld && !await IsOnOverworld(token).ConfigureAwait(false))
            await ResetToOverworld(token).ConfigureAwait(false);

        // If we're expected to start on Link Play menu and we aren't there, reset to overworld.
        if (!StartFromOverworld && !await IsOnMenu(MenuState.LinkPlay, token).ConfigureAwait(false))
        {
            await ResetToOverworld(token).ConfigureAwait(false);
            StartFromOverworld = true;
        }

        var toSend = poke.TradeData;
        if (toSend.Species != 0)
            await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);

        // If we're starting from the overworld, open the menu.
        if (StartFromOverworld)
        {
            Log("Entering Link Play menu.");
            await Click(X, 0_800, token).ConfigureAwait(false);
            await Click(DUP, 0_300, token).ConfigureAwait(false);
            await Click(A, 0_800, token).ConfigureAwait(false);
        }

        Log("Selecting Link Trade.");
        await Click(DLEFT, 0_400, token).ConfigureAwait(false);
        await Click(A, 0_800, token).ConfigureAwait(false);
        await Click(DRIGHT, 0_400, token).ConfigureAwait(false);

        // If we're not connected, the first click will connect and save the game, which will take a few seconds.
        if (!await IsConnected(token).ConfigureAwait(false))
        {
            Log("Connecting online.");
            await Click(A, 0_300, token).ConfigureAwait(false);
            while (!await IsConnected(token).ConfigureAwait(false))
                await (Click(A, 0_200, token)).ConfigureAwait(false);
            await (Task.Delay(1_000, token)).ConfigureAwait(false);
            Log("Successfully connected!");
            await InitializeOnlineOffsets(token).ConfigureAwait(false);
        }
        else
        {
            // If already connected, an extra click is needed to open the keypad.
            await Click(A, 0_500, token).ConfigureAwait(false);
        }

        // Only need one more click to open the keypad.
        await Click(A, 0_800, token).ConfigureAwait(false);

        // Loading code entry.
        if (poke.Type != PokeTradeType.Random)
            Hub.Config.Stream.StartEnterCode(this);
        await Task.Delay(Hub.Config.Timings.ExtraTimeOpenCodeEntry, token).ConfigureAwait(false);

        var code = poke.Code;

        // LZA has more complex logic for entering the link code.
        await EnterLinkCodeLZA(code, token).ConfigureAwait(false);

        // Wait for Barrier to trigger all bots simultaneously.
        WaitAtBarrierIfApplicable(token);
        await Click(PLUS, 1_000, token).ConfigureAwait(false);

        poke.TradeSearching(this);

        // Wait for a Trainer...
        var partnerFound = await WaitForTradePartner(poke, token).ConfigureAwait(false);

        if (token.IsCancellationRequested)
        {
            await ResetToOverworld(token).ConfigureAwait(false);
            return PokeTradeResult.RoutineCancel;
        }
        if (!partnerFound)
        {
            // Make sure we cancel the trade search first if we waited less than 55 seconds.
            // Actual timeout seems to be just over 60 seconds, but it's better not to accidentally click into keypad again.
            if (Hub.Config.Trade.TradeWaitTime < 55)
            {
                await Click(B, 0_500, token).ConfigureAwait(false);
                await Click(A, 0_300, token).ConfigureAwait(false);
            }
            await ResetToLinkPlay(token).ConfigureAwait(false);
            var cancelResult = poke.IsCanceled ? PokeTradeResult.UserCanceled : PokeTradeResult.NoTrainerFound;
            poke.SendNotification(this, poke.IsCanceled
                ? "Trade canceled by user."
                : "No trading partner found. Canceling the trade.");
            poke.TradeCanceled(this, cancelResult);
            return cancelResult;
        }

        Hub.Config.Stream.EndEnterCode(this);

        // Some more time to fully enter the trade.
        await Task.Delay(1_000 + Hub.Config.Timings.ExtraTimeOpenBox, token).ConfigureAwait(false);

        var tradePartnerBasic = await GetTradePartnerInfo(token).ConfigureAwait(false);
        var tradePartnerFullInfo = await GetTradePartnerFullInfo(token).ConfigureAwait(false);
        var tradePartner = new TradePartnerLZA(tradePartnerBasic.NID, tradePartnerFullInfo);
        RecordUtil<PokeTradeBotLZA>.Record($"Initiating\t{tradePartner.NID:X16}\t{tradePartner.TrainerName}\t{poke.Trainer.TrainerName}\t{poke.Trainer.ID}\t{poke.ID}\t{toSend.EncryptionConstant:X8}");
        Log($"Found Link Trade partner: {tradePartner.TrainerName} (Gender: {tradePartner.GenderString}, Language: {tradePartner.LanguageString})-{tradePartner.TID7} (ID: {tradePartner.NID})");

        var tradeCodeStorage = new TradeCodeStorage();
        var existingTradeDetails = tradeCodeStorage.GetTradeDetails(poke.Trainer.ID);

        bool shouldUpdateOT = existingTradeDetails?.OT != tradePartner.TrainerName;
        bool shouldUpdateTID = existingTradeDetails?.TID != int.Parse(tradePartner.TID7);
        bool shouldUpdateSID = existingTradeDetails?.SID != int.Parse(tradePartner.SID7);

        if (shouldUpdateOT || shouldUpdateTID || shouldUpdateSID)
        {
            string? ot = shouldUpdateOT ? tradePartner.TrainerName : existingTradeDetails?.OT;
            int? tid = shouldUpdateTID ? int.Parse(tradePartner.TID7) : existingTradeDetails?.TID;
            int? sid = shouldUpdateSID ? int.Parse(tradePartner.SID7) : existingTradeDetails?.SID;

            if (ot != null && tid.HasValue && sid.HasValue)
                tradeCodeStorage.UpdateTradeDetails(poke.Trainer.ID, ot, tid.Value, sid.Value);
        }

        var partnerCheck = CheckPartnerReputation(this, poke, tradePartner.NID, tradePartner.TrainerName, AbuseSettings, token);
        if (partnerCheck != PokeTradeResult.Success)
        {
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return partnerCheck;
        }

        poke.SendNotification(this, $"Found Link Trade partner: {tradePartner.TrainerName}. Waiting for a Pokémon...");

        if (poke.Type == PokeTradeType.Dump)
        {
            var result = await ProcessDumpTradeAsync(poke, token).ConfigureAwait(false);
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return result;
        }

        if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
        {
            toSend = await ApplyAutoOT(toSend, tradePartnerFullInfo, sav, token).ConfigureAwait(false);
            poke.TradeData = toSend;
        }

        if (poke.Type == PokeTradeType.Batch)
            return await PerformBatchTrade(sav, poke, tradePartner, tradePartnerFullInfo, token).ConfigureAwait(false);

        // Watch their status to indicate they have offered a Pokémon as well.
        var offering = await ReadUntilChanged(TradePartnerStatusOffset, [0x3], 25_000, 1_000, true, true, token).ConfigureAwait(false);
        if (!offering)
        {
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        Log("Checking offered Pokémon.");
        // If we got to here, we can read their offered Pokémon.

        // Wait for user input... Needs to be different from the previously offered Pokémon.
        var offered = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 3_000, 0_050, BoxFormatSlotSize, token).ConfigureAwait(false);
        if (offered == null || offered.Species == 0 || !offered.ChecksumValid)
        {
            Log("Trade ended because trainer offer was rescinded too quickly.");
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return PokeTradeResult.TrainerOfferCanceledQuick;
        }
        offered.Heal();
        offered.RefreshChecksum();

        var trainer = new PartnerDataHolder(0, tradePartner.TrainerName, tradePartner.TID7);
        (toSend, PokeTradeResult update) = await GetEntityToSend(sav, poke, offered, toSend, trainer, token).ConfigureAwait(false);
        if (update != PokeTradeResult.Success)
        {
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return update;
        }

        if (Hub.Config.Trade.DisallowTradeEvolve && TradeEvolutions.WillTradeEvolve(offered.Species, offered.Form, offered.HeldItem, toSend.Species))
        {
            Log("Trade cancelled because trainer offered a Pokémon that would evolve upon trade.");
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return PokeTradeResult.TradeEvolveNotAllowed;
        }

        Log("Confirming trade.");
        var tradeResult = await ConfirmAndStartTrading(poke, token).ConfigureAwait(false);
        if (tradeResult != PokeTradeResult.Success)
        {
            if (tradeResult == PokeTradeResult.TrainerLeft)
                Log("Trade canceled because trainer left the trade.");
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return tradeResult;
        }

        if (token.IsCancellationRequested)
        {
            await ResetToOverworld(token).ConfigureAwait(false);
            return PokeTradeResult.RoutineCancel;
        }

        // Trade was successful!
        var received = await ReadPokemon(BoxStartOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
        // Pokémon in b1s1 is same as the one they were supposed to receive (was never sent).
        if (SearchUtil.HashByDetails(received) == SearchUtil.HashByDetails(toSend) && received.Checksum == toSend.Checksum)
        {
            Log("User did not complete the trade.");
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        // As long as we got rid of our inject in b1s1, assume the trade went through.
        Log("User completed the trade.");
        poke.TradeFinished(this, received);

        // Only log if we completed the trade.
        UpdateCountsAndExport(poke, received, toSend);

        // Log for Trade Abuse tracking.
        LogSuccessfulTrades(poke, tradePartner.NID, tradePartner.TrainerName);

        await ResetToLinkPlay(token).ConfigureAwait(false);
        return PokeTradeResult.Success;
    }

    private void UpdateCountsAndExport(PokeTradeDetail<PA9> poke, PA9 received, PA9 toSend)
    {
        var counts = TradeSettings;
        if (poke.Type == PokeTradeType.Random)
            counts.AddCompletedDistribution();
        else if (poke.Type == PokeTradeType.Clone)
            counts.AddCompletedClones();
        else
            counts.AddCompletedTrade();

        if (DumpSetting.Dump && !string.IsNullOrEmpty(DumpSetting.DumpFolder))
        {
            var subfolder = poke.Type.ToString().ToLower();
            DumpPokemon(DumpSetting.DumpFolder, subfolder, received); // received by bot
            if (poke.Type is PokeTradeType.Specific or PokeTradeType.Clone)
                DumpPokemon(DumpSetting.DumpFolder, "traded", toSend); // sent to partner
        }
    }

    private async Task<PokeTradeResult> ConfirmAndStartTrading(PokeTradeDetail<PA9> detail, CancellationToken token)
    {
        if (detail.RequiresControlPlaneEvolutionBlock())
        {
            var offered = await ReadUntilPresentPointer(
                Offsets.LinkTradePartnerPokemonPointer,
                2_000,
                0_200,
                BoxFormatSlotSize,
                token).ConfigureAwait(false);
            if (offered is not null &&
                TradeEvolutions.WillTradeEvolve(
                    offered.Species,
                    offered.Form,
                    offered.HeldItem,
                    detail.TradeData.Species))
            {
                detail.SendNotification(this, "Trade cancelled before confirmation because the offered Pokémon would evolve.");
                return PokeTradeResult.TradeEvolveNotAllowed;
            }
        }
        detail.ReportLifecycle(PokeTradeLifecycleStage.Confirming);
        // We'll keep watching B1S1 for a change to indicate a trade started -> should try quitting at that point.
        var oldEC = await SwitchConnection.ReadBytesAbsoluteAsync(BoxStartOffset, 8, token).ConfigureAwait(false);

        await Click(A, 3_000, token).ConfigureAwait(false);
        for (int i = 0; i < Hub.Config.Trade.MaxTradeConfirmTime; i++)
        {
            if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
                return PokeTradeResult.TrainerLeft;
            if (await IsUserBeingShifty(detail, token).ConfigureAwait(false))
                return PokeTradeResult.SuspiciousActivity;
            await Click(A, 1_000, token).ConfigureAwait(false);

            // EC is detectable at the start of the animation.
            var newEC = await SwitchConnection.ReadBytesAbsoluteAsync(BoxStartOffset, 8, token).ConfigureAwait(false);
            if (!newEC.SequenceEqual(oldEC))
            {
                detail.ReportLifecycle(PokeTradeLifecycleStage.Settling);
                await Task.Delay(30_000, token).ConfigureAwait(false);
                return PokeTradeResult.Success;
            }
        }
        if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
            return PokeTradeResult.TrainerLeft;

        // If we don't detect a B1S1 change, the trade didn't go through in that time.
        return PokeTradeResult.TrainerTooSlow;
    }

    protected virtual async Task<bool> WaitForTradePartner(PokeTradeDetail<PA9> poke, CancellationToken token)
    {
        Log("Waiting for trainer...");
        int remainMs = (Hub.Config.Trade.TradeWaitTime * 1_000) - 2_000;
        await Task.Delay(2_000, token).ConfigureAwait(false);
        while (remainMs > 0)
        {
            if (poke.IsCanceled) return false;
            if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
            {
                await Task.Delay(0_100, token).ConfigureAwait(false);
                remainMs -= 0_100;
                continue;
            }
            remainMs -= 0_500;
            await Task.Delay(0_500, token).ConfigureAwait(false);

            // If we made it to here, then we're in the box. Set the offset for their status.
            var (valid, offset) = await ValidatePointerAll(Offsets.TradePartnerStatusPointer, token).ConfigureAwait(false);
            if (!valid)
                continue;
            TradePartnerStatusOffset = offset;
            return true;
        }
        return false;
    }

    // Generally used for recovery if we can't make it to Link Play for some reason.
    private async Task ResetToOverworld(CancellationToken token)
    {
        if (await IsOnOverworld(token).ConfigureAwait(false))
            return;

        Log("Resetting to the overworld...");
        // If we're in the Box or searching for a Link Trade, we need to use the BAB approach, otherwise we can just mash B.
        var remainMs = 120_000;
        while (await GetMenuState(token).ConfigureAwait(false) >= MenuState.LinkTrade)
        {
            if (remainMs < 0)
            {
                // Failed to exit somehow.
                await RestartGameLZA(token).ConfigureAwait(false);
                return;
            }

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) < MenuState.LinkTrade)
                break;

            var box = await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false);
            await Click(box ? A : B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) < MenuState.LinkTrade)
                break;

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) < MenuState.LinkTrade)
                break;
            remainMs -= 3_000;
        }

        // From here, we should be able to press B.
        while (!await IsOnOverworld(token).ConfigureAwait(false))
            await Click(B, 0_200, token).ConfigureAwait(false);

        StartFromOverworld = true;
    }

    // We'll be doing this most of the time. Going to the overworld is a little slower.
    private async Task ResetToLinkPlay(CancellationToken token)
    {
        var current = await GetMenuState(token).ConfigureAwait(false);
        if (current == MenuState.LinkPlay)
        {
            StartFromOverworld = false;
            return;
        }

        // Already on an earlier menu than Link Trade. Just go to overworld and start over next trade.
        if (current < MenuState.LinkPlay)
        {
            await ResetToOverworld(token).ConfigureAwait(false);
            StartFromOverworld = true;
            return;
        }

        Log("Resetting to the Link Play menu...");
        // If we're in the Box or searching for a Link Trade, we need to use the BAB approach, otherwise we can just mash B.
        var remainMs = 120_000;
        while (await GetMenuState(token).ConfigureAwait(false) >= MenuState.LinkPlay)
        {
            if (remainMs < 0)
            {
                // Failed to exit somehow.
                await RestartGameLZA(token).ConfigureAwait(false);
                StartFromOverworld = true;
                return;
            }

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) == MenuState.LinkPlay)
                break;

            var box = await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false);
            await Click(box ? A : B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) == MenuState.LinkPlay)
                break;

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) == MenuState.LinkPlay)
                break;
            remainMs -= 3_000;
        }

        // Wait a little bit extra in case of slow box closing.
        await Task.Delay(0_800, token).ConfigureAwait(false);
        StartFromOverworld = false;
    }

    // LZA saves the previous Link Code after the first trade.
    // If the pointer isn't valid, we haven't traded yet.
    // Otherwise, we should be able to see if it's the same and how long it is.
    private async Task EnterLinkCodeLZA(int code, CancellationToken token)
    {
        var (valid, _) = await ValidatePointerAll(Offsets.LinkTradeCodePointer, token).ConfigureAwait(false);
        if (!valid)
        {
            // If it's not valid, then we can freely enter our code in because no trades have been done yet.
            Log($"Entering Link Trade code: {code:0000 0000}...");
            await EnterLinkCode(code, Hub.Config, token).ConfigureAwait(false);
        }
        else
        {
            var prev_code = await GetStoredLinkTradeCode(token).ConfigureAwait(false);
            if (prev_code != code) // Only clear if the new code is different.
            {
                var code_length = await GetStoredLinkTradeCodeLength(token).ConfigureAwait(false);
                if (code_length > 0)
                    await PressAndHold(B, (code_length * 0_100) + 0_200, 0_100, token).ConfigureAwait(false);

                Log($"Entering Link Trade code: {code:0000 0000}...");
                await EnterLinkCode(code, Hub.Config, token).ConfigureAwait(false);
            }
            else
            {
                Log($"Using previous Link Trade code: {code:0000 0000}.");
            }
        }
    }

    // These don't change per game session, and we access them frequently, so set these each time we start.
    private async Task InitializeSessionOffsets(CancellationToken token)
    {
        Log("Caching session offsets...");
        BoxStartOffset = await SwitchConnection.PointerAll(Offsets.BoxStartPokemonPointer, token).ConfigureAwait(false);
    }

    // These don't change per online session, so set them whenever we connect.
    private async Task InitializeOnlineOffsets(CancellationToken token)
    {
        Log("Caching online offsets...");
        var baseOffset = await SwitchConnection.PointerAll(Offsets.LinkTradePartnerDataPointer, token).ConfigureAwait(false);
        TradePartnerNIDOffset = baseOffset + TradePartnerNIDShift;
        TradePartnerTIDOffset = baseOffset + TradePartnerTIDShift;
    }

    // todo: future
    protected virtual async Task<bool> IsUserBeingShifty(PokeTradeDetail<PA9> detail, CancellationToken token)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return false;
    }

    private async Task RestartGameLZA(CancellationToken token)
    {
        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
        await InitializeSessionOffsets(token).ConfigureAwait(false);
    }

    private async Task<PokeTradeResult> ProcessDumpTradeAsync(PokeTradeDetail<PA9> detail, CancellationToken token)
    {
        int dumped = 0;
        var time = TimeSpan.FromSeconds(Hub.Config.Trade.MaxDumpTradeTime);
        var start = DateTime.Now;

        var pkprev = new PA9();
        var pressB = 0;
        while (dumped < Hub.Config.Trade.MaxDumpsPerTrade && DateTime.Now - start < time)
        {
            if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
                break;
            if (pressB++ % 3 == 0)
                await Click(B, 0_100, token).ConfigureAwait(false);

            // Wait for user input... Needs to be different from the previously offered Pokémon.
            var pk = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 3_000, 0_050, BoxFormatSlotSize, token).ConfigureAwait(false);
            if (pk == null || pk.Species == 0 || !pk.ChecksumValid || SearchUtil.HashByDetails(pk) == SearchUtil.HashByDetails(pkprev))
                continue;
            pk.Heal();
            pk.RefreshChecksum();

            // Save the new Pokémon for comparison next round.
            pkprev = pk;

            // Send results from separate thread; the bot doesn't need to wait for things to be calculated.
            if (DumpSetting.Dump)
            {
                var subfolder = detail.Type.ToString().ToLower();
                DumpPokemon(DumpSetting.DumpFolder, subfolder, pk); // received
            }

            var la = new LegalityAnalysis(pk);
            var verbose = $"```{la.Report(true)}```";
            Log($"Shown Pokémon is: {(la.Valid ? "Valid" : "Invalid")}.");

            dumped++;
            var msg = Hub.Config.Trade.DumpTradeLegalityCheck ? verbose : $"File {dumped}";

            // Extra information about trainer data for people requesting with their own trainer data.
            var ot = pk.OriginalTrainerName;
            var ot_gender = pk.OriginalTrainerGender == 0 ? "Male" : "Female";
            var tid = pk.GetDisplayTID().ToString(pk.GetTrainerIDFormat().GetTrainerIDFormatStringTID());
            var sid = pk.GetDisplaySID().ToString(pk.GetTrainerIDFormat().GetTrainerIDFormatStringSID());
            msg += $"\n**Trainer Data**\n```OT: {ot}\nOTGender: {ot_gender}\nTID: {tid}\nSID: {sid}```";

            msg += pk.IsShiny ? "\n**This Pokémon is shiny!**" : string.Empty;
            detail.SendNotification(this, pk, msg);
        }

        Log($"Ended Dump loop after processing {dumped} Pokémon.");
        if (dumped == 0)
            return PokeTradeResult.TrainerTooSlow;

        TradeSettings.AddCompletedDumps();
        detail.Notifier.SendNotification(this, detail, $"Dumped {dumped} Pokémon.");
        detail.Notifier.TradeFinished(this, detail, detail.TradeData); // blank PA9
        return PokeTradeResult.Success;
    }

    private async Task<TradePartnerLZA> GetTradePartnerInfo(CancellationToken token)
    {
        // Grab a chunk of bytes starting from the NID. Most likely this will also include the OT and TID.
        // Check if data is loaded at the last byte of this chunk. If it's not loaded, we'll have to try and find OT and TID at the fallback location.
        var chunk = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerNIDOffset, 0x69, token).ConfigureAwait(false);

        // NID should be the first 8 bytes, converted to a ulong.
        var id = chunk.AsSpan(0, 8).ToArray();
        var nid = BitConverter.ToUInt64(id);
        if (nid == 0) // They probably left too quickly, so try the backup pointer.
            nid = await GetTradePartnerNID(token).ConfigureAwait(false);

        // Now check if the last byte is populated.
        if (chunk[0x68] != 0)
        {
            // Data is loaded here, so we can read TID and OT from here.
            var tid = chunk.AsSpan(0x44, 4).ToArray();
            var name = chunk.AsSpan(0x4C, TradePartnerLZA.MaxByteLengthStringObject).ToArray();
            return new TradePartnerLZA(nid, tid, name);
        }
        // Data is not loaded at the expected place, so we have to read TID and OT from the fallback location.
        {
            chunk = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerTIDOffset + FallBackTradePartnerDataShift, 34, token).ConfigureAwait(false);
            var tid = chunk.AsSpan(0, 4).ToArray();
            var name = chunk.AsSpan(0x8, TradePartnerLZA.MaxByteLengthStringObject).ToArray();
            return new TradePartnerLZA(nid, tid, name);
        }
    }

    private async Task<TradePartnerStatusLZA> GetTradePartnerFullInfo(CancellationToken token)
    {
        var partner = new TradePartnerStatusLZA();

        var chunk = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerNIDOffset, 0x69, token).ConfigureAwait(false);
        if (chunk[0x68] != 0)
        {
            chunk.AsSpan(0x44, 4).CopyTo(partner.Data);
            partner.Data[0x04] = chunk[0x48];
            partner.Data[0x05] = chunk[0x49];
            chunk.AsSpan(0x4C, TradePartnerLZA.MaxByteLengthStringObject).CopyTo(partner.Data.AsSpan(0x08));
            return partner;
        }

        chunk = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerTIDOffset + FallBackTradePartnerDataShift, 34, token).ConfigureAwait(false);
        chunk.AsSpan(0, 4).CopyTo(partner.Data);
        partner.Data[0x04] = chunk[0x04];
        partner.Data[0x05] = chunk[0x05];
        chunk.AsSpan(0x08, TradePartnerLZA.MaxByteLengthStringObject).CopyTo(partner.Data.AsSpan(0x08));
        return partner;
    }

    protected virtual async Task<(PA9 toSend, PokeTradeResult check)> GetEntityToSend(SAV9ZA sav, PokeTradeDetail<PA9> poke, PA9 offered, PA9 toSend, PartnerDataHolder partnerID, CancellationToken token)
    {
        return poke.Type switch
        {
            PokeTradeType.Random => await HandleRandomLedy(sav, poke, offered, toSend, partnerID, token).ConfigureAwait(false),
            PokeTradeType.Clone => await HandleClone(sav, poke, offered, token).ConfigureAwait(false),
            _ => (toSend, PokeTradeResult.Success),
        };
    }

    private async Task<(PA9 toSend, PokeTradeResult check)> HandleClone(SAV9ZA sav, PokeTradeDetail<PA9> poke, PA9 offered, CancellationToken token)
    {
        if (Hub.Config.Discord.ReturnPKMs)
            poke.SendNotification(this, offered, "Here's what you showed me!");

        var la = new LegalityAnalysis(offered);
        if (!la.Valid)
        {
            Log($"Clone request (from {poke.Trainer.TrainerName}) has detected an invalid Pokémon: {GetSpeciesName(offered.Species)}.");
            if (DumpSetting.Dump)
                DumpPokemon(DumpSetting.DumpFolder, "hacked", offered);

            var report = la.Report();
            Log(report);
            poke.SendNotification(this, "This Pokémon is not legal per PKHeX's legality checks. I am forbidden from cloning this. Exiting trade.");
            poke.SendNotification(this, report);

            return (offered, PokeTradeResult.IllegalTrade);
        }

        var clone = offered.Clone();
        if (Hub.Config.Legality.ResetHOMETracker)
            clone.Tracker = 0;

        var cloneSpecies = GetSpeciesName(clone.Species);
        poke.SendNotification(this, $"**Cloned your {cloneSpecies}!**\nNow press B to cancel your offer and trade me a Pokémon you don't want.");
        Log($"Cloned a {cloneSpecies}. Waiting for user to change their Pokémon...");

        if (!await CheckCloneChangedOffer(token).ConfigureAwait(false))
        {
            // They get one more chance.
            poke.SendNotification(this, "**HEY CHANGE IT NOW OR I AM LEAVING!!!**");
            if (!await CheckCloneChangedOffer(token).ConfigureAwait(false))
            {
                Log("Trade partner did not change their Pokémon.");
                return (offered, PokeTradeResult.TrainerTooSlow);
            }
        }

        // If we got to here, we can read their offered Pokémon.
        var pk2 = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 5_000, 1_000, BoxFormatSlotSize, token).ConfigureAwait(false);
        if (pk2 is null || SearchUtil.HashByDetails(pk2) == SearchUtil.HashByDetails(offered))
        {
            Log("Trade partner did not change their Pokémon.");
            return (offered, PokeTradeResult.TrainerTooSlow);
        }

        await SetBoxPokemonAbsolute(BoxStartOffset, clone, token, sav).ConfigureAwait(false);

        return (clone, PokeTradeResult.Success);
    }

    private async Task<bool> CheckCloneChangedOffer(CancellationToken token)
    {
        // Watch their status to indicate they canceled, then offered a new Pokémon.
        var hovering = await ReadUntilChanged(TradePartnerStatusOffset, [0x2], 25_000, 1_000, true, true, token).ConfigureAwait(false);
        if (!hovering)
        {
            Log("Trade partner did not change their initial offer.");
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return false;
        }
        var offering = await ReadUntilChanged(TradePartnerStatusOffset, [0x3], 25_000, 1_000, true, true, token).ConfigureAwait(false);
        if (!offering)
        {
            await ResetToLinkPlay(token).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    private async Task<(PA9 toSend, PokeTradeResult check)> HandleRandomLedy(SAV9ZA sav, PokeTradeDetail<PA9> poke, PA9 offered, PA9 toSend, PartnerDataHolder partner, CancellationToken token)
    {
        // Allow the trade partner to do a Ledy swap.
        var config = Hub.Config.Distribution;
        var trade = Hub.Ledy.GetLedyTrade(offered, partner.TrainerOnlineID, config.LedySpecies);
        if (trade != null)
        {
            if (trade.Type == LedyResponseType.AbuseDetected)
            {
                var msg = $"Found {partner.TrainerName} has been detected for abusing Ledy trades.";
                if (AbuseSettings.EchoNintendoOnlineIDLedy)
                    msg += $"\nID: {partner.TrainerOnlineID}";
                if (!string.IsNullOrWhiteSpace(AbuseSettings.LedyAbuseEchoMention))
                    msg = $"{AbuseSettings.LedyAbuseEchoMention} {msg}";
                EchoUtil.Echo(msg);

                return (toSend, PokeTradeResult.SuspiciousActivity);
            }

            toSend = trade.Receive;
            poke.TradeData = toSend;

            poke.SendNotification(this, "Injecting the requested Pokémon.");
            await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);
        }
        else if (config.LedyQuitIfNoMatch)
        {
            var nickname = offered.IsNicknamed ? $" (Nickname: \"{offered.Nickname}\")" : string.Empty;
            poke.SendNotification(this, $"No match found for the offered {GetSpeciesName(offered.Species)}{nickname}.");
            return (toSend, PokeTradeResult.TrainerRequestBad);
        }

        return (toSend, PokeTradeResult.Success);
    }

    private void WaitAtBarrierIfApplicable(CancellationToken token)
    {
        if (!ShouldWaitAtBarrier)
            return;
        var opt = Hub.Config.Distribution.SynchronizeBots;
        if (opt == BotSyncOption.NoSync)
            return;

        var timeoutAfter = Hub.Config.Distribution.SynchronizeTimeout;
        if (FailedBarrier == 1) // failed last iteration
            timeoutAfter *= 2; // try to re-sync in the event things are too slow.

        var result = Hub.BotSync.Barrier.SignalAndWait(TimeSpan.FromSeconds(timeoutAfter), token);

        if (result)
        {
            FailedBarrier = 0;
            return;
        }

        FailedBarrier++;
        Log($"Barrier sync timed out after {timeoutAfter} seconds. Continuing.");
    }

    private async Task<PokeTradeResult> PerformBatchTrade(SAV9ZA sav, PokeTradeDetail<PA9> poke, TradePartnerLZA tradePartner, TradePartnerStatusLZA tradePartnerInfo, CancellationToken token)
    {
        int completedTrades = 0;
        var originalTrainerID = poke.Trainer.ID;
        var tradesToProcess = poke.BatchTrades ?? [poke.TradeData];
        var totalBatchTrades = tradesToProcess.Count;

        var batchTracker = BatchTradeTracker<PA9>.Instance;

        void Cleanup()
        {
            var allReceived = batchTracker.GetReceivedPokemon(originalTrainerID);
            if (allReceived.Count > 0)
            {
                poke.SendNotification(this, $"Sending you the {allReceived.Count} Pokémon you traded to me before the interruption.");
                for (int j = 0; j < allReceived.Count; j++)
                {
                    var pokemon = allReceived[j];
                    var speciesName = SpeciesName.GetSpeciesName(pokemon.Species, 2);
                    poke.SendNotification(this, pokemon, $"Pokémon you traded to me: {speciesName}");
                    Thread.Sleep(500);
                }
            }
            batchTracker.ClearReceivedPokemon(originalTrainerID);
            batchTracker.ReleaseBatch(originalTrainerID, poke.UniqueTradeID);
            poke.IsProcessing = false;
            Hub.Queues.Info.Remove(new TradeEntry<PA9>(poke, originalTrainerID, PokeRoutineType.Batch, poke.Trainer.TrainerName, poke.UniqueTradeID));
        }

        var partnerHolder = new PartnerDataHolder(0, tradePartner.TrainerName, tradePartner.TID7);

        for (int i = 0; i < totalBatchTrades; i++)
        {
            var toSend = tradesToProcess[i];
            poke.TradeData = toSend;
            poke.Notifier.UpdateBatchProgress(i + 1, toSend, poke.UniqueTradeID);

            if (i == 0)
            {
                if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
                {
                    toSend = await ApplyAutoOT(toSend, tradePartnerInfo, sav, token).ConfigureAwait(false);
                    tradesToProcess[i] = toSend;
                    poke.TradeData = toSend;
                }

                if (toSend.Species != 0)
                    await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);

                // Pokemon was already placed in box before searching; notify user to offer.
                poke.SendNotification(this, $"Please offer your Pokémon for trade 1/{totalBatchTrades}.");
            }
            else
            {
                // Previous trade animation has finished; prepare next pokemon.
                poke.SendNotification(this, $"Trade {completedTrades} completed! **DO NOT OFFER YET** - Preparing your next Pokémon ({i + 1}/{totalBatchTrades})...");
                await Task.Delay(5_000, token).ConfigureAwait(false);
                if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
                {
                    toSend = await ApplyAutoOT(toSend, tradePartnerInfo, sav, token).ConfigureAwait(false);
                    tradesToProcess[i] = toSend;
                }
                if (toSend.Species != 0)
                    await SetBoxPokemonAbsolute(BoxStartOffset, toSend, token, sav).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
                poke.SendNotification(this, $"**Ready!** You can now offer your Pokémon for trade {i + 1}/{totalBatchTrades}.");
                await Task.Delay(3_000, token).ConfigureAwait(false);
            }

            // Wait for partner to offer a Pokémon.
            var offering = await ReadUntilChanged(TradePartnerStatusOffset, [0x3], 45_000, 1_000, true, true, token).ConfigureAwait(false);
            if (!offering)
            {
                poke.SendNotification(this, $"Trade partner took too long for trade {i + 1}/{totalBatchTrades}. Canceling the remaining trades.");
                Cleanup();
                await ResetToLinkPlay(token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }

            var offered = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 3_000, 0_050, BoxFormatSlotSize, token).ConfigureAwait(false);
            if (offered == null || offered.Species == 0 || !offered.ChecksumValid)
            {
                poke.SendNotification(this, $"Invalid Pokémon offered for trade {i + 1}/{totalBatchTrades}. Canceling the remaining trades.");
                Cleanup();
                await ResetToLinkPlay(token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }
            offered.Heal();
            offered.RefreshChecksum();

            PokeTradeResult update;
            (toSend, update) = await GetEntityToSend(sav, poke, offered, toSend, partnerHolder, token).ConfigureAwait(false);
            if (update != PokeTradeResult.Success)
            {
                poke.SendNotification(this, $"Update check failed for trade {i + 1}/{totalBatchTrades}. Canceling the remaining trades.");
                Cleanup();
                await ResetToLinkPlay(token).ConfigureAwait(false);
                return update;
            }

            Log($"Confirming trade {i + 1}/{totalBatchTrades}.");
            var tradeResult = await ConfirmAndStartTrading(poke, token).ConfigureAwait(false);
            if (tradeResult != PokeTradeResult.Success)
            {
                poke.SendNotification(this, $"Trade confirmation failed for trade {i + 1}/{totalBatchTrades}. Canceling the remaining trades.");
                Cleanup();
                await ResetToLinkPlay(token).ConfigureAwait(false);
                return tradeResult;
            }

            if (token.IsCancellationRequested)
            {
                poke.SendNotification(this, "Canceling the batch trades. The routine has been interrupted.");
                Cleanup();
                await ResetToOverworld(token).ConfigureAwait(false);
                return PokeTradeResult.RoutineCancel;
            }

            var received = await ReadPokemon(BoxStartOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
            if (SearchUtil.HashByDetails(received) == SearchUtil.HashByDetails(toSend) && received.Checksum == toSend.Checksum)
            {
                poke.SendNotification(this, $"Partner did not complete trade {i + 1}/{totalBatchTrades}. Canceling the remaining trades.");
                Cleanup();
                await ResetToLinkPlay(token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }

            UpdateCountsAndExport(poke, received, toSend);
            LogSuccessfulTrades(poke, tradePartner.NID, tradePartner.TrainerName);
            batchTracker.AddReceivedPokemon(originalTrainerID, received);
            completedTrades = i + 1;
            Log($"Batch trade {completedTrades}/{totalBatchTrades} complete.");

            if (completedTrades == totalBatchTrades)
            {
                var allReceived = batchTracker.GetReceivedPokemon(originalTrainerID);
                poke.SendNotification(this, "All batch trades completed! Thank you for trading!");

                if (Hub.Config.Discord.ReturnPKMs && allReceived.Count > 0)
                {
                    poke.SendNotification(this, $"Here are the {allReceived.Count} Pokémon you traded to me:");
                    for (int j = 0; j < allReceived.Count; j++)
                    {
                        var pokemon = allReceived[j];
                        var speciesName = SpeciesName.GetSpeciesName(pokemon.Species, 2);
                        poke.SendNotification(this, pokemon, $"Pokémon you traded to me: {speciesName}");
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                }

                poke.TradeFinished(this, allReceived.Count > 0 ? allReceived[^1] : received);
                Hub.Queues.CompleteTrade(this, poke);
                batchTracker.ClearReceivedPokemon(originalTrainerID);
                poke.IsProcessing = false;
                break;
            }
        }

        await ResetToLinkPlay(token).ConfigureAwait(false);
        return PokeTradeResult.Success;
    }

    /// <summary>
    /// Checks if the barrier needs to get updated to consider this bot.
    /// If it should be considered, it adds it to the barrier if it is not already added.
    /// If it should not be considered, it removes it from the barrier if not already removed.
    /// </summary>
    private void UpdateBarrier(bool shouldWait)
    {
        if (ShouldWaitAtBarrier == shouldWait)
            return; // no change required

        ShouldWaitAtBarrier = shouldWait;
        if (shouldWait)
        {
            Hub.BotSync.Barrier.AddParticipant();
            Log($"Joined the Barrier. Count: {Hub.BotSync.Barrier.ParticipantCount}");
        }
        else
        {
            Hub.BotSync.Barrier.RemoveParticipant();
            Log($"Left the Barrier. Count: {Hub.BotSync.Barrier.ParticipantCount}");
        }
    }
}
