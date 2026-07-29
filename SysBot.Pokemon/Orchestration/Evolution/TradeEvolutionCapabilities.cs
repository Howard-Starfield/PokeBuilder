using System.Collections.Generic;

namespace SysBot.Pokemon;

public sealed record TradeEvolutionCapability(
    ProgramMode GameMode,
    bool OrdinaryPreConfirmDetection,
    bool BatchPreConfirmDetection,
    bool EvolutionAnimationHandled,
    bool MoveLearningHandled,
    bool NativeSwitchValidated,
    string Evidence)
{
    public bool AllowsManual =>
        OrdinaryPreConfirmDetection &&
        BatchPreConfirmDetection &&
        NativeSwitchValidated;

    public bool AllowsAutomaticHandling =>
        AllowsManual &&
        EvolutionAnimationHandled &&
        MoveLearningHandled;
}

public interface ITradeEvolutionCapabilityRegistry
{
    TradeEvolutionCapability Get(ProgramMode mode);

    bool Supports(ProgramMode mode, TradeEvolutionPolicy policy);
}

/// <summary>
/// Fail-closed capability evidence for trade evolutions. A scene constant or
/// late EC-change check is recorded as evidence, not treated as a completed
/// handler or native validation.
/// </summary>
public sealed class TradeEvolutionCapabilityRegistry :
    ITradeEvolutionCapabilityRegistry
{
    private static readonly IReadOnlyDictionary<
        ProgramMode,
        TradeEvolutionCapability> Capabilities =
        new Dictionary<ProgramMode, TradeEvolutionCapability>
        {
            [ProgramMode.SWSH] = new(
                ProgramMode.SWSH,
                OrdinaryPreConfirmDetection: true,
                BatchPreConfirmDetection: true,
                EvolutionAnimationHandled: false,
                MoveLearningHandled: false,
                NativeSwitchValidated: false,
                "Control-plane ordinary and batch confirmations re-read the offered Pokémon and apply TradeEvolutions before the first confirmation input; animation handling is not proven."),
            [ProgramMode.BDSP] = new(
                ProgramMode.BDSP,
                OrdinaryPreConfirmDetection: true,
                BatchPreConfirmDetection: true,
                EvolutionAnimationHandled: false,
                MoveLearningHandled: false,
                NativeSwitchValidated: false,
                "Control-plane confirmation now performs a pre-confirm offered-Pokémon guard. An evolution scene ID exists, but no complete animation or move-learning flow is proven."),
            [ProgramMode.LA] = new(
                ProgramMode.LA,
                OrdinaryPreConfirmDetection: true,
                BatchPreConfirmDetection: true,
                EvolutionAnimationHandled: false,
                MoveLearningHandled: false,
                NativeSwitchValidated: false,
                "Control-plane ordinary and batch confirmations perform a pre-confirm offered-Pokémon guard; no verified animation handler exists."),
            [ProgramMode.SV] = new(
                ProgramMode.SV,
                OrdinaryPreConfirmDetection: true,
                BatchPreConfirmDetection: true,
                EvolutionAnimationHandled: false,
                MoveLearningHandled: false,
                NativeSwitchValidated: false,
                "Control-plane confirmation performs a pre-confirm guard. The observed scene value remains broader than a unique evolution window."),
            [ProgramMode.LGPE] = new(
                ProgramMode.LGPE,
                OrdinaryPreConfirmDetection: true,
                BatchPreConfirmDetection: true,
                EvolutionAnimationHandled: false,
                MoveLearningHandled: false,
                NativeSwitchValidated: false,
                "Control-plane confirmation now reads the offered PB7 and applies the shared evolution table before input; no evolution-state handler is verified."),
            [ProgramMode.LZA] = new(
                ProgramMode.LZA,
                OrdinaryPreConfirmDetection: true,
                BatchPreConfirmDetection: true,
                EvolutionAnimationHandled: false,
                MoveLearningHandled: false,
                NativeSwitchValidated: false,
                "Control-plane confirmation performs a pre-confirm guard; MenuState still has no evolution-specific state or animation handler."),
        };

    public TradeEvolutionCapability Get(ProgramMode mode) =>
        Capabilities.TryGetValue(mode, out var capability)
            ? capability
            : new(
                mode,
                false,
                false,
                false,
                false,
                false,
                "Unsupported or unknown game mode.");

    public bool Supports(
        ProgramMode mode,
        TradeEvolutionPolicy policy)
    {
        if (policy == TradeEvolutionPolicy.Block)
            return true;
        var capability = Get(mode);
        return policy switch
        {
            TradeEvolutionPolicy.AllowManual => capability.AllowsManual,
            TradeEvolutionPolicy.AllowAndHandle =>
                capability.AllowsAutomaticHandling,
            _ => false,
        };
    }
}
