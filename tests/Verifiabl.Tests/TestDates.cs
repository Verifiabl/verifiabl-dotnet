using System.Globalization;

namespace Verifiabl.Tests;

internal static class TestDates
{
    // Generated v2 dates are DateOnly on modern .NET and YYYY-MM-DD strings on .NET Framework.
#if NET6_0_OR_GREATER
    internal static DateOnly PayslipDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
#else
    internal static string PayslipDate(string value) => value;
#endif
}
