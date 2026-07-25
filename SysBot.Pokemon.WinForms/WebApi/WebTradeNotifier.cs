using PKHeX.Core;
using SysBot.Pokemon;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms.WebApi;

/// <summary>
/// IPokeTradeNotifier that writes status updates to Supabase.
/// Used in Supabase mode when trades come from the website.
/// </summary>
public class WebTradeNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
{
    private readonly SupabaseClient _client;
    private readonly string _requestId;
    private readonly string _userId;
    private readonly string _showdownSet;
    private readonly string _gameVersion;

    public Action<PokeRoutineExecutor<T>>? OnFinish { get; set; }

    public WebTradeNotifier(SupabaseClient client, string requestId, string userId, string showdownSet, string gameVersion)
    {
        _client = client;
        _requestId = requestId;
        _userId = userId;
        _showdownSet = showdownSet;
        _gameVersion = gameVersion;
    }

    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        _ = UpdateSafe(new
        {
            status = "inprogress",
            link_code = info.Code,
        });
    }

    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        _ = UpdateSafe(new { status = "searching" });
    }

    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
    {
        _ = UpdateSafe(new { status = "canceled", result_message = msg.ToString() });
        _ = InsertHistory("canceled", msg.ToString());
        OnFinish?.Invoke(routine);
    }

    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
    {
        var message = $"Trade completed! You received {(Species)result.Species}.";
        _ = UpdateSafe(new { status = "completed", result_message = message });
        _ = InsertHistory("completed", message);
        OnFinish?.Invoke(routine);
    }

    public void UpdateBatchProgress(int currentBatchTradeNumber, T currentPokemon, int uniqueTradeID)
    {
        _ = UpdateSafe(new
        {
            status = "inprogress",
            result_message = $"Trading Pokémon {currentBatchTradeNumber}…",
            batch_current = currentBatchTradeNumber,
        });
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
    {
        _ = UpdateSafe(new { result_message = message });
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
    {
        _ = UpdateSafe(new { result_message = message.Summary });
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
    {
        _ = UpdateSafe(new { result_message = message });
    }

    private async Task UpdateSafe(object updates)
    {
        try { await _client.UpdateTradeRequest(_requestId, updates); }
        catch (Exception ex) { SysBot.Base.LogUtil.LogInfo("SupabaseUpdate", $"Failed to update trade {_requestId}: {ex.Message}"); }
    }

    private async Task InsertHistory(string status, string? resultMessage)
    {
        try
        {
            await _client.InsertTradeHistory(new TradeHistoryRow
            {
                Source = "web",
                UserId = _userId,
                ShowdownSet = _showdownSet,
                GameVersion = _gameVersion,
                Status = status,
                ResultMessage = resultMessage,
            });
        }
        catch (Exception ex) { SysBot.Base.LogUtil.LogInfo("SupabaseHistory", $"Failed to insert history: {ex.Message}"); }
    }
}
