using System;
using System.ComponentModel;
using System.Globalization;

namespace SysBot.Pokemon.Helpers;

/// <summary>
/// Parses standard five-field cron expressions and finds their next local-time occurrence.
/// </summary>
public sealed class CronSchedule
{
    private const int MaximumSearchMinutes = 5 * 366 * 24 * 60;

    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _daysOfMonth;
    private readonly bool[] _months;
    private readonly bool[] _daysOfWeek;
    private readonly bool _dayOfMonthWildcard;
    private readonly bool _dayOfWeekWildcard;

    private CronSchedule(
        bool[] minutes,
        bool[] hours,
        bool[] daysOfMonth,
        bool[] months,
        bool[] daysOfWeek,
        bool dayOfMonthWildcard,
        bool dayOfWeekWildcard)
    {
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _dayOfMonthWildcard = dayOfMonthWildcard;
        _dayOfWeekWildcard = dayOfWeekWildcard;
    }

    public static CronSchedule Parse(string expression)
    {
        if (TryParse(expression, out var schedule, out var error))
            return schedule!;

        throw new FormatException(error);
    }

    public static bool TryParse(
        string? expression,
        out CronSchedule? schedule,
        out string? error)
    {
        schedule = null;
        error = null;

        var fields = expression?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields is not { Length: 5 })
        {
            error = "Cron schedules require five fields: minute hour day-of-month month day-of-week.";
            return false;
        }

        if (!TryParseField(fields[0], 0, 59, normalizeSunday: false, "minute", out var minutes, out _, out error) ||
            !TryParseField(fields[1], 0, 23, normalizeSunday: false, "hour", out var hours, out _, out error) ||
            !TryParseField(fields[2], 1, 31, normalizeSunday: false, "day-of-month", out var daysOfMonth, out var dayOfMonthWildcard, out error) ||
            !TryParseField(fields[3], 1, 12, normalizeSunday: false, "month", out var months, out _, out error) ||
            !TryParseField(fields[4], 0, 7, normalizeSunday: true, "day-of-week", out var daysOfWeek, out var dayOfWeekWildcard, out error))
        {
            return false;
        }

        if (dayOfWeekWildcard &&
            !dayOfMonthWildcard &&
            !HasPossibleCalendarDate(daysOfMonth, months))
        {
            error = "The selected day-of-month does not exist in any selected month.";
            return false;
        }

        schedule = new CronSchedule(
            minutes,
            hours,
            daysOfMonth,
            months,
            daysOfWeek,
            dayOfMonthWildcard,
            dayOfWeekWildcard);
        return true;
    }

    public DateTime GetNextOccurrence(DateTime after)
    {
        var candidate = new DateTime(
            after.Year,
            after.Month,
            after.Day,
            after.Hour,
            after.Minute,
            0,
            after.Kind).AddMinutes(1);

        for (var index = 0; index < MaximumSearchMinutes; index++, candidate = candidate.AddMinutes(1))
        {
            if (candidate.Kind == DateTimeKind.Local && TimeZoneInfo.Local.IsInvalidTime(candidate))
                continue;
            if (!_months[candidate.Month] ||
                !_hours[candidate.Hour] ||
                !_minutes[candidate.Minute])
            {
                continue;
            }

            var dayOfMonthMatches = _daysOfMonth[candidate.Day];
            var dayOfWeekMatches = _daysOfWeek[(int)candidate.DayOfWeek];
            var dayMatches = (_dayOfMonthWildcard, _dayOfWeekWildcard) switch
            {
                (true, true) => true,
                (true, false) => dayOfWeekMatches,
                (false, true) => dayOfMonthMatches,
                _ => dayOfMonthMatches || dayOfWeekMatches,
            };

            if (dayMatches)
                return candidate;
        }

        throw new InvalidOperationException("The cron schedule has no occurrence within the next five years.");
    }

    public static bool TryGetDailyTime(string? expression, out TimeSpan time)
    {
        var fields = expression?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields is not { Length: 5 } ||
            fields[2] != "*" ||
            fields[3] != "*" ||
            fields[4] != "*" ||
            !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minute) ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ||
            minute is < 0 or > 59 ||
            hour is < 0 or > 23)
        {
            time = default;
            return false;
        }

        time = new TimeSpan(hour, minute, 0);
        return true;
    }

    public static string FromDailyTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(time), "A daily restart time must be within one day.");

        return $"{time.Minutes} {time.Hours} * * *";
    }

    private static bool TryParseField(
        string field,
        int minimum,
        int maximum,
        bool normalizeSunday,
        string fieldName,
        out bool[] allowed,
        out bool wildcard,
        out string? error)
    {
        allowed = new bool[normalizeSunday ? maximum : maximum + 1];
        wildcard = field == "*";
        error = null;

        foreach (var segment in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var rangeAndStep = segment.Split('/', StringSplitOptions.TrimEntries);
            if (rangeAndStep.Length > 2 ||
                (rangeAndStep.Length == 2 &&
                 !TryParseNumber(rangeAndStep[1], 1, maximum - minimum + 1, out _)))
            {
                error = $"Invalid {fieldName} step in '{field}'.";
                return false;
            }

            var step = rangeAndStep.Length == 2
                ? int.Parse(rangeAndStep[1], CultureInfo.InvariantCulture)
                : 1;
            var range = rangeAndStep[0];

            int start;
            int end;
            if (range == "*")
            {
                start = minimum;
                end = maximum;
            }
            else
            {
                var bounds = range.Split('-', StringSplitOptions.TrimEntries);
                if (bounds.Length == 1)
                {
                    if (!TryParseNumber(bounds[0], minimum, maximum, out start))
                    {
                        error = $"Invalid {fieldName} value in '{field}'.";
                        return false;
                    }
                    end = rangeAndStep.Length == 2 ? maximum : start;
                }
                else if (bounds.Length == 2 &&
                         TryParseNumber(bounds[0], minimum, maximum, out start) &&
                         TryParseNumber(bounds[1], minimum, maximum, out end) &&
                         start <= end)
                {
                    // Parsed above.
                }
                else
                {
                    error = $"Invalid {fieldName} range in '{field}'.";
                    return false;
                }
            }

            for (var value = start; value <= end; value += step)
            {
                var normalized = normalizeSunday && value == 7 ? 0 : value;
                allowed[normalized] = true;
            }
        }

        if (Array.IndexOf(allowed, true) >= 0)
            return true;

        error = $"The {fieldName} field cannot be empty.";
        return false;
    }

    private static bool TryParseNumber(string value, int minimum, int maximum, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
        parsed >= minimum &&
        parsed <= maximum;

    private static bool HasPossibleCalendarDate(bool[] daysOfMonth, bool[] months)
    {
        for (var month = 1; month <= 12; month++)
        {
            if (!months[month])
                continue;

            var maximumDay = month switch
            {
                2 => 29,
                4 or 6 or 9 or 11 => 30,
                _ => 31,
            };
            for (var day = 1; day <= maximumDay; day++)
            {
                if (daysOfMonth[day])
                    return true;
            }
        }

        return false;
    }
}

public sealed class CronExpressionConverter : StringConverter
{
    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string expression)
        {
            var normalized = string.Join(
                ' ',
                expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            CronSchedule.Parse(normalized);
            return normalized;
        }

        return base.ConvertFrom(context, culture, value);
    }
}
