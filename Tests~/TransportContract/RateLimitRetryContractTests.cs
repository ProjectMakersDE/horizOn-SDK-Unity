using System;
using System.Globalization;
using PM.horizOn.Cloud.Transport;

internal static class RateLimitRetryContractTests
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i <= 1000; i++)
        {
            double sample = i / 1000d; int wait = RateLimitRetryPolicy.DelayMilliseconds(10, sample).Value;
            Check(wait >= 10000 && wait <= 12000, "jitter stays in positive0–20% range");
        }
        Check(RateLimitRetryPolicy.DelayMilliseconds(10, 0) == 10000, "lower boundary never retries early");
        Check(RateLimitRetryPolicy.DelayMilliseconds(10, 1) == 12000, "upper boundary20%");
        Check(RateLimitRetryPolicy.DelayMilliseconds(1, .12345) == 1025, "delay rounds up to milliseconds");
        Check(!RateLimitRetryPolicy.DelayMilliseconds(double.MaxValue, .5).HasValue, "unrepresentable wait fails closed without overflow/early retry");
        foreach (string code in new[] { "RUN_RATE_LIMITED", "RUN_CAPACITY_REACHED", "INBOX_RATE_LIMITED", "CUSTOM_FUTURE_CODE", " " })
            Check(RateLimitRetryPolicy.IsFeatureLimit(code), "all nonempty feature codes refuse automatic retry");
        Check(!RateLimitRetryPolicy.IsFeatureLimit(null) && !RateLimitRetryPolicy.IsFeatureLimit(""), "codefree account limit remains retryable");
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Check(RateLimitRetryPolicy.RetryAfterSeconds("1.5", 3, now) == 1.5, "HTTP decimals use invariant culture");
            Check(RateLimitRetryPolicy.RetryAfterSeconds(now.AddSeconds(20).ToString("r", CultureInfo.InvariantCulture), 3, now) == 20, "HTTP-date header supported");
            Check(RateLimitRetryPolicy.RetryAfterSeconds("Friday, 16-Oct-26 01:00:00 GMT", 3, now) == 3600, "obsolete RFC850 date preserves wait");
            Check(RateLimitRetryPolicy.RetryAfterSeconds("Fri Oct 16 01:00:00 2026", 3, now) == 3600, "asctime date preserves wait");
            var single = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
            Check(RateLimitRetryPolicy.RetryAfterSeconds("Fri Nov  6 01:00:00 2026", 3, single) == 3600, "single digit asctime day accepted");
            Check(!RateLimitRetryPolicy.DelayMilliseconds(RateLimitRetryPolicy.RetryAfterSeconds(new string('9', 400), 1, now), 0).HasValue, "oversized valid integer never falls back to early retry");
            var far = new DateTimeOffset(2075, 10, 16, 0, 0, 0, TimeSpan.Zero);
            Check(RateLimitRetryPolicy.RetryAfterSeconds("Wednesday, 16-Oct-75 01:00:00 GMT", 3, far) == 3600, "two-digit year follows current50-year window, not device calendar cutoff");
            foreach (string header in new[] { null, "", "bad", "NaN", "Infinity", "-5", "0" })
                Check(RateLimitRetryPolicy.RetryAfterSeconds(header, 3, now) == 3, "invalid headers use configured positive fallback");
            Check(RateLimitRetryPolicy.RetryAfterSeconds("bad", 0, now) == 1, "invalid zero fallback cannot spin");
        }
        finally { CultureInfo.CurrentCulture = old; }
        Console.WriteLine("Rate-limit retry-after, positive jitter, feature-code and bounded-delay contracts passed.");
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception("Rate-limit contract failed: " + name); }
}
