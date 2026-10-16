using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PM.horizOn.Cloud.Transport
{
    /// <summary>Account429 retries wait at least Retry-After; feature limits are returned immediately.</summary>
    internal static class RateLimitRetryPolicy
    {
        private static readonly Random Random = new Random();
        internal static bool IsFeatureLimit(string code) => !string.IsNullOrEmpty(code);
        internal static double RetryAfterSeconds(string header, double fallback, DateTimeOffset now)
        {
            string raw = header?.Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0) return seconds;
            // A syntactically valid integer cannot become a short fallback just because parsing overflows.
            if (!string.IsNullOrEmpty(raw) && Regex.IsMatch(raw, @"\A[0-9]+\z") && raw.TrimStart('0').Length > 0)
                return double.MaxValue;
            if (TryHttpDate(raw, now, out var date))
            { seconds = (date - now).TotalSeconds; if (seconds > 0) return seconds; }
            return !double.IsNaN(fallback) && !double.IsInfinity(fallback) && fallback > 0 ? Math.Max(1, fallback) : 1;
        }
        private static bool TryHttpDate(string raw, DateTimeOffset now, out DateTimeOffset date)
        {
            // RFC9110 5.6.7 requires IMF-fixdate, RFC850 and asctime forms.
            var old = Regex.Match(raw ?? "", @"\A(?<weekday>[A-Za-z]+), (?<day>[0-9]{2})-(?<month>[A-Za-z]{3})-(?<year>[0-9]{2}) (?<time>[0-9]{2}:[0-9]{2}:[0-9]{2}) GMT\z");
            if (old.Success)
            {
                int upperYear = Math.Min(9999, now.Year + 50);
                int year = upperYear / 100 * 100 + int.Parse(old.Groups["year"].Value, CultureInfo.InvariantCulture);
                if (year > upperYear) year -= 100;
                string expanded = old.Groups["day"].Value + "-" + old.Groups["month"].Value + "-" + year.ToString("D4", CultureInfo.InvariantCulture) + " " + old.Groups["time"].Value;
                if (DateTimeOffset.TryParseExact(expanded, "dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date))
                {
                    if (now.Year <= 9949 && date > now.AddYears(50)) date = date.AddYears(-100);
                    return string.Equals(old.Groups["weekday"].Value, date.ToString("dddd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }
            return DateTimeOffset.TryParseExact(raw, new[] { "r", "ddd MMM d HH:mm:ss yyyy" }, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out date);
        }
        internal static int? DelayMilliseconds(double seconds, double randomUnit)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || double.IsNaN(randomUnit) || randomUnit < 0 || randomUnit > 1)
                throw new ArgumentOutOfRangeException();
            double milliseconds = Math.Ceiling(seconds * (1 + randomUnit * .2) * 1000);
            // Fail closed instead of retrying earlier than a very large server wait or overflowing Task.Delay.
            return milliseconds <= int.MaxValue ? (int?)milliseconds : null;
        }
        internal static int? DelayMilliseconds(double seconds)
        { lock (Random) return DelayMilliseconds(seconds, Random.NextDouble()); }
    }
}
