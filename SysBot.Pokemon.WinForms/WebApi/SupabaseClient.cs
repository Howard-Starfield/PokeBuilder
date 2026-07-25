using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms.WebApi;

public class BatchItemRow
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("species")] public string Species { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "waiting";
}

public class TradeRequestRow
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("showdown_set")] public string ShowdownSet { get; set; } = "";
    [JsonPropertyName("game_version")] public string GameVersion { get; set; } = "SV";
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("link_code")] public int? LinkCode { get; set; }
    [JsonPropertyName("queue_position")] public int? QueuePosition { get; set; }
    [JsonPropertyName("result_message")] public string? ResultMessage { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    // Batch fields (0 / null = single trade)
    [JsonPropertyName("batch_total")] public int BatchTotal { get; set; }
    [JsonPropertyName("batch_current")] public int BatchCurrent { get; set; }
    [JsonPropertyName("batch_items")] public List<BatchItemRow>? BatchItems { get; set; }
}

public class TradeHistoryRow
{
    [JsonPropertyName("source")] public string Source { get; set; } = "web";
    [JsonPropertyName("user_id")] public string? UserId { get; set; }
    [JsonPropertyName("discord_id")] public string? DiscordId { get; set; }
    [JsonPropertyName("showdown_set")] public string ShowdownSet { get; set; } = "";
    [JsonPropertyName("game_version")] public string GameVersion { get; set; } = "SV";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("result_message")] public string? ResultMessage { get; set; }
}

public sealed class SupabaseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _restUrl;

    public SupabaseClient(string supabaseUrl, string serviceKey)
    {
        _restUrl = $"{supabaseUrl.TrimEnd('/')}/rest/v1";
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("apikey", serviceKey);
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {serviceKey}");
    }

    /// <summary>Get pending trade requests ordered by creation time.</summary>
    public async Task<List<TradeRequestRow>> GetPendingRequests(int limit = 10)
    {
        var url = $"{_restUrl}/trade_requests?status=eq.pending&order=created_at.asc&limit={limit}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<TradeRequestRow>>(json) ?? [];
    }

    /// <summary>
    /// Get trades stuck in an active non-pending state (processing/queued/searching/inprogress)
    /// that are older than the given TTL. These represent PokeBot disconnect scenarios.
    /// </summary>
    public async Task<List<TradeRequestRow>> GetStuckActiveRequests(int ttlMinutes)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-ttlMinutes).ToString("O");
        var statuses = "processing,queued,searching,inprogress";
        var url = $"{_restUrl}/trade_requests?status=in.({statuses})&created_at=lt.{cutoff}&order=created_at.asc&limit=20";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<TradeRequestRow>>(json) ?? [];
    }

    /// <summary>Update a trade request's fields by ID.</summary>
    public async Task UpdateTradeRequest(string id, object updates)
    {
        var json = JsonSerializer.Serialize(updates);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"{_restUrl}/trade_requests?id=eq.{id}")
        {
            Content = content,
        };
        request.Headers.Add("Prefer", "return=minimal");

        var resp = await _http.SendAsync(request);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Insert a completed trade into history.</summary>
    public async Task InsertTradeHistory(TradeHistoryRow row)
    {
        var json = JsonSerializer.Serialize(row);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_restUrl}/trade_history")
        {
            Content = content,
        };
        request.Headers.Add("Prefer", "return=minimal");

        var resp = await _http.SendAsync(request);
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
