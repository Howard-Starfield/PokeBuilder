using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Drawing.PokeSprite;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Color = Discord.Color;

namespace SysBot.Pokemon.Discord;

public class DiscordTradeNotifier<T> : IPokeTradeNotifier<T>, IDisposable
    where T : PKM, new()
{
    private T Data { get; set; }
    private PokeTradeTrainerInfo Info { get; }
    private int Code { get; }
    private List<Pictocodes> LGCode { get; }
    private SocketUser Trader { get; }
    private int BatchTradeNumber { get; set; }
    private int TotalBatchTrades { get; }
    private bool IsMysteryEgg { get; }

    private readonly ulong _traderID;
    private int _uniqueTradeID;
    private Timer? _periodicUpdateTimer;
    private const int PeriodicUpdateInterval = 60000;
    private bool _isTradeActive = true;
    private bool _initialUpdateSent;
    private bool _almostUpNotificationSent;
    private int _lastReportedPosition = -1;

    public readonly PokeTradeHub<T> Hub = SysCord<T>.Runner.Hub;

    public DiscordTradeNotifier(T data, PokeTradeTrainerInfo info, int code, SocketUser trader, int batchTradeNumber, int totalBatchTrades, bool isMysteryEgg, List<Pictocodes> lgcode)
    {
        Data = data;
        Info = info;
        Code = code;
        Trader = trader;
        BatchTradeNumber = batchTradeNumber;
        TotalBatchTrades = totalBatchTrades;
        IsMysteryEgg = isMysteryEgg;
        LGCode = lgcode;
        _traderID = trader.Id;
        _uniqueTradeID = GetUniqueTradeID();
    }

    public Action<PokeRoutineExecutor<T>>? OnFinish { private get; set; }

    public void UpdateBatchProgress(int currentBatchNumber, T currentPokemon, int uniqueTradeID)
    {
        BatchTradeNumber = currentBatchNumber;
        Data = currentPokemon;
        _uniqueTradeID = uniqueTradeID;
    }

    public void UpdateUniqueTradeID(int uniqueTradeID)
    {
        _uniqueTradeID = uniqueTradeID;
    }

    private int GetUniqueTradeID()
    {
        return (int)(DateTime.UtcNow.Ticks % int.MaxValue);
    }

    private PokeRoutineType GetQueueRoutineType() => TotalBatchTrades > 1 ? PokeRoutineType.Batch : PokeRoutineType.LinkTrade;

    private string GetFormattedTradeCode()
    {
        var codeText = Code > 9999 ? $"{Code:0000 0000}" : $"{Code:0000}";
        return $"**Trade Code**: {codeText}";
    }

    private void StartPeriodicUpdates()
    {
        _periodicUpdateTimer?.Dispose();
        _isTradeActive = true;

        _periodicUpdateTimer = new Timer(async _ =>
        {
            if (!_isTradeActive)
                return;

            var position = Hub.Queues.Info.CheckPosition(_traderID, _uniqueTradeID, GetQueueRoutineType());
            if (!position.InQueue)
                return;

            var currentPosition = position.Position < 1 ? 1 : position.Position;
            _lastReportedPosition = currentPosition;

            if (position.Detail == null)
                return;

            if (currentPosition == 1 && _initialUpdateSent && !_almostUpNotificationSent)
            {
                _almostUpNotificationSent = true;

                var batchInfo = TotalBatchTrades > 1
                    ? $"\n\nImportant: this batch contains {TotalBatchTrades} Pokemon. Stay in the trade until every trade is complete."
                    : string.Empty;

                var upNextEmbed = new EmbedBuilder
                {
                    Color = Color.Gold,
                    Title = "You're Up Next!",
                    Description = $"Your trade will begin very soon. Please be ready!{batchInfo}",
                    Footer = new EmbedFooterBuilder { Text = "Get ready to connect!" },
                    Timestamp = DateTimeOffset.Now
                }.Build();

                await Trader.SendDirectMessageAsync(embed: upNextEmbed).ConfigureAwait(false);
            }
        }, null, PeriodicUpdateInterval, PeriodicUpdateInterval);
    }

    private void StopPeriodicUpdates()
    {
        _isTradeActive = false;
        _periodicUpdateTimer?.Dispose();
        _periodicUpdateTimer = null;
    }

    public async Task SendInitialQueueUpdate()
    {
        var position = Hub.Queues.Info.CheckPosition(_traderID, _uniqueTradeID, GetQueueRoutineType());
        var currentPosition = position.Position < 1 ? 1 : position.Position;
        var botct = Hub.Bots.Count;
        var currentETA = currentPosition > botct ? Hub.Config.Queues.EstimateDelay(currentPosition, botct) : 0;

        _lastReportedPosition = currentPosition;

        var batchDescription = TotalBatchTrades > 1
            ? $"Your batch trade request ({TotalBatchTrades} Pokemon) has been queued.\n{GetFormattedTradeCode()}\n\nImportant instructions:\n- Stay in the trade for all {TotalBatchTrades} trades\n- Have all {TotalBatchTrades} Pokemon ready to trade\n- Do not exit until you see the completion message\n\nPosition in queue: **{currentPosition}**"
            : $"Your trade request has been queued.\n{GetFormattedTradeCode()}\n\nPosition in queue: **{currentPosition}**";

        var initialEmbed = new EmbedBuilder
        {
            Color = Color.Green,
            Title = TotalBatchTrades > 1 ? "Batch Trade Request Queued" : "Trade Request Queued",
            Description = batchDescription,
            Footer = new EmbedFooterBuilder
            {
                Text = $"Estimated wait time: {(currentETA > 0 ? $"{currentETA} minutes" : "Less than a minute")}"
            },
            Timestamp = DateTimeOffset.Now
        }.Build();

        try
        {
            var sent = await Trader.SendDirectMessageAsync(embed: initialEmbed).ConfigureAwait(false);
            if (!sent)
                return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception)
        {
            return;
        }

        _initialUpdateSent = true;
        StartPeriodicUpdates();
    }

    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        _uniqueTradeID = info.UniqueTradeID;
        StopPeriodicUpdates();
        _almostUpNotificationSent = true;

        int language = 2;
        var speciesName = IsMysteryEgg ? "Mystery Egg" : SpeciesName.GetSpeciesName(Data.Species, language);
        var receive = Data.Species == 0 ? string.Empty : (IsMysteryEgg ? string.Empty : $" ({Data.Nickname})");

        if (Data is PK9)
        {
            string message;
            if (TotalBatchTrades > 1)
            {
                if (BatchTradeNumber == 1)
                {
                    message = $"Starting your batch trade! Trading {TotalBatchTrades} Pokemon.\n\n**Trade 1/{TotalBatchTrades}**: {speciesName}{receive}\n\nIMPORTANT: stay in the trade until all {TotalBatchTrades} trades are completed.";
                }
                else
                {
                    message = $"Preparing trade {BatchTradeNumber}/{TotalBatchTrades}: {speciesName}{receive}";
                }
            }
            else
            {
                message = $"Initializing trade{receive}. Please be ready.";
            }

            _ = EmbedHelper.SendTradeInitializingEmbedAsync(Trader, speciesName, Code, IsMysteryEgg, message);
        }
        else if (Data is PB7)
        {
            var (thefile, lgcodeembed) = CreateLGLinkCodeSpriteEmbed(LGCode);
            _ = Trader.SendDirectFileAsync(thefile, $"Initializing trade{receive}. Please be ready. Your code is", lgcodeembed);
        }
        else
        {
            _ = EmbedHelper.SendTradeInitializingEmbedAsync(Trader, speciesName, Code, IsMysteryEgg);
        }
    }

    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
    {
        StopPeriodicUpdates();

        var name = Info.TrainerName;
        var trainer = string.IsNullOrEmpty(name) ? string.Empty : $" {name}";

        if (Data is PB7 && LGCode.Count != 0)
        {
            var batchInfo = TotalBatchTrades > 1 ? $" (Trade {BatchTradeNumber}/{TotalBatchTrades})" : string.Empty;
            var message = $"I'm waiting for you{trainer}{batchInfo}! My IGN is **{routine.InGameName}**.";
            _ = Trader.SendDirectMessageAsync(message);
        }
        else
        {
            string? additionalMessage = null;
            if (TotalBatchTrades > 1)
            {
                if (BatchTradeNumber == 1)
                {
                    additionalMessage = $"Starting batch trade ({TotalBatchTrades} Pokemon total). **Please select your first Pokemon!**";
                }
                else
                {
                    var speciesName = IsMysteryEgg ? "Mystery Egg" : SpeciesName.GetSpeciesName(Data.Species, 2);
                    additionalMessage = $"Trade {BatchTradeNumber}/{TotalBatchTrades}: Now trading {speciesName}. **Select your next Pokemon!**";
                }
            }

            _ = EmbedHelper.SendTradeSearchingEmbedAsync(Trader, trainer, routine.InGameName, additionalMessage);
        }
    }

    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
    {
        OnFinish?.Invoke(routine);
        StopPeriodicUpdates();

        var cancelMessage = TotalBatchTrades > 1
            ? $"Batch trade canceled: {msg}. All remaining trades have been canceled."
            : msg.ToString();

        _ = EmbedHelper.SendTradeCanceledEmbedAsync(Trader, cancelMessage);
    }

    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
    {
        if (TotalBatchTrades <= 1 || BatchTradeNumber == TotalBatchTrades)
        {
            OnFinish?.Invoke(routine);
            StopPeriodicUpdates();
        }

        var tradedToUser = Data.Species;

        string message;
        if (TotalBatchTrades > 1)
        {
            if (BatchTradeNumber == TotalBatchTrades)
            {
                message = $"All {TotalBatchTrades} trades completed successfully. Thank you for trading!";
            }
            else
            {
                var speciesName = IsMysteryEgg ? "Mystery Egg" : SpeciesName.GetSpeciesName(Data.Species, 2);
                message = $"Trade {BatchTradeNumber}/{TotalBatchTrades} completed! ({speciesName})\nPreparing trade {BatchTradeNumber + 1}/{TotalBatchTrades}...";
            }
        }
        else
        {
            message = tradedToUser != 0 ? "Trade finished. Enjoy!" : "Trade finished!";
        }

        _ = Trader.SendDirectMessageAsync(message);

        if (result is not null && Hub.Config.Discord.ReturnPKMs && TotalBatchTrades <= 1)
        {
            _ = Trader.SendPKMAsync(result, "Here's what you traded me!");
        }
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
    {
        if (TotalBatchTrades > 1 && !message.Contains("Trade") && !message.Contains("batch"))
        {
            message = $"Trade {BatchTradeNumber}/{TotalBatchTrades}: {message}";
        }

        _ = EmbedHelper.SendNotificationEmbedAsync(Trader, message);
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
    {
        if (message.ExtraInfo is SeedSearchResult r)
        {
            SendNotificationZ3(r);
            return;
        }

        var msg = message.Summary;
        if (message.Details.Count > 0)
            msg += ", " + string.Join(", ", message.Details.Select(z => $"{z.Heading}: {z.Detail}"));
        _ = Trader.SendDirectMessageAsync(msg);
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
    {
        if (result.Species != 0 && (Hub.Config.Discord.ReturnPKMs || info.Type == PokeTradeType.Dump))
        {
            _ = Trader.SendPKMAsync(result, message);
        }
    }

    private void SendNotificationZ3(SeedSearchResult r)
    {
        var lines = r.ToString();

        var embed = new EmbedBuilder { Color = Color.LighterGrey };
        embed.AddField(x =>
        {
            x.Name = $"Seed: {r.Seed:X16}";
            x.Value = lines;
            x.IsInline = false;
        });
        var msg = $"Here are the details for `{r.Seed:X16}`:";
        _ = Trader.SendDirectMessageAsync(msg, embed.Build());
    }

    public static (string, Embed) CreateLGLinkCodeSpriteEmbed(List<Pictocodes> lgcode)
    {
        List<System.Drawing.Image> spritearray = [];
        foreach (Pictocodes cd in lgcode)
        {
            var showdown = new ShowdownSet(cd.ToString());
            var sav = BlankSaveFile.Get(EntityContext.Gen7b, "pip");
            PKM pk = sav.GetLegalFromSet(showdown).Created;
            System.Drawing.Image png = pk.Sprite();
            var destRect = new Rectangle(-40, -65, 137, 130);
            var destImage = new Bitmap(137, 130);
            destImage.SetResolution(png.HorizontalResolution, png.VerticalResolution);
            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(png, destRect, 0, 0, png.Width, png.Height, GraphicsUnit.Pixel);
            }
            png = destImage;
            spritearray.Add(png);
        }

        int outputImageWidth = spritearray[0].Width + 20;
        int outputImageHeight = spritearray[0].Height - 65;
        Bitmap outputImage = new(outputImageWidth, outputImageHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(outputImage))
        {
            graphics.DrawImage(spritearray[0], new Rectangle(0, 0, spritearray[0].Width, spritearray[0].Height),
                new Rectangle(new Point(), spritearray[0].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[1], new Rectangle(50, 0, spritearray[1].Width, spritearray[1].Height),
                new Rectangle(new Point(), spritearray[1].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[2], new Rectangle(100, 0, spritearray[2].Width, spritearray[2].Height),
                new Rectangle(new Point(), spritearray[2].Size), GraphicsUnit.Pixel);
        }

        System.Drawing.Image finalembedpic = outputImage;
        var filename = $"{System.IO.Directory.GetCurrentDirectory()}//finalcode.png";
        finalembedpic.Save(filename);
        filename = System.IO.Path.GetFileName($"{System.IO.Directory.GetCurrentDirectory()}//finalcode.png");
        Embed returnembed = new EmbedBuilder().WithTitle($"{lgcode[0]}, {lgcode[1]}, {lgcode[2]}").WithImageUrl($"attachment://{filename}").Build();
        return (filename, returnembed);
    }

    public void Dispose()
    {
        StopPeriodicUpdates();
        GC.SuppressFinalize(this);
    }

    ~DiscordTradeNotifier()
    {
        Dispose();
    }
}
