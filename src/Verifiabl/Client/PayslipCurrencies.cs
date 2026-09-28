using System.Collections.ObjectModel;

namespace Verifiabl.Client;

/// <summary>Currency codes accepted by the AU2 and NZ2 payslip schemas.</summary>
public static class PayslipCurrencies
{
    /// <summary>Australian dollar.</summary>
    public const string Aud = "AUD";

    /// <summary>New Zealand dollar.</summary>
    public const string Nzd = "NZD";

    /// <summary>United States dollar.</summary>
    public const string Usd = "USD";

    /// <summary>Pound sterling.</summary>
    public const string Gbp = "GBP";

    /// <summary>Euro.</summary>
    public const string Eur = "EUR";

    /// <summary>Canadian dollar.</summary>
    public const string Cad = "CAD";

    /// <summary>Singapore dollar.</summary>
    public const string Sgd = "SGD";

    /// <summary>Hong Kong dollar.</summary>
    public const string Hkd = "HKD";

    /// <summary>Swiss franc.</summary>
    public const string Chf = "CHF";

    /// <summary>South African rand.</summary>
    public const string Zar = "ZAR";

    /// <summary>The complete supported currency allow-list.</summary>
    public static readonly IReadOnlyList<string> All = new ReadOnlyCollection<string>(
    [
        Aud,
        Nzd,
        Usd,
        Gbp,
        Eur,
        Cad,
        Sgd,
        Hkd,
        Chf,
        Zar,
    ]);
}
