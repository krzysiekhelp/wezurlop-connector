using System.Text.Json.Nodes;

namespace AtrapaTenanta;

/// <summary>Stan atrapy serwera wezurlop — w pamieci, jeden zamek na wszystko.</summary>
public sealed class StanAtrapy
{
    public readonly object Zamek = new();

    public string Klucz { get; set; } = "";
    public HashSet<string> DozwoloneNip { get; } = new();
    public HashSet<string> OdrzuconeNip { get; } = new();
    public Dictionary<string, string> Zgloszenia { get; } = new();
    public Dictionary<string, StanFirmy> Firmy { get; } = new();
    public Dictionary<string, (int Status, string Tresc)> Idempotencja { get; } = new();
    public Queue<(int Status, int? RetryAfter)> Awarie { get; } = new();

    public int NastepnyInterwal { get; set; } = 60;
    public TimeSpan Rezerwacja { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Limity serwera z tury 7; 0 = wylaczone (testy robia wiele zadan naraz).</summary>
    public int LimitNaMinute { get; set; }
    public int LimitZgloszenNaGodzine { get; set; }
    public Queue<DateTime> Zadania { get; } = new();
    public Queue<DateTime> Zgloszeniaczas { get; } = new();

    /// <summary>Kazde zadanie HTTP (bez /_atrapa) — do asercji w testach.</summary>
    public List<string> Dziennik { get; } = new();

    public StanFirmy Firma(string nip)
    {
        if (!Firmy.TryGetValue(nip, out var f)) Firmy[nip] = f = new StanFirmy();
        return f;
    }
}

public sealed class StanFirmy
{
    public Dictionary<string, Dictionary<string, JsonObject>> Rekordy { get; } = new();
    public Dictionary<string, string> Kursory { get; } = new();
    public int LicznikKursora { get; set; } = 880;
    public HashSet<string> WymusPelny { get; } = new();
    public bool PrzegladKontrolny { get; set; }
    public Dictionary<string, Przebieg> Przebiegi { get; } = new();
    public List<Zadanie> Zadania { get; } = new();
    public HashSet<string> Wycofane { get; } = new();
    public List<JsonObject> Starty { get; } = new();
    public List<JsonObject> Konce { get; } = new();
    public int PominietePola { get; set; }

    /// <summary>Bezpiecznik z 20.09.2026: odcisk tozsamosci zapamietany przy pierwszym starcie tej firmy.</summary>
    public string? OdciskTozsamosci { get; set; }
    public DateTimeOffset? OstatniPrzebiegZamkniety { get; set; }

    public Dictionary<string, JsonObject> Zbior(string z)
    {
        if (!Rekordy.TryGetValue(z, out var d)) Rekordy[z] = d = new();
        return d;
    }

    public string NowyKursor(string zbior) => Kursory[zbior] = "c-" + (++LicznikKursora);
}

public sealed class Przebieg
{
    public required string Id { get; init; }
    public Dictionary<string, HashSet<string>> PrzegladId { get; } = new();
    public bool Rozjazd { get; set; }
    public bool Zamkniety { get; set; }
}

public enum StanZadania { Oczekuje, Wydane, Zamkniete }

public sealed class Zadanie
{
    public required string RequestId { get; init; }
    public required JsonObject Tresc { get; init; }
    public StanZadania Stan { get; set; } = StanZadania.Oczekuje;
    public DateTime WydaneDo { get; set; }
    public int Wydan { get; set; }
    public JsonObject? Wynik { get; set; }
    public int Wynikow { get; set; }
}
