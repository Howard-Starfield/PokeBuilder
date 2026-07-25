using Discord;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public sealed record DownloadedTradeAttachment(string Filename, Download<PKM> Download);

public sealed record BatchTradeAttachmentError(int Index, string Filename, string Error);

public sealed class BatchTradeAttachmentResult<T> where T : PKM, new()
{
    public IReadOnlyList<T> Pokemon { get; init; } = [];
    public IReadOnlyList<BatchTradeAttachmentError> Errors { get; init; } = [];
    public int NormalizedHandlerCount { get; init; }
    public bool IsValid => Errors.Count == 0 && Pokemon.Count > 0;
}

public static class BatchAttachmentHelpers<T> where T : PKM, new()
{
    public static async Task<BatchTradeAttachmentResult<T>> ProcessAsync(IEnumerable<IAttachment> attachments)
    {
        var downloads = new List<DownloadedTradeAttachment>();
        foreach (var attachment in attachments)
        {
            string filename = Format.Sanitize(attachment.Filename);
            Download<PKM> download;
            try
            {
                download = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
                filename = download.SanitizedFileName ?? filename;
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(ProcessAsync));
                download = new Download<PKM>
                {
                    SanitizedFileName = filename,
                    ErrorMessage = $"{filename}: Failed to download attachment.",
                    Success = false,
                };
            }
            downloads.Add(new DownloadedTradeAttachment(filename, download));
        }

        return ProcessDownloads(downloads, pk => TradeRequestValidator<T>.Validate(pk));
    }

    public static BatchTradeAttachmentResult<T> ProcessDownloads(
        IReadOnlyList<DownloadedTradeAttachment> downloads,
        Func<T, TradeRequestValidationResult<T>> validate)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(validate);

        var pokemon = new List<T>(downloads.Count);
        var errors = new List<BatchTradeAttachmentError>();
        int normalizedHandlerCount = 0;

        for (int i = 0; i < downloads.Count; i++)
        {
            var item = downloads[i];
            int index = i + 1;
            string filename = item.Download.SanitizedFileName ?? Format.Sanitize(item.Filename);

            if (!item.Download.Success)
            {
                errors.Add(new BatchTradeAttachmentError(index, filename,
                    item.Download.ErrorMessage ?? "Attachment download failed."));
                continue;
            }

            var pk = Helpers<T>.GetRequest(item.Download);
            if (pk is null)
            {
                errors.Add(new BatchTradeAttachmentError(index, filename,
                    "Attachment provided is not compatible with this module!"));
                continue;
            }

            TradeRequestValidationResult<T> validation;
            try
            {
                validation = validate(pk);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(ProcessDownloads));
                errors.Add(new BatchTradeAttachmentError(index, filename, "Failed to validate attachment."));
                continue;
            }
            if (!validation.IsValid)
            {
                errors.Add(new BatchTradeAttachmentError(index, filename,
                    validation.Error ?? "Attachment validation failed."));
                continue;
            }

            pokemon.Add(validation.Pokemon!);
            if (validation.HandlerNormalized)
                normalizedHandlerCount++;
        }

        return errors.Count == 0
            ? new BatchTradeAttachmentResult<T>
            {
                Pokemon = pokemon,
                NormalizedHandlerCount = normalizedHandlerCount,
            }
            : new BatchTradeAttachmentResult<T> { Errors = errors };
    }

    public static async Task SendErrorsAsync(SocketCommandContext context, IReadOnlyList<BatchTradeAttachmentError> errors)
    {
        const int MaxFieldsPerEmbed = 20;
        int pageCount = (int)Math.Ceiling(errors.Count / (double)MaxFieldsPerEmbed);

        for (int page = 0; page < pageCount; page++)
        {
            var embed = new EmbedBuilder()
                .WithTitle("Batch File Validation Failed")
                .WithColor(Color.Red)
                .WithDescription("No Pokémon were queued. Fix the listed files and submit the complete batch again.")
                .WithFooter(pageCount > 1 ? $"Error page {page + 1}/{pageCount}" : "Batch requests are all-or-nothing.");

            foreach (var error in errors.Skip(page * MaxFieldsPerEmbed).Take(MaxFieldsPerEmbed))
            {
                string fieldName = $"#{error.Index}: {error.Filename}";
                if (fieldName.Length > 256)
                    fieldName = fieldName[..253] + "...";
                string value = error.Error.Length > 1024 ? error.Error[..1021] + "..." : error.Error;
                embed.AddField(fieldName, value);
            }

            await context.Channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
        }
    }
}
