using System.ComponentModel;

namespace SysBot.Pokemon;

public enum WebTradeMode
{
    [Description("Local — Next.js on localhost talks directly to PokeBot. No auth. For testing.")]
    Local,

    [Description("Supabase — Website writes to Supabase, PokeBot polls. Full auth + Realtime.")]
    Supabase,

    [Description("Tunnel — Website calls PokeBot through Cloudflare Tunnel with JWT auth.")]
    Tunnel,
}

public sealed class WebTradeSettings
{
    private const string WebTrade = nameof(WebTrade);
    private const string SupabaseConfig = nameof(SupabaseConfig);
    private const string TunnelConfig = nameof(TunnelConfig);
    private const string Limits = nameof(Limits);

    [Category(WebTrade), Description("Connection mode for the trading website.")]
    public WebTradeMode Mode { get; set; } = WebTradeMode.Local;

    [Category(WebTrade), Description("Enable web trade API endpoints. When disabled, only Discord trades work.")]
    public bool EnableWebTrades { get; set; } = true;

    [Category(WebTrade), Description("Allowed CORS origins, comma-separated. Used in Local and Tunnel modes. Not needed in Supabase mode (browser talks to Supabase directly).")]
    public string AllowedOrigins { get; set; } = "http://localhost:3000";

    [Category(SupabaseConfig), Description("Supabase project URL (e.g. https://xxxxx.supabase.co). Required for Supabase and Tunnel modes.")]
    public string SupabaseUrl { get; set; } = string.Empty;

    [Category(SupabaseConfig), Description("Supabase service_role key. Server-side only — never expose publicly. Required for Supabase and Tunnel modes.")]
    public string SupabaseServiceKey { get; set; } = string.Empty;

    [Category(SupabaseConfig), Description("Supabase JWT Secret. Required for Tunnel mode to validate user tokens.")]
    public string SupabaseJwtSecret { get; set; } = string.Empty;

    [Category(SupabaseConfig), Description("How often (seconds) to poll Supabase for pending trade requests. Supabase mode only. Minimum recommended: 3.")]
    public int SupabasePollIntervalSeconds { get; set; } = 5;

    [Category(TunnelConfig), Description("Public tunnel URL (e.g. https://pokebot.yourdomain.com). For reference only — set in cloudflared config.")]
    public string TunnelUrl { get; set; } = string.Empty;

    [Category(Limits), Description("Maximum pending trades per user.")]
    public int MaxPendingTradesPerUser { get; set; } = 3;

    [Category(Limits), Description("Cooldown in seconds between trade requests from the same user.")]
    public int CooldownSeconds { get; set; } = 30;

    [Category(Limits), Description("Minutes before a pending trade request expires. Supabase mode only.")]
    public int TradeRequestTtlMinutes { get; set; } = 15;

    public override string ToString() => $"Web Trading ({Mode})";
}
