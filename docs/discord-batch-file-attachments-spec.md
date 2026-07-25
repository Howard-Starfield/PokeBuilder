# Discord Batch File Attachments

## Title and Metadata

**Author:** Codex with Howard  
**Date:** 2026-07-22  
**Status:** Approved  
**Reviewer:** Howard  
**Target:** `SysBot.Pokemon.Discord`

## Context

The Discord `trade` command originally accepted one attached Pokemon file because its attachment helper selected only the first attachment. The bot already has a batch queue and game-specific batch executors. Multi-file support was added to `trade` / `t`, but the `batchTrade` / `bt` command still only exposes a required Showdown-text parameter. As a result, invoking `<prefix>bt` with attachments and no text is rejected by Discord.Net before the attachment handler runs with `The input text has too few parameters.`

Users need to attach multiple individual Pokemon files to one `trade` / `t` command and have the files processed as one ordered batch. The existing configurable Discord command prefix remains authoritative; examples such as `.t`, `$t`, and `!t` are the same command under different configurations.

The existing four-standard/six-favored hard ceilings are policy limits rather than game-memory limits. This feature changes the policy to a standard-user maximum of five and an operator-controlled favored/VIP/owner maximum that is unlimited by default.

## Functional Requirements

- FR-1: The system MUST continue to recognize `trade` and `t` through `DiscordSettings.CommandPrefix`; it MUST NOT hardcode `.` as the prefix.
- FR-2: A `trade` / `t` request with exactly one attachment MUST retain the existing single-file trade behavior.
- FR-3: A `trade` / `t` request with two or more attachments MUST treat the attachments as one batch request.
- FR-4: The batch trade order MUST match the Discord attachment enumeration order.
- FR-5: Every attachment MUST be downloaded, converted to the active bot Pokemon type, and validated before the batch is queued.
- FR-6: File validation MUST apply the existing tradeability, held-item, legality, and HOME-tracker policies used by single-file trades.
- FR-7: Batch validation MUST be atomic: if any attachment fails, the system MUST queue none of the attachments.
- FR-8: A batch validation error MUST identify the one-based attachment number, sanitized filename, and failure reason.
- FR-9: Standard users MUST be limited to at most five Pokemon per batch.
- FR-10: Favored, VIP/sudo, and owner users MUST be unlimited when `MaxPkmsPerTrade` is zero or negative.
- FR-11: When `MaxPkmsPerTrade` is positive, it MUST act as an operator cap for favored/VIP/owner users and as a stricter cap for standard users.
- FR-12: The effective standard limit MUST be `min(5, MaxPkmsPerTrade)` when the setting is positive, otherwise five.
- FR-13: The implementation MUST NOT add a separate hardcoded Discord attachment-count ceiling.
- FR-14: Role classification MUST use the existing `RequestSignificance` result from owner, sudo/VIP, and favored-role checks.
- FR-15: `AllowBatchTrades = false` MUST reject multi-attachment requests without changing one-attachment behavior.
- FR-16: An accepted file batch MUST create one batch queue entry, use one trade code, and reuse the existing batch executor.
- FR-17: Existing optional explicit trade-code and `ignoreAutoOT` arguments MUST behave the same for one-file and multi-file attachment requests.
- FR-18: The existing Showdown `batchTrade` / `bt` command MUST use the same role-aware batch-limit policy.
- FR-19: Showdown text and file attachments in the same `trade` request MUST NOT be combined into one batch.
- FR-20: `MaxPkmsPerTrade` MUST default to zero for newly created configurations, where zero means no operator cap for elevated users.
- FR-21: `batchTrade` / `bt` with one or more attachments and no Showdown text MUST route to the file-attachment batch path without a required text parameter.
- FR-22: `batchTrade` / `bt` attachment overloads MUST preserve optional explicit trade-code and `ignoreAutoOT` arguments.
- FR-23: The operator-facing `Allow Batch Trades` and `Maximum Pokémon per Batch` settings MUST appear in the top-level `Legality` settings section, with the maximum displayed immediately after the enable setting.
- FR-24: Existing configurations that store `AllowBatchTrades` and `MaxPkmsPerTrade` under `Trade.TradeConfiguration` MUST migrate those values into the new `Legality` location when the new fields are absent.
- FR-25: Saved configurations MUST keep legacy trade-location values synchronized for backward compatibility, while runtime enforcement uses the `Legality` values as authoritative.

## Non-Functional Requirements

- NFR-1 Reliability: An invalid or failed download MUST leave the queue unchanged.
- NFR-2 Security: Attachment bytes MUST continue through the existing filename sanitization and PKM size/type detection path.
- NFR-3 Compatibility: Existing single-file commands, explicit trade codes, AutoOT behavior, command aliases, and configurable prefixes MUST remain compatible.
- NFR-4 Maintainability: Single-file and batch-file trade validation MUST share one validation implementation so their safety rules cannot drift.
- NFR-5 Scalability: Processing MUST use collection sizes and loops without a fixed elevated-user batch array or save-slot allocation.
- NFR-6 Observability: User-visible errors MUST identify the failed attachment without exposing exception stack traces or download URLs.
- NFR-7 Compatibility: Existing Showdown-text `batchTrade` / `bt` requests and existing saved batch settings MUST continue to work after attachment overloads and settings relocation are introduced.

## Acceptance Criteria

### AC-1: Configurable prefix single-file regression (FR-1, FR-2, NFR-3)

Given the configured prefix is `!`, when a standard user sends `!t` with one valid file, then the existing single trade is queued.

### AC-2: Five-file standard batch (FR-3, FR-4, FR-16)

Given a standard user sends `<prefix>t` with five valid files, when validation succeeds, then one five-Pokemon batch is queued in attachment order.

### AC-3: Standard ceiling (FR-9, FR-12)

Given a standard user sends six valid files and `MaxPkmsPerTrade <= 0`, when the command is processed, then it is rejected with a maximum-of-five message before queue insertion.

### AC-4: Elevated unlimited default (FR-10, FR-13, FR-14)

Given a favored/VIP/owner user and `MaxPkmsPerTrade <= 0`, when the user attaches any number of files accepted by Discord, then the bot applies no additional numeric batch ceiling.

### AC-5: Operator cap (FR-11, FR-12)

Given `MaxPkmsPerTrade = 3`, when either a standard or elevated user submits four files, then the request is rejected with a maximum-of-three message.

### AC-6: Atomic validation (FR-5, FR-6, FR-7, FR-8, NFR-1)

Given attachment three is invalid, when a five-file request is processed, then no queue entry is created and the response identifies attachment three and its sanitized filename.

### AC-7: Batch disabled (FR-15)

Given batch trades are disabled, when a user sends two files, then the request is rejected; when the same user sends one file, then single-file processing remains available.

### AC-8: Explicit trade code (FR-17)

Given an explicit trade code and multiple valid files, when the batch is queued, then the queue entry uses that trade code.

### AC-9: Batch AutoOT opt-out (FR-17)

Given `ignoreAutoOT = true` and multiple valid files, when the batch is executed, then the batch detail preserves the ignore-AutoOT flag.

### AC-10: Shared text-batch limits (FR-18)

Given a text `batchTrade` request, when its count is evaluated, then it receives the same standard/elevated/configured limit decision as a file batch.

### AC-11: Mixed input is not merged (FR-19)

Given a `trade` request contains Showdown text and attachments, when overload resolution selects text input, then the existing text trade behavior remains authoritative and the attachments are not silently appended.

### AC-12: New configuration default (FR-20)

Given a newly constructed `LegalitySettings`, when its batch cap is read, then `MaxPkmsPerTrade` is zero.

### AC-13: `bt` attachment routing (FR-21, FR-22, NFR-7)

Given a user sends `<prefix>bt` with attachments and no Showdown text, when Discord.Net resolves the command, then an attachment-capable overload with no required text parameter is selected and the files enter the existing atomic batch path.

### AC-14: `bt` text compatibility (FR-21, NFR-7)

Given a user sends `<prefix>bt` with Showdown sets separated by `---`, when Discord.Net resolves the command, then the existing Showdown-text batch implementation remains authoritative.

### AC-15: Legality settings placement (FR-23)

Given the operator expands the top-level `Legality` settings section, when the `Generate` category is displayed, then `Allow Batch Trades` is followed by `Maximum Pokémon per Batch`, and both values are editable there.

### AC-16: Legacy setting migration (FR-24, FR-25, NFR-7)

Given a saved configuration contains batch settings only under `Trade.TradeConfiguration`, when the configuration is loaded, then the values are copied to `Legality`, runtime enforcement uses the copied values, and the compatibility fields are synchronized on save.

## Edge Cases

- EC-1: No attachment is present for an attachment overload: return the existing no-attachment error.
- EC-2: An attachment download fails: abort the complete batch and identify the attachment.
- EC-3: An attachment has an invalid PKM size or format: abort the complete batch.
- EC-4: A file converts to a Pokemon type incompatible with the active bot: abort the complete batch.
- EC-5: A file is illegal, trade-blocked, holds a blocked item, or violates the configured HOME-tracker policy: abort the complete batch.
- EC-6: The user is already in the queue: do not replace or append to the existing request.
- EC-7: The operator cap changes while a request is processing: use the value captured when command processing begins.
- EC-8: Discord supplies more files than usual: process them for an elevated user when the operator cap is unlimited; do not impose a local Discord-count constant.
- EC-9: The user disconnects during execution: retain the existing game-specific batch cancellation behavior.
- EC-10: `MaxPkmsPerTrade` is negative: treat it the same as zero for limit evaluation.
- EC-11: `batchTrade` / `bt` is invoked with neither attachments nor Showdown text: return the existing no-attachment error instead of a parser parameter-count error.
- EC-12: `batchTrade` / `bt` is invoked with both text and attachments: preserve the text-batch behavior and do not merge inputs.
- EC-13: A configuration already contains the new `Legality` batch fields: do not overwrite them from legacy trade-location values.

## API Contracts

External transport: Discord Create Message (`POST /channels/{channel.id}/messages`). The bot consumes the resulting message event through Discord.Net; it does not expose a new HTTP endpoint.

```ts
type BatchUserTier = "standard" | "elevated";

interface BatchLimitPolicy {
  tier: BatchUserTier;
  operatorCap: number; // <= 0 means no operator cap
  maximum: number | null; // null means unlimited
}

interface BatchAttachmentInput {
  index: number; // one-based
  filename: string;
  attachment: IAttachment;
}

interface BatchAttachmentResult<TPokemon> {
  index: number;
  filename: string;
  pokemon?: TPokemon;
  error?: string;
}

interface BatchAttachmentProcessingResult<TPokemon> {
  pokemon: TPokemon[];
  errors: BatchAttachmentResult<TPokemon>[];
  isValid: boolean;
}
```

Command contract:

```text
<prefix>trade [tradeCode] [ignoreAutoOT] + one file  -> single queue entry
<prefix>trade [tradeCode] [ignoreAutoOT] + 2+ files -> batch queue entry
<prefix>t     [tradeCode] [ignoreAutoOT] + files    -> same behavior
<prefix>batchTrade <Showdown sets separated by ---> -> existing text batch
<prefix>batchTrade [tradeCode] [ignoreAutoOT] + files -> attachment batch
<prefix>bt         [tradeCode] [ignoreAutoOT] + files -> attachment batch
```

## Data Models

| Model | Field | Type | Constraints |
|---|---|---|---|
| Batch limit policy | Tier | enum | Standard or elevated |
| Batch limit policy | Operator cap | integer | `<= 0` means uncapped |
| Batch limit policy | Maximum | nullable integer | `null` means unlimited |
| Legality settings | AllowBatchTrades | boolean | Authoritative runtime batch enable |
| Legality settings | MaxPkmsPerTrade | integer | `<= 0` means unlimited elevated; standard remains at most five |
| Attachment result | Index | integer | One-based and preserves Discord order |
| Attachment result | Filename | string | Sanitized before display |
| Attachment result | Pokemon | active `T` | Present only on success |
| Attachment result | Error | string | Present only on failure |
| Processing result | Pokemon | ordered list | Empty when atomic validation fails |
| Processing result | Errors | list | Contains every detected attachment failure |

The JSON configuration adds authoritative `Legality.AllowBatchTrades` and `Legality.MaxPkmsPerTrade` fields. Existing `Trade.TradeConfiguration` values are retained as synchronized compatibility fields and migrate into `Legality` only when the new fields are absent.

## Out of Scope

- OS-1: ZIP, RAR, or other archive expansion is not included because the requested interface is multiple Discord file attachments.
- OS-2: Combining attachments from multiple Discord messages into one batch is not included.
- OS-3: Adding new Switch offsets, save pointers, or box-slot allocation is not included because current batch executors reuse one outgoing slot.
- OS-4: Rewriting game-specific batch executor timing or recovery logic is not included.
- OS-5: Changing Discord's own upload-count or upload-size limits is not possible and is not emulated locally.
