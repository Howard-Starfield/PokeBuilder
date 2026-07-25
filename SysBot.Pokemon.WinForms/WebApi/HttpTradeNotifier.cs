using PKHeX.Core;
using SysBot.Pokemon;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SysBot.Pokemon.WinForms.WebApi;

public enum HttpTradeStatus { Queued, Searching, InProgress, Completed, Failed, Canceled }

public class HttpTradeRecord
{
    public string TradeId { get; init; } = Guid.NewGuid().ToString();
    public string DiscordId { get; init; } = string.Empty;
    public int TradeCode { get; set; }
    public int QueuePosition { get; set; }
    public HttpTradeStatus Status { get; set; } = HttpTradeStatus.Queued;
    public string ResultMessage { get; set; } = string.Empty;
    public Action? CancelAction { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Batch fields (0 / null = single trade)
    public int BatchTotal { get; set; }
    public int BatchCurrent { get; set; }
    public List<string> BatchSpecies { get; set; } = [];
}

/// <summary>Non-generic registry shared across all game-type notifiers.</summary>
public static class HttpTradeRegistry
{
    public static readonly ConcurrentDictionary<string, HttpTradeRecord> ActiveTrades = new();
}

public class HttpTradeNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
{
    private readonly HttpTradeRecord _record;

    public Action<PokeRoutineExecutor<T>>? OnFinish { get; set; }

    public HttpTradeNotifier(HttpTradeRecord record)
    {
        _record = record;
        HttpTradeRegistry.ActiveTrades[record.TradeId] = record;
    }

    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        _record.Status = HttpTradeStatus.InProgress;
        _record.TradeCode = info.Code;
    }

    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        _record.Status = HttpTradeStatus.Searching;
    }

    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
    {
        _record.Status = HttpTradeStatus.Canceled;
        _record.ResultMessage = msg.ToString();
        OnFinish?.Invoke(routine);
        CleanupLater(_record.TradeId);
    }

    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
    {
        _record.Status = HttpTradeStatus.Completed;
        _record.ResultMessage = $"Trade completed! You received {(Species)result.Species}.";
        OnFinish?.Invoke(routine);
        CleanupLater(_record.TradeId);
    }

    public void UpdateBatchProgress(int currentBatchTradeNumber, T currentPokemon, int uniqueTradeID)
    {
        _record.BatchCurrent = currentBatchTradeNumber;
        _record.Status = HttpTradeStatus.InProgress;
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
    {
        _record.ResultMessage = message;
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
    {
        _record.ResultMessage = message.Summary;
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
    {
        _record.ResultMessage = message;
    }

    private static void CleanupLater(string tradeId)
    {
        System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(5))
            .ContinueWith(t => { HttpTradeRegistry.ActiveTrades.TryRemove(tradeId, out _); });
    }
}
