using System.Net;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Wymiana;

namespace WuConnector.Tests;

/// <summary>
/// Tura 11: NIP firmy idzie naglowkiem `X-Nip`, nie parametrem `?nip=`. Powod jest prywatnosciowy —
/// parametr w adresie zapisywal sie jawnie w logu dostepu serwera tenanta, a log dostepu zyje dluzej
/// i oglada go wiecej osob niz baze. Te testy pilnuja obu polowek umowy: ze naglowek jest tam,
/// gdzie ma byc, i ze `nip=` nie wyciekl z powrotem do zadnego adresu GET.
/// </summary>
public class NipWNaglowkuTesty
{
    /// <summary>Zapisuje kazde zadanie i odpowiada pustym obiektem — tresc odpowiedzi nie ma tu znaczenia.</summary>
    sealed class Podsluch : HttpMessageHandler
    {
        public List<HttpRequestMessage> Zadania { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Zadania.Add(r);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"protokol":1,"zadania":[]}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    const string NipTestowy = "1234567009";

    static (KlientTenanta, Podsluch) Klient()
    {
        var p = new Podsluch();
        return (new KlientTenanta(new Uri("https://przyklad.test"), new string('a', 64), "test", p), p);
    }

    [Fact]
    public async Task Stan_wysyla_NIP_naglowkiem_a_nie_w_adresie()
    {
        var (k, p) = Klient();
        using (k) await k.StanAsync(NipTestowy);

        var z = Assert.Single(p.Zadania);
        Assert.Equal(NipTestowy, Assert.Single(z.Headers.GetValues("X-Nip")));
        Assert.DoesNotContain("nip", z.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stan_bez_NIP_u_nie_wysyla_naglowka()
    {
        var (k, p) = Klient();
        using (k) await k.StanAsync();

        // Naglowek jest opcjonalny: bez niego serwer zwraca sam stan klucza, jeszcze przed wykryciem firmy.
        var z = Assert.Single(p.Zadania);
        Assert.False(z.Headers.Contains("X-Nip"));
        Assert.Equal("/api/v1/sync/stan", z.RequestUri!.AbsolutePath);
        Assert.Empty(z.RequestUri.Query);
    }

    [Fact]
    public async Task Zadania_wysylaja_NIP_naglowkiem_a_przebieg_i_limit_zostaja_w_adresie()
    {
        var (k, p) = Klient();
        using (k) await k.ZadaniaAsync(NipTestowy, "przebieg-7", 20);

        var z = Assert.Single(p.Zadania);
        Assert.Equal(NipTestowy, Assert.Single(z.Headers.GetValues("X-Nip")));
        Assert.Contains("przebiegId=przebieg-7", z.RequestUri!.Query);
        Assert.Contains("limit=20", z.RequestUri.Query);
        Assert.DoesNotContain("nip", z.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bramka na przyszlosc: gdyby ktos dolozyl operacje GET z NIP-em w adresie, ten test ma ja zatrzymac,
    /// zanim NIP-y klienta zaczna sie znow zapisywac w logu dostepu serwera.
    /// </summary>
    [Fact]
    public async Task Zadna_operacja_GET_nie_ma_nip_w_adresie()
    {
        var (k, p) = Klient();
        using (k)
        {
            await k.StanAsync();
            await k.StanAsync(NipTestowy);
            await k.ZadaniaAsync(NipTestowy, "przebieg-7", 20);
        }

        var gety = p.Zadania.Where(z => z.Method == HttpMethod.Get).ToList();
        Assert.Equal(3, gety.Count);
        foreach (var z in gety)
            Assert.DoesNotContain("nip", z.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zachowanie connectora kontra schemat tenanta. Kopia `openapi-sync-v1.json` bywa nieaktualna
    /// (jest aktualizowana recznie, mailem), wiec ten test jest ostrzezeniem: jesli ktos podmieni ja na
    /// starsza, nie przeoczymy tego, bo caly sens zmiany — naglowek `X-Nip` — zniknie ze schematu.
    /// </summary>
    [Fact]
    public void Schemat_tenanta_zna_naglowek_X_Nip_i_kod_NIP_NIESPOJNY()
    {
        string plik = Path.Combine(AppContext.BaseDirectory, "Kontrakt", "openapi-sync-v1.json");
        using var dok = System.Text.Json.JsonDocument.Parse(File.ReadAllText(plik));
        var korzen = dok.RootElement;

        // Naglowek jest zdefiniowany raz, jako wspolny parametr.
        var naglowek = korzen.GetProperty("components").GetProperty("parameters").GetProperty("NipHeader");
        Assert.Equal("X-Nip", naglowek.GetProperty("name").GetString());
        Assert.Equal("header", naglowek.GetProperty("in").GetString());

        foreach (string sciezka in new[] { "/api/v1/sync/stan", "/api/v1/sync/zadania" })
        {
            var parametry = korzen.GetProperty("paths").GetProperty(sciezka).GetProperty("get").GetProperty("parameters");

            Assert.Contains(parametry.EnumerateArray(),
                p => p.TryGetProperty("$ref", out var r) && r.GetString() == "#/components/parameters/NipHeader");

            // `?nip=` jeszcze dziala (okres przejsciowy po stronie tenanta), ale jest oznaczony jako wycofywany.
            var wAdresie = parametry.EnumerateArray().Single(p => p.TryGetProperty("name", out var n) && n.GetString() == "nip");
            Assert.True(wAdresie.GetProperty("deprecated").GetBoolean());

            // Dwa rozne NIP-y w jednym zadaniu to 400 — connector traktuje ten kod jako blad konfiguracji.
            Assert.True(korzen.GetProperty("paths").GetProperty(sciezka).GetProperty("get")
                              .GetProperty("responses").TryGetProperty("400", out _));
        }
    }

    [Fact]
    public void NIP_w_dzienniku_jest_skrocony_do_trzech_ostatnich_cyfr()
    {
        // Trzy cyfry wystarczaja, zeby odroznic firmy w dzienniku, a za malo, zeby odtworzyc numer.
        Assert.Equal("*******009", Nip.DoLogu(NipTestowy));
        Assert.Equal("*******009", Nip.DoLogu("123-456-70-09"));   // normalizacja przed maskowaniem
        Assert.Equal("*******009", Nip.DoLogu("PL1234567009"));
        Assert.Equal("?", Nip.DoLogu(null));
        Assert.Equal("?", Nip.DoLogu("   "));

        // najwazniejsze: pelny numer nie zostaje nigdzie w wyniku
        Assert.DoesNotContain(NipTestowy, Nip.DoLogu(NipTestowy));
    }
}
