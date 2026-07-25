using System;
using System.Collections.Concurrent;

namespace SysBot.Pokemon.WinForms.WebApi;

/// <summary>
/// Stores per-trade cancel actions for Supabase-mode trades, keyed by Supabase request UUID.
/// </summary>
public static class WebTradeRegistry
{
    public static readonly ConcurrentDictionary<string, Action> CancelActions = new();
}
