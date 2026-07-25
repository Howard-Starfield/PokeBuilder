# Pitfall Report: Why Task 8/9/11 Took So Long

## The Core Problem

**Task 8 (Core trade system updates) ported SysBot.NET's simpler types and overwrote PokeBot's extended versions.**

PokeBot is not a plain fork of SysBot.NET — it's a heavily customized extension with features like batch trades, mystery eggs, medal achievements, LGPE pictocodes, recovery systems, and rich Discord embeds. When the SysBot.NET backport copied over "updated" files, it replaced PokeBot's richer types with SysBot.NET's barebones ones.

## What Happened Step by Step

1. **Task 8 copied SysBot.NET's `TradeSettings.cs`** — but PokeBot's version had nested category classes (`TradeSettingsCategory`, `CountStatsSettingsCategory`) with properties like `TradeWaitTime`, `MaxDumpsPerTrade`, `AddCompletedTrade()`, etc. The game-specific trade bots (SV, SWSH, BDSP, LA, LGPE, LZA) all call these directly on `TradeSettings`. After the overwrite, those properties still existed but were buried in nested classes, breaking every trade bot.

2. **Task 8 copied SysBot.NET's `TradeEntry.cs`** — but PokeBot's version had a 5th constructor parameter (`UniqueTradeID`) and batch trade properties. Every queue operation broke.

3. **Task 8 copied SysBot.NET's `QueueCheckResult.cs`** — missing `BatchTradeNumber` and `TotalBatchTrades` fields that PokeBot's queue system relies on.

4. **Task 8 copied SysBot.NET's `IPokeTradeNotifier.cs`** — missing `UpdateBatchProgress()` method that PokeBot's Discord notifier implements.

5. **Multiple settings files were simplified** — `LegalitySettings` lost `UseTradePartnerInfo`, `DisallowNonNatives`, `DisallowTracked`. `DistributionSettings` lost `LGPECode1/2/3`. `TimingSettings` lost `CheckGameDelay`, `ProfileSelectionRequired`. `QueueResultAdd` lost `NotAllowedItem`, `QueueFull`. `PokeTradeResult` lost `UserCanceled`.

6. **NLog 6.x breaking changes went unnoticed** — `ConcurrentWrites`, `ArchiveNumberingMode`, and `ArchiveDateFormat` were removed/renamed in NLog 6.0 but PokeBot's enhanced `LogUtil.cs` still used them.

7. **LZA bots were new (added in Task 5/6)** but missed `RebootAndStop()` override that the base class requires.

8. **PKHeX v26 API changes** — `InventoryPouch8` constructor parameter order changed, `GetSpeciesName` was expected as an instance method but needed to be static, `CanBeTraded` now requires an `IEncounterTemplate` argument, `BatchEditing` class replaced `EntityBatchEditor.Instance`.

## Why It Was So Time-Consuming

### 1. Cascading failures
One bad overwrite (e.g., `TradeSettings.cs`) caused errors in 6+ game-specific trade bots, the queue system, Discord module, Twitch module, and WinForms — dozens of files referencing the same missing properties.

### 2. No compilation check between tasks
Tasks 5-8 were committed without verifying the solution compiled. Errors accumulated silently across tasks, making it impossible to tell which task introduced which break.

### 3. Diffing complexity
Every shared file between SysBot.NET and PokeBot had differences in BOTH directions — SysBot.NET had newer base APIs while PokeBot had custom extensions. You can't just copy either direction; you have to merge intelligently. The diff output was thousands of lines across 35+ files.

### 4. Two-layer problem
The fix wasn't just "add missing properties." It required understanding:
- What SysBot.NET changed (new APIs, renamed types)
- What PokeBot added (custom features, extended types)
- How to reconcile both without breaking either side

## Lessons for Future Tasks

1. **Build after every task** — run `dotnet build` and fix errors before committing.
2. **Never blindly copy settings/type files** from SysBot.NET — always diff first and merge PokeBot's extensions.
3. **PokeBot's custom features are in the types, not just the modules** — batch trades, medals, LGPE codes, etc. are woven into `TradeEntry`, `TradeSettings`, `QueueCheckResult`, and `IPokeTradeNotifier`.
4. **Check the nested class pattern** — PokeBot uses `TradeSettings.TradeConfiguration.X` but trade bots call `TradeSettings.X` via pass-through properties. Both must exist.
5. **Test the full solution, not just the project you changed** — Discord, Twitch, WinForms, and Tests all depend on core types.
