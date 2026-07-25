using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon;

public sealed class TradeRateLimitDecision
{
    public bool Allowed { get; init; }
    public string? ReservationId { get; init; }
    public int Limit { get; init; }
    public int UsedCount { get; init; }
    public int PendingCount { get; init; }
    public int RequestedCount { get; init; }
    public int WindowMinutes { get; init; }
    public long? RetryAtUnixSeconds { get; init; }
    public string FailureReason { get; init; } = string.Empty;
}

public sealed class TradeRateLimitService
{
    public const string ReservationContextKey = "TradeRateLimitReservationId";

    private sealed class ReservationRecord
    {
        public ulong UserId { get; init; }
        public int RemainingCount { get; set; }
    }

    private sealed class UserUsage
    {
        public List<DateTimeOffset> CompletedTrades { get; } = [];
        public HashSet<string> ReservationIds { get; } = [];
    }

    private readonly object _sync = new();
    private readonly Dictionary<ulong, UserUsage> _usageByUser = [];
    private readonly Dictionary<string, ReservationRecord> _reservations = [];

    public static TradeRateLimitService Instance { get; } = new();

    public TradeRateLimitDecision TryReserve(ulong userId, int requestedCount, int limit, int windowMinutes)
    {
        lock (_sync)
        {
            var usage = GetOrCreateUsage(userId);
            PruneCompletedTrades(usage, windowMinutes);

            int usedCount = usage.CompletedTrades.Count;
            int pendingCount = usage.ReservationIds
                .Where(_reservations.ContainsKey)
                .Sum(id => _reservations[id].RemainingCount);

            if (requestedCount > limit)
            {
                return new TradeRateLimitDecision
                {
                    Allowed = false,
                    Limit = limit,
                    UsedCount = usedCount,
                    PendingCount = pendingCount,
                    RequestedCount = requestedCount,
                    WindowMinutes = windowMinutes,
                    FailureReason = "request_exceeds_limit",
                };
            }

            int blockedSlots = (usedCount + pendingCount + requestedCount) - limit;
            if (blockedSlots > 0)
            {
                long? retryAt = null;
                if (pendingCount == 0 && blockedSlots <= usedCount)
                {
                    var completionToExpire = usage.CompletedTrades
                        .OrderBy(t => t)
                        .ElementAt(blockedSlots - 1);
                    retryAt = completionToExpire.AddMinutes(windowMinutes).ToUnixTimeSeconds();
                }

                return new TradeRateLimitDecision
                {
                    Allowed = false,
                    Limit = limit,
                    UsedCount = usedCount,
                    PendingCount = pendingCount,
                    RequestedCount = requestedCount,
                    WindowMinutes = windowMinutes,
                    RetryAtUnixSeconds = retryAt,
                    FailureReason = pendingCount > 0 ? "active_reservations" : "window_limit_reached",
                };
            }

            string reservationId = Guid.NewGuid().ToString("N");
            _reservations[reservationId] = new ReservationRecord
            {
                UserId = userId,
                RemainingCount = requestedCount,
            };
            usage.ReservationIds.Add(reservationId);

            return new TradeRateLimitDecision
            {
                Allowed = true,
                ReservationId = reservationId,
                Limit = limit,
                UsedCount = usedCount,
                PendingCount = pendingCount,
                RequestedCount = requestedCount,
                WindowMinutes = windowMinutes,
            };
        }
    }

    public void ConsumeReservation(string reservationId, int completedCount)
    {
        if (completedCount <= 0)
            return;

        lock (_sync)
        {
            if (!_reservations.TryGetValue(reservationId, out var reservation))
                return;

            var usage = GetOrCreateUsage(reservation.UserId);
            int countToConsume = Math.Min(completedCount, reservation.RemainingCount);
            var now = DateTimeOffset.UtcNow;
            for (int i = 0; i < countToConsume; i++)
                usage.CompletedTrades.Add(now);

            reservation.RemainingCount -= countToConsume;
            if (reservation.RemainingCount <= 0)
                RemoveReservationInternal(reservationId, reservation.UserId);
        }
    }

    public void ReleaseReservation(string reservationId)
    {
        lock (_sync)
        {
            if (_reservations.TryGetValue(reservationId, out var reservation))
                RemoveReservationInternal(reservationId, reservation.UserId);
        }
    }

    public void ReleaseReservation<T>(PokeTradeDetail<T> detail) where T : PKM, new()
    {
        if (detail.Context.TryGetValue(ReservationContextKey, out var reservationObj) && reservationObj is string reservationId)
            ReleaseReservation(reservationId);
    }

    public void AttachReservation<T>(PokeTradeDetail<T> detail, string reservationId) where T : PKM, new()
    {
        detail.Context[ReservationContextKey] = reservationId;
    }

    private UserUsage GetOrCreateUsage(ulong userId)
    {
        if (!_usageByUser.TryGetValue(userId, out var usage))
        {
            usage = new UserUsage();
            _usageByUser[userId] = usage;
        }

        return usage;
    }

    private static void PruneCompletedTrades(UserUsage usage, int windowMinutes)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-windowMinutes);
        usage.CompletedTrades.RemoveAll(t => t <= cutoff);
    }

    private void RemoveReservationInternal(string reservationId, ulong userId)
    {
        _reservations.Remove(reservationId);

        if (_usageByUser.TryGetValue(userId, out var usage))
        {
            usage.ReservationIds.Remove(reservationId);
            if (usage.CompletedTrades.Count == 0 && usage.ReservationIds.Count == 0)
                _usageByUser.Remove(userId);
        }
    }
}
