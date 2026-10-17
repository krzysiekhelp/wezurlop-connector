using WuConnector.Core.Odczyt;

namespace WuConnector.Core.Rekordy;

/// <summary>
/// Dekodowanie LATA_KAL (USTALENIA 16.3): blob na miesiac, 2 bajty na dzien, little-endian.
///   480 (i inne dodatnie) — dzien roboczy (minuty pracy), 0 — wolny, 9999 — niedziela/swieto.
/// Format nieudokumentowany przez producenta — wiazace dni i tak liczy R2 przy zapisie.
/// </summary>
public static class KalendarzR2
{
    public const int Wolny = 0;
    public const int Swieto = 9999;

    public static List<DateTime> DniNierobocze(LataKalWiersz w)
    {
        var dni = new List<DateTime>();
        for (int m = 1; m <= 12; m++)
        {
            byte[]? blob = w.Miesiace[m - 1];
            if (blob is null) continue;
            int ile = Math.Min(DateTime.DaysInMonth(w.Rok, m), blob.Length / 2);
            for (int d = 1; d <= ile; d++)
            {
                int v = blob[2 * d - 2] | (blob[2 * d - 1] << 8);
                if (v == Wolny || v == Swieto) dni.Add(new DateTime(w.Rok, m, d));
            }
        }
        return dni;
    }
}
