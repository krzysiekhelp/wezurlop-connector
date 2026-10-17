using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WuConnector.Core.Rekordy;

namespace WuConnector.Core.Wymiana;

/// <summary>
/// Klient API tenanta v1. Jedno wywolanie = jedno zadanie HTTP; ponawianie nalezy do cyklu (Cykl),
/// ktory zna kontekst (przebieg, paczke, klucz idempotencji).
/// </summary>
public sealed class KlientTenanta : IDisposable
{
    public const string WersjaProtokolu = "1";

    /// <summary>Paczki wieksze niz tyle bajtow ida z gzip.</summary>
    public const int ProgKompresji = 16 * 1024;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly HttpClient _http;
    readonly bool _wlasnyHttp;
    readonly Ogranicznik _ogranicznik;

    /// <param name="adres">Domena klienta, np. https://urlopy.firma.pl — sciezka /api/v1/sync/ dokladana tutaj.</param>
    /// <param name="klucz">Klucz instalacji connectora (64 hex). Nigdy w URL ani w logu.</param>
    public KlientTenanta(Uri adres, string klucz, string wersjaConnectora, HttpMessageHandler? handler = null, TimeSpan? limitCzasu = null,
                         Ogranicznik? ogranicznik = null)
    {
        _wlasnyHttp = true;
        _ogranicznik = ogranicznik ?? Ogranicznik.DlaTenanta();
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(adres, "/api/v1/sync/");
        _http.Timeout = limitCzasu ?? TimeSpan.FromSeconds(45);   // serwer ma twarde 30 s; zapas na siec
        // X-Protokol od tury 7 jest informacyjny (brak = 1), ale wysylamy go zawsze.
        // Klucz w OBU naglowkach — czesc hostingow wycina Authorization pod CGI/FastCGI.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", klucz);
        _http.DefaultRequestHeaders.Add("X-Api-Key", klucz);
        _http.DefaultRequestHeaders.Add("X-Protokol", WersjaProtokolu);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WuConnector", wersjaConnectora));
    }

    // ------------------------------------------------------------------ operacje

    public Task<OdpowiedzStart> StartAsync(StanConnectora stan, string kluczIdempotencji, CancellationToken ct = default)
    {
        var cialo = new JsonObject
        {
            ["nip"] = stan.Nip,
            ["wersjaConnectora"] = stan.WersjaConnectora,
            ["wersjaR2"] = stan.WersjaR2,
            ["ostatniUdanyCykl"] = stan.OstatniUdanyCykl?.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            ["stan"] = stan.Stan,
            ["stanOpis"] = stan.StanOpis is { Length: > 200 } d ? d[..200] : stan.StanOpis,
            ["kursory"] = new JsonObject(stan.Kursory.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value))),
            ["odciskTozsamosci"] = stan.OdciskTozsamosci,
        };
        return PostAsync<OdpowiedzStart>("start", cialo, kluczIdempotencji, ct);
    }

    /// <param name="rekordy">Rekordy i usuniecia tego zbioru; max 500 (limit protokolu).</param>
    public Task<OdpowiedzPaczki> DaneAsync(string nip, string przebiegId, string zbior, int paczka, bool ostatnia,
                                           IReadOnlyList<JsonObject> rekordy, string kluczIdempotencji, CancellationToken ct = default)
    {
        var cialo = new JsonObject
        {
            ["nip"] = nip, ["przebiegId"] = przebiegId, ["zbior"] = zbior, ["paczka"] = paczka, ["ostatnia"] = ostatnia,
            ["rekordy"] = new JsonArray(rekordy.Select(r => (JsonNode?)r.DeepClone()).ToArray()),
        };
        return PostAsync<OdpowiedzPaczki>("dane", cialo, kluczIdempotencji, ct);
    }

    public Task<OdpowiedzPaczki> PrzegladAsync(string nip, string przebiegId, string zbior, int paczka, bool ostatnia,
                                               IReadOnlyList<Rekord> pozycje, string kluczIdempotencji, CancellationToken ct = default)
    {
        var cialo = new JsonObject
        {
            ["nip"] = nip, ["przebiegId"] = przebiegId, ["zbior"] = zbior, ["paczka"] = paczka, ["ostatnia"] = ostatnia,
            ["pozycje"] = new JsonArray(pozycje.Select(r => (JsonNode?)new JsonObject { ["id"] = r.Id, ["suma"] = r.Suma }).ToArray()),
        };
        return PostAsync<OdpowiedzPaczki>("przeglad", cialo, kluczIdempotencji, ct);
    }


    /// <summary>
    /// GET z NIP-em firmy w naglowku <c>X-Nip</c>, nigdy w adresie.
    /// Powod: parametr <c>?nip=</c> zapisywal sie jawnie w logu dostepu serwera tenanta, a log dostepu
    /// zyje dluzej i oglada go wiecej osob niz baze. Naglowki do niego nie trafiaja.
    /// `nip` w ciele operacji POST zostaje bez zmian — tam nie ma tego problemu.
    /// Gdyby w adresie i w naglowku byly dwa rozne NIP-y, tenant odpowiada 400 NIP_NIESPOJNY;
    /// dlatego connector nie dokleja `?nip=` nawet pomocniczo.
    /// </summary>
    static HttpRequestMessage Get(string sciezka, string? nip)
    {
        var z = new HttpRequestMessage(HttpMethod.Get, sciezka);
        if (nip is not null) z.Headers.Add("X-Nip", nip);
        return z;
    }

    /// <summary>
    /// GET /sync/stan — stan klucza i firmy bez otwierania przebiegu (test polaczenia).
    /// `nip` opcjonalny: bez niego sprawdzamy sam klucz, jeszcze zanim poznamy NIP firmy.
    /// </summary>
    public async Task<OdpowiedzStanu> StanAsync(string? nip = null, CancellationToken ct = default)
    {
        using var odp = await WyslijAsync(() => Get("stan", nip), ct);
        return await CzytajAsync<OdpowiedzStanu>(odp, ct);
    }

    public async Task<List<ZadanieTenanta>> ZadaniaAsync(string nip, string przebiegId, int limit, CancellationToken ct = default)
    {
        // X-Idempotency-Key tylko przy POST. NIP idzie naglowkiem; w adresie zostaja przebiegId i limit.
        string q = $"zadania?przebiegId={Uri.EscapeDataString(przebiegId)}&limit={limit}";
        using var odp = await WyslijAsync(() => Get(q, nip), ct);
        return (await CzytajAsync<OdpowiedzZadania>(odp, ct)).Zadania ?? new();
    }

    public Task<JsonObject> WynikiAsync(string nip, string przebiegId, IReadOnlyList<WynikZadania> wyniki,
                                        string kluczIdempotencji, CancellationToken ct = default)
    {
        var cialo = new JsonObject
        {
            ["nip"] = nip, ["przebiegId"] = przebiegId,
            ["wyniki"] = JsonSerializer.SerializeToNode(wyniki, Json),
        };
        return PostAsync<JsonObject>("wyniki", cialo, kluczIdempotencji, ct);
    }

    public Task<OdpowiedzKoniec> KoniecAsync(PodsumowanieKonca p, string kluczIdempotencji, CancellationToken ct = default)
        => PostAsync<OdpowiedzKoniec>("koniec", (JsonObject)JsonSerializer.SerializeToNode(p, Json)!, kluczIdempotencji, ct);

    public Task<JsonObject> ZgloszenieFirmyAsync(string nip, string nazwa, string kluczIdempotencji, CancellationToken ct = default)
        => PostAsync<JsonObject>("zgloszenie-firmy", new JsonObject { ["nip"] = nip, ["nazwa"] = nazwa }, kluczIdempotencji, ct);

    // ------------------------------------------------------------------ transport

    async Task<T> PostAsync<T>(string sciezka, JsonObject cialo, string kluczIdempotencji, CancellationToken ct)
    {
        byte[] json = Encoding.UTF8.GetBytes(cialo.ToJsonString(Json));
        using var odp = await WyslijAsync(() =>
        {
            var z = new HttpRequestMessage(HttpMethod.Post, sciezka) { Content = Tresc(json) };
            z.Headers.Add("X-Idempotency-Key", kluczIdempotencji);
            return z;
        }, ct);
        return await CzytajAsync<T>(odp, ct);
    }

    static HttpContent Tresc(byte[] json)
    {
        HttpContent c;
        if (json.Length > ProgKompresji)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(json);
            c = new ByteArrayContent(ms.ToArray());
            c.Headers.ContentEncoding.Add("gzip");
        }
        else c = new ByteArrayContent(json);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return c;
    }

    async Task<HttpResponseMessage> WyslijAsync(Func<HttpRequestMessage> zadanie, CancellationToken ct)
    {
        await _ogranicznik.CzekajNaMiejsceAsync(ct);   // limit serwera: 120 zadan/min na klucz
        HttpResponseMessage odp;
        try
        {
            using var z = zadanie();
            odp = await _http.SendAsync(z, ct);
        }
        catch (HttpRequestException ex) { throw new BladProtokolu(0, "BRAK_POLACZENIA", ex.Message, null, inner: ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new BladProtokolu(0, "PRZEKROCZONY_CZAS", "Serwer nie odpowiedzial w czasie", null, inner: ex); }

        if (odp.IsSuccessStatusCode) return odp;

        using (odp)
        {
            string tresc = await odp.Content.ReadAsStringAsync(ct);
            string? kod = null, kom = null, zd = null, powod = null;
            var poz = new List<PozycjaBledu>();
            try
            {
                if (JsonNode.Parse(tresc)?["blad"] is JsonObject b)
                {
                    kod = (string?)b["kod"]; kom = (string?)b["komunikat"]; zd = (string?)b["zdarzenieId"];
                    powod = (string?)b["powod"];   // pole dokladane przez tenanta przy 401 i 409 — gdy go nie ma, zostaje null
                    if (b["pozycje"] is JsonArray a)
                        poz.AddRange(a.OfType<JsonObject>().Select(p => new PozycjaBledu((string)p["id"]!, (string?)p["kod"] ?? "", (string?)p["pole"])));
                }
            }
            catch (JsonException) { kom = tresc.Length > 200 ? tresc[..200] : tresc; }
            throw new BladProtokolu((int)odp.StatusCode, kod, kom, zd, RetryAfter(odp), poz, powod: powod);
        }
    }

    /// <summary>Retry-After w obu formatach: sekundy albo data HTTP.</summary>
    static TimeSpan? RetryAfter(HttpResponseMessage odp)
    {
        var ra = odp.Headers.RetryAfter;
        if (ra?.Delta is TimeSpan d) return d;
        if (ra?.Date is DateTimeOffset t) { var x = t - DateTimeOffset.UtcNow; return x > TimeSpan.Zero ? x : TimeSpan.Zero; }
        return null;
    }

    static async Task<T> CzytajAsync<T>(HttpResponseMessage odp, CancellationToken ct)
    {
        using (odp)
        {
            await using var s = await odp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(s, Json, ct)
                   ?? throw new BladProtokolu((int)odp.StatusCode, "ODPOWIEDZ_PUSTA", "Pusta odpowiedz serwera", null);
        }
    }

    public void Dispose() { if (_wlasnyHttp) _http.Dispose(); }
}
