using System;

namespace HadesMatrixBridge.Models
{
    public class TimeRange
    {
        public TimeOnly Start { get; }
        public TimeOnly End { get; }

        public TimeRange(TimeOnly start, TimeOnly end)
        {
            Start = start;
            End = end;
        }

        public bool Contains(TimeOnly time)
        {
            return time >= Start && time <= End;
        }

        public static TimeRange? Parse(string range)
        {
            try
            {
                var parts = range.Split('-');
                if (parts.Length != 2) return null;

                if (TimeOnly.TryParse(parts[0].Trim(), out var start) &&
                    TimeOnly.TryParse(parts[1].Trim(), out var end))
                {
                    return new TimeRange(start, end);
                }
            }
            catch
            {
                // Invalid format
            }
            return null;
        }
    }
}