using PKHeX.Core;

namespace SysBot.Pokemon;

public static class TradeControlContextKeys
{
    public const string EvolutionPolicy = "control_plane_evolution_policy";
}

public static class TradeControlDetailExtensions
{
    public static bool RequiresControlPlaneEvolutionBlock<T>(
        this PokeTradeDetail<T> detail)
        where T : PKM, new() =>
        detail.Context.TryGetValue(
            TradeControlContextKeys.EvolutionPolicy,
            out var value) &&
        value is string policy &&
        policy == TradeEvolutionPolicy.Block.ToString();
}
