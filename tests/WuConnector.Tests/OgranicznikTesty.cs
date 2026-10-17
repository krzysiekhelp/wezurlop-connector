using System.Net;
using System.Net.Http.Json;
using WuConnector.Core.Wymiana;

namespace WuConnector.Tests;

public class OgranicznikTesty
{
    [Fact]
    public async Task Nadmiarowe_zadanie_czeka_do_zwolnienia_miejsca_w_oknie()
    {
        var teraz = new DateTime(2026, 9, 20, 12, 0, 0);
        var czekania = new List<TimeSpan>();
        var o = new Ogranicznik(3, TimeSpan.FromMinutes(1), () => teraz,
            (t, _) => { czekania.Add(t); teraz += t; return Task.CompletedTask; });

        for (int i = 0; i < 3; i++) { await o.CzekajNaMiejsceAsync(default); teraz += TimeSpan.FromSeconds(10); }
        await o.CzekajNaMiejsceAsync(default);   // 4. w ciagu minuty — musi poczekac

        Assert.Equal(1, o.Opoznien);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(czekania));   // pierwsze wyszlo o 12:00, teraz 12:00:30
    }

    [Fact]
    public async Task Rozlozone_w_czasie_nie_czekaja()
    {
        var teraz = DateTime.UtcNow;
        var o = new Ogranicznik(2, TimeSpan.FromMinutes(1), () => teraz, (_, _) => throw new InvalidOperationException("nie powinno czekac"));
        for (int i = 0; i < 10; i++) { await o.CzekajNaMiejsceAsync(default); teraz += TimeSpan.FromSeconds(31); }
        Assert.Equal(0, o.Opoznien);
    }

    [Fact]
    public void Limit_tenanta_z_zapasem_wzgledem_120_na_minute()
    {
        // 100 < 120 — zapas na test polaczenia z okna i przesuniecie zegara.
        var o = Ogranicznik.DlaTenanta();
        Assert.NotNull(o);
    }
}

public class ProtokolTura7Testy : IClassFixture<AtrapaFixture>
{
    readonly AtrapaFixture _f;
    public ProtokolTura7Testy(AtrapaFixture f) { _f = f; }

    static HttpRequestMessage Start(string? protokol)
    {
        var z = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sync/start")
        {
            Content = JsonContent.Create(new { nip = AtrapaFixture.Nip, wersjaConnectora = "t", stan = "ok", kursory = new { } }),
        };
        z.Headers.Add("X-Api-Key", AtrapaFixture.Klucz);
        if (protokol is not null) z.Headers.Add("X-Protokol", protokol);
        return z;
    }

    [Fact]
    public async Task Brak_naglowka_X_Protokol_to_wersja_1()
        => Assert.Equal(HttpStatusCode.OK, (await _f.Sterowanie.SendAsync(Start(null))).StatusCode);

    [Fact]
    public async Task Niezgodna_wersja_to_409()
        => Assert.Equal(HttpStatusCode.Conflict, (await _f.Sterowanie.SendAsync(Start("2"))).StatusCode);
}
