using PKHeX.Core;
using System;

namespace SysBot.Pokemon;

public sealed class RateLimitedTradeNotifier<T>(IPokeTradeNotifier<T> inner, string reservationId) : IPokeTradeNotifier<T>
    where T : PKM, new()
{
    private readonly IPokeTradeNotifier<T> _inner = inner;
    private readonly string _reservationId = reservationId;
    private Action<PokeRoutineExecutor<T>>? _onFinish;
    private int _countedCompletedTrades;

    public Action<PokeRoutineExecutor<T>>? OnFinish
    {
        set
        {
            _onFinish = value;
            _inner.OnFinish = routine => _onFinish?.Invoke(routine);
        }
    }

    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info) => _inner.TradeInitialize(routine, info);

    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info) => _inner.TradeSearching(routine, info);

    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
    {
        TradeRateLimitService.Instance.ReleaseReservation(_reservationId);
        _inner.TradeCanceled(routine, info, msg);
    }

    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
    {
        int totalTrades = info.TotalBatchTrades > 1 ? info.TotalBatchTrades : 1;
        int remainingTrades = totalTrades - _countedCompletedTrades;
        if (remainingTrades > 0)
        {
            TradeRateLimitService.Instance.ConsumeReservation(_reservationId, remainingTrades);
            _countedCompletedTrades += remainingTrades;
        }

        _inner.TradeFinished(routine, info, result);
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message) =>
        _inner.SendNotification(routine, info, message);

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message) =>
        _inner.SendNotification(routine, info, message);

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message) =>
        _inner.SendNotification(routine, info, result, message);

    public void UpdateBatchProgress(int currentBatchNumber, T currentPokemon, int uniqueTradeID)
    {
        int completedTrades = Math.Max(0, currentBatchNumber - 1);
        int delta = completedTrades - _countedCompletedTrades;
        if (delta > 0)
        {
            TradeRateLimitService.Instance.ConsumeReservation(_reservationId, delta);
            _countedCompletedTrades += delta;
        }

        _inner.UpdateBatchProgress(currentBatchNumber, currentPokemon, uniqueTradeID);
    }

    public void UpdateUniqueTradeID(int uniqueTradeID) => _inner.UpdateUniqueTradeID(uniqueTradeID);
}
