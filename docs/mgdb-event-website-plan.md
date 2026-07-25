# MGDB Event Pokemon Website Plan

## Goal

Expose event Pokemon on a website so users can browse available Mystery Gift events and request one for trade, without manually maintaining a large folder of generated `.pk*` files.

## Recommendation

Use MGDB/Wonder Card files as the source of truth for Mystery Gift events, then generate the Pokemon only when a user requests one.

Use a curated `.pk*` file catalog for event-like Pokemon that are not cleanly represented as Mystery Gift Wonder Cards, or where text generation does not reliably pick the exact origin. Pokemon Dream Radar legends are a good example: a Dream Ball Tornadus from White 2 is a special source, but it is not the same thing as an SV serial-code Wonder Card.

This is safer than pre-generating every `.pk*` file because event Pokemon can be sensitive to:

- language,
- game/version,
- fixed OT/TID/SID,
- fixed nickname,
- relearn moves,
- ribbons,
- fateful encounter flags,
- HOME tracker behavior,
- current PKHeX legality rules.

The Keldeo issue is the exact Mystery Gift example: the Wonder Card generated a legal Japanese event Keldeo, but changing its language to English made PKHeX unable to match it back to the Mystery Gift.

The Dream Ball Tornadus example is the opposite lesson: a known-good `.pk*` file can be more consistent than asking ALM to rediscover the exact old-game source from a plain text request.

## Existing Code Owners

- MGDB path setting:
  - `SysBot.Pokemon/Settings/LegalitySettings.cs`
  - property: `MGDBPath`
- MGDB load point:
  - `SysBot.Pokemon/Helpers/AutoLegalityWrapper.cs`
  - call: `EncounterEvent.RefreshMGDB(cfg.MGDBPath)`
- Existing Discord event browser/request flow:
  - `SysBot.Pokemon.Discord/Commands/Bots/SpecialRequestModule.cs`
  - list data owner: `GetEventData`
  - conversion owner: `ConvertEventToPKM`
- Existing prebuilt file request folder:
  - `Trade.RequestFolderSettings.EventsFolder`
  - Discord commands: `listevents` / `eventrequest`

## How To Know All Event Pokemon

Do not try to infer the list from filenames. Ask PKHeX after MGDB is loaded.

Use ProjectPokemon EventsGallery as the upstream event archive. Download the current repository contents, not only the old GitHub release package, and extract it as the `mgdb` folder used by SysBot/PKHeX. PKHeX also has built-in event database data, so the final list should come from PKHeX after `EncounterEvent.RefreshMGDB(...)` has run.

The existing code already maps game/generation names to PKHeX MGDB arrays:

```csharp
EncounterEvent.MGDB_G4
EncounterEvent.MGDB_G5
EncounterEvent.MGDB_G6
EncounterEvent.MGDB_G7
EncounterEvent.MGDB_G7GG
EncounterEvent.MGDB_G8
EncounterEvent.MGDB_G8A
EncounterEvent.MGDB_G8B
EncounterEvent.MGDB_G9
EncounterEvent.MGDB_G9A
```

Each entry is a `MysteryGift`. Filter to real Pokemon events:

```csharp
var events = EncounterEvent.GetAllEvents(false)
    .Where(gift => gift.IsEntity && !gift.IsItem)
    .ToList();
```

For each event, save enough metadata for the website to show variants clearly. A species name alone is not enough.

Example catalog fields:

```json
{
  "eventId": "sv:54:wc9:647:0:daisakusen:jpn",
  "sourceGroup": "sv",
  "sourceIndex": 54,
  "speciesId": 647,
  "speciesName": "Keldeo",
  "form": 0,
  "level": 50,
  "ot": "だいさくせん",
  "language": "Japanese",
  "version": "Scarlet/Violet",
  "cardTitle": "0054 SV - だいさくせん Keldeo",
  "kind": "mystery-gift"
}
```

The important part is `eventId`. The website must request the exact event id, not just `Keldeo`.

Why: the same Pokemon can have multiple valid sources, such as a normal encounter, an older transfer, a HOME gift, a serial-code Mystery Gift, or a region/language-specific campaign gift. They can all have different OT, TID, language, ribbons, relearn moves, fateful encounter flags, and legality rules.

## Event Requests vs Normal Requests

The website should separate these as different request types.

Normal generation request:

```json
{
  "requestType": "showdown",
  "game": "sv",
  "showdownSet": "Keldeo"
}
```

Event request:

```json
{
  "requestType": "mysteryGift",
  "game": "sv",
  "eventId": "sv:54:wc9:647:0:daisakusen:jpn",
  "language": "Japanese"
}
```

Curated PKM file request:

```json
{
  "requestType": "pkmFile",
  "game": "sv",
  "sourceId": "b2w2:dream-radar:tornadus:dream-ball:pk5"
}
```

For `requestType = "showdown"`, send the set through ALM like `$t`.

For `requestType = "mysteryGift"`, look up the selected `MysteryGift` from the event catalog and call `ConvertToPKM`. Do not let the user edit event-only fields like Classic Ribbon, Fateful Encounter, relearn moves, or fixed OT unless you revalidate afterward.

For `requestType = "pkmFile"`, load the curated `.pk*` file, convert it to the active bot format if needed, validate it, then queue it the same way Discord attachment trades do.

The website can still show both choices on a species page:

```text
Keldeo
- Generate legal transferable Keldeo
- Event: SV だいさくせん Keldeo
- Event: HOME Keldeo
- Event: GF Keldeo
- Event: WIN2013 Keldeo
```

```text
Tornadus
- Generate legal transferable Tornadus
- Curated file: White 2 Dream Radar Tornadus, Dream Ball
- Curated file: Black 2 Dream Radar Tornadus, Dream Ball
```

When the user clicks an event row, the website stores the row's `eventId` and sends that exact id back to the server.

For a website catalog, store/display fields like:

- `index` or stable generated id,
- game group, such as `gen9`, `sv`, `swsh`, `bdsp`,
- species id and species name,
- form,
- level,
- OT,
- TID/SID if useful,
- card title/header,
- language availability,
- shiny state,
- source Wonder Card filename if available,
- whether conversion validates for the target game.

## Website Flow

Recommended API shape:

```text
GET /events?game=sv
GET /events?game=sv&species=Keldeo
GET /pokemon/Keldeo/sources
POST /events/request
POST /pkm-files/request
```

Example request body:

```json
{
  "game": "sv",
  "eventId": "sv:54:wc9:647:0:daisakusen:jpn",
  "language": "Japanese",
  "tradeCode": 12345678
}
```

Server-side request flow:

1. Load MGDB on startup.
2. Build an event catalog from `EncounterEvent.MGDB_*` or `EncounterEvent.GetAllEvents(false)`.
3. User picks an event from the website.
4. Resolve `eventId` back to the selected `MysteryGift`.
5. Convert the selected `MysteryGift` with `ConvertToPKM`.
6. Convert to the active bot format if needed, such as `PK9` for Scarlet/Violet.
7. Run `LegalityAnalysis`.
8. If legal, queue the trade.
9. If illegal, return the legality report instead of queueing.

## Curated PKM File Catalog

Use this for sources that are reliable as files but difficult to regenerate from plain text, including:

- Pokemon Dream Radar transfers,
- old event Pokemon preserved as `.pk*` instead of Wonder Cards,
- special encounter edge cases where ALM chooses the wrong source,
- manually verified variants you want users to request exactly as-is.

Catalog every curated file with metadata:

```json
{
  "sourceId": "b2w2:dream-radar:tornadus:dream-ball:pk5",
  "requestType": "pkmFile",
  "speciesId": 641,
  "speciesName": "Tornadus",
  "originGame": "White 2",
  "sourceName": "Pokemon Dream Radar",
  "ball": "Dream Ball",
  "filePath": "events/curated/b2w2/dream-radar/tornadus-dream-ball.pk5",
  "targetGame": "sv",
  "notes": "Known-good legal source file; validate before queueing."
}
```

Server-side request flow:

1. Resolve `sourceId` to a local file path.
2. Load with `EntityFormat.GetFromBytes(...)`.
3. Convert to the target format if needed, such as `PK9` for SV.
4. Run `LegalityAnalysis`.
5. Run the normal trade checks, including `CanBeTraded`.
6. Queue only if valid.

This mirrors why Discord attachment trading works: the `.pk*` already carries the exact origin data, ball, met data, language, and other fields. The bot only has to validate and queue it.

## Conversion Sketch

```csharp
EncounterEvent.RefreshMGDB(config.Hub.Legality.MGDBPath);

var gifts = EncounterEvent.MGDB_G9
    .Where(gift => gift.IsEntity && !gift.IsItem)
    .ToArray();

var selected = gifts[eventIndex - 1];

var trainer = new SimpleTrainerInfo(selected.Version)
{
    Language = (byte)LanguageID.Japanese,
};

var pkm = selected.ConvertToPKM(trainer, EncounterCriteria.Unrestricted);

if (pkm is not PK9 pk9)
{
    pkm = EntityConverter.ConvertToType(pkm, typeof(PK9), out _);
    pk9 = pkm as PK9;
}

if (pk9 is null)
    throw new InvalidOperationException("Event is not compatible with SV.");

var legality = new LegalityAnalysis(pk9);
if (!legality.Valid)
    throw new InvalidOperationException(legality.Report());

// Queue pk9 for trade.
```

## Pre-Generating Files

Pre-generating `.pk*` files can work for a curated download/request folder, and it is useful for difficult old sources like Dream Radar. It should not replace MGDB for Wonder Cards, but it should exist beside MGDB as a separate source type.

The Discord behavior matches this:

- `$t` text generation asks ALM to infer the correct source from a lossy Showdown-style request.
- Attachment trade loads the concrete `.pk*` file and then validates it before queueing.
- The attachment path still runs `LegalityAnalysis`, `CanBeTraded`, and blocked-item checks before adding the trade.

So a prebuilt `.pk*` file is not automatically bypassing legality. It is more consistent because it preserves exact origin data that a text request may not carry. A Dream Ball Tornadus from White 2 is a good example: the concrete PKM can contain the exact transfer/source details, while `$t Tornadus @ Dream Ball` may not give ALM enough information to pick the same source.

If you do pre-generate:

- generate from MGDB, not from hand-edited Showdown sets;
- validate every generated file with `LegalityAnalysis`;
- preserve event language if changing language makes it illegal;
- generate per target game format, such as `.pk9` for SV;
- name files with enough metadata to distinguish variants;
- regenerate after PKHeX/MGDB updates;
- keep the original Wonder Card/event/source metadata next to the generated file.

Example filename format:

```text
SV_0054_Keldeo_OT-daisakusen_Lang-JPN.pk9
Gen5_W2_DreamRadar_Tornadus_DreamBall.pk9
```

Recommended website delivery model:

```json
{
  "eventId": "sv:54:wc9:647:0:daisakusen:jpn",
  "speciesName": "Keldeo",
  "deliveryMode": "mgdb-generate"
}
```

```json
{
  "eventId": "gen5:white2:dream-radar:tornadus:dream-ball",
  "speciesName": "Tornadus",
  "deliveryMode": "prebuilt-pk",
  "pkFilePath": "events/gen5/white2/Gen5_W2_DreamRadar_Tornadus_DreamBall.pk9",
  "sourceNotes": "White 2 Dream Radar source; text generation may not preserve the exact origin."
}
```

Use `mgdb-generate` for Wonder Card events that PKHeX can convert reliably.

Use `prebuilt-pk` for old sources, transfer edge cases, and curated Pokemon where a real PKM file is more reliable than asking ALM to infer the source from text.

## Reliability Notes

MGDB is reliable when:

- `MGDBPath` points to the correct MGDB root;
- SysBot is restarted after changing MGDB;
- the relevant `.wc*` file exists;
- generation preserves event-sensitive fields;
- PKHeX.Core and AutoMod are compatible;
- every generated Pokemon is validated before queueing.

MGDB is less reliable if the website stores stale generated files forever. For that reason, the website should list MGDB events and generate on request, with optional caching only after legality validation.


## Addendum: Curated PKM Library For Exact Historical Sources

A curated .pk* library is the more reliable path for source-specific historical Pokemon when ALM cannot reproduce the exact source from a short Showdown set. Example: Tornadus from White 2 / Dream Ball can be valid as an attached PKM file while generated text such as Tornadus plus Ball: Dream Ball fails to match an encounter.

Use three website request types: showdown for ordinary ALM generation, event for true Wonder Card / MGDB Mystery Gift rows, and pkm-template for curated exact-source files.

For pkm-template, the website sends a stable template id such as gen5-white2-dreamradar-tornadus-dreamball-pk9. The server resolves that id to a local .pk* file, clones it, applies only approved safe edits, runs LegalityAnalysis, and queues the trade only if it is still legal.

Operational rule: MGDB is good for Wonder Card events; curated PKM files are better for exact historical-source requests such as Dream Radar, Dream World, old transfer variants, or any Pokemon where the attached file validates but generated text does not. The website catalog should show MGDB event rows and curated PKM template rows as separate sources for the same species.
