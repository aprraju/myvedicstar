using Microsoft.Extensions.Options;
using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Business.Configuration;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Panchang.Business.Calendar;

/// <summary>
/// Builds and validates the list of local dates for single-day, range, month and year requests.
/// </summary>
internal sealed class CalendarRangeBuilder(IOptions<PanchangOptions> options)
{
    public IReadOnlyList<LocalDate> Single(LocalDate date) => Range(date, 1);

    public IReadOnlyList<LocalDate> Range(LocalDate startDate, int days)
    {
        var maxDays = options.Value.MaxRangeDays;
        if (days < 1 || days > maxDays)
        {
            throw new ValidationException("days", $"Days must be between 1 and {maxDays}.");
        }

        EnsureSupported(startDate);

        var dates = new LocalDate[days];
        for (var i = 0; i < days; i++)
        {
            dates[i] = startDate.PlusDays(i);
        }

        EnsureSupported(dates[^1]);
        return dates;
    }

    public IReadOnlyList<LocalDate> Month(int year, int month)
    {
        EnsureSupportedYear(year);
        if (month is < 1 or > 12)
        {
            throw new ValidationException("month", "Month must be between 1 and 12.");
        }

        var start = new LocalDate(year, month, 1);
        return Range(start, start.Calendar.GetDaysInMonth(year, month));
    }

    public IReadOnlyList<LocalDate> Year(int year)
    {
        EnsureSupportedYear(year);

        var start = new LocalDate(year, 1, 1);
        return Range(start, start.Calendar.GetDaysInYear(year));
    }

    private static void EnsureSupportedYear(int year)
    {
        if (year is < SupportedDates.MinYear or > SupportedDates.MaxYear)
        {
            throw new ValidationException("year", $"Year must be between {SupportedDates.MinYear} and {SupportedDates.MaxYear}.");
        }
    }

    private static void EnsureSupported(LocalDate date)
    {
        if (date.Year is < SupportedDates.MinYear or > SupportedDates.MaxYear)
        {
            throw new ValidationException(
                "date", $"Dates must fall between {SupportedDates.MinYear}-01-01 and {SupportedDates.MaxYear}-12-31.");
        }
    }
}
