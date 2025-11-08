namespace CalendarLib
{
public enum DayCountConvention
    {
        Act365,
        Act360,
        ActAct
    }

    public class MarketCalendar
    {
        private readonly HashSet<DateTime> _holidays;
        private readonly HashSet<DayOfWeek> _weekendDays;
        private readonly TimeSpan _marketOpen;
        private readonly TimeSpan _marketClose;
        private readonly DayCountConvention _dayCountConvention;

        public MarketCalendar(
            IEnumerable<DateTime> holidays,
            IEnumerable<DayOfWeek> weekendDays,
            TimeSpan marketOpen,
            TimeSpan marketClose,
            DayCountConvention dayCountConvention = DayCountConvention.ActAct)
        {
            _holidays = new HashSet<DateTime>(holidays);
            _weekendDays = new HashSet<DayOfWeek>(weekendDays);
            _marketOpen = marketOpen;
            _marketClose = marketClose;
            _dayCountConvention = dayCountConvention;
        }

        public bool IsBusinessDay(DateTime date)
        {
            return !_weekendDays.Contains(date.DayOfWeek) && !_holidays.Contains(date.Date);
        }

        public DateTime GetNextBusinessDate(DateTime date)
        {
            DateTime nextDate = date.Date.AddDays(1);
            while (!IsBusinessDay(nextDate))
            {
                nextDate = nextDate.AddDays(1);
            }
            return nextDate;
        }

        public DateTime AddDays(DateTime date, int days)
        {
            return date.AddDays(days);
        }

        public DateTime AddBusinessDays(DateTime date, int businessDays)
        {
            DateTime result = date;
            int added = 0;
            while (added < businessDays)
            {
                result = result.AddDays(1);
                if (IsBusinessDay(result))
                {
                    added++;
                }
            }
            return result;
        }

        public double GetYearFraction(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException($"Invalid date range: 'to' ({to:yyyy-MM-dd HH:mm:ss}) is earlier than 'from' ({from:yyyy-MM-dd HH:mm:ss}).");

            if (to == from)
                return 0.0;

            double yearFraction = 0.0;
            var current = from;

            var denominators = new Dictionary<int, double>();
            for (int year = from.Year; year <= to.Year; year++)
            {
                denominators[year] = GetDenominator(year);
            }

            while (current.Date <= to.Date)
            {
                if (!IsBusinessDay(current))
                {
                    current = GetNextBusinessDate(current);
                    if (current > to) break;
                }

                DateTime marketStart = current.Date + _marketOpen;
                DateTime marketEnd = current.Date + _marketClose;

                DateTime actualStart = current > marketStart ? current : marketStart;
                DateTime actualEnd = to < marketEnd ? to : marketEnd;

                if (actualEnd > actualStart)
                {
                    double tradingSeconds = (actualEnd - actualStart).TotalSeconds;
                    double denominator = denominators[current.Year];
                    yearFraction += tradingSeconds / denominator;
                }

                current = GetNextBusinessDate(current).Date + _marketOpen;
            }

            return yearFraction;
        }

        private double GetDenominator(int year)
        {
            double tradingSecondsPerDay = (_marketClose - _marketOpen).TotalSeconds;
            int businessDays = Enumerable.Range(1, DateTime.IsLeapYear(year) ? 366 : 365)
                .Select(day => new DateTime(year, 1, 1).AddDays(day - 1))
                .Count(IsBusinessDay);

            return _dayCountConvention switch
            {
                DayCountConvention.Act365 => 365.0 * 24 * 60 * 60,
                DayCountConvention.Act360 => 360.0 * 24 * 60 * 60,
                DayCountConvention.ActAct => businessDays * tradingSecondsPerDay,
                _ => throw new InvalidOperationException("Unknown day count convention.")
            };
        }
    }
}