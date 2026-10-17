using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WuConnector.Core.Biblioteki;

namespace WuConnector.Tests;

public class ManifestTesty
{
    [Fact]
    public void Podpisany_manifest_przechodzi_zmieniony_albo_obcym_kluczem_nie()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var obcy = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var m = new ManifestBibliotek(1, DateTimeOffset.Now, new() { new("10.10.33", "10.1.0.33", "AB", 1, "10.1.0.33/R2PApi10.dll") });
        byte[] tresc = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m, ManifestBibliotek.Json));
        string podpis = ManifestBibliotek.Podpisz(tresc, ec.ExportPkcs8PrivateKeyPem());

        Assert.NotNull(ManifestBibliotek.Zweryfikuj(tresc, podpis, ec.ExportSubjectPublicKeyInfoPem()));
        Assert.Null(ManifestBibliotek.Zweryfikuj(tresc, podpis, obcy.ExportSubjectPublicKeyInfoPem()));

        var zmieniony = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(tresc).Replace("10.1.0.33/", "10.1.0.32/"));
        Assert.Null(ManifestBibliotek.Zweryfikuj(zmieniony, podpis, ec.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData("10.1.0.33", "10.10.33", true)]
    [InlineData("10.1.0.32", "10.10.33", false)]
    [InlineData("10.1.0.32", "10.10.32", true)]
    [InlineData("11.1.0.33", "10.10.33", false)]
    public void Wersja_pliku_do_wersji_bazy(string plik, string baza, bool pasuje)
        => Assert.Equal(pasuje, ManifestBibliotek.PasujeDoBazy(plik, baza));

    [Fact]
    public void Wbudowany_klucz_publiczny_jest_poprawnym_kluczem_EC()
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(KluczManifestu.Publiczny);
        Assert.Equal(256, ec.KeySize);
    }
}

/// <summary>Testy na prawdziwych bibliotekach R2 z tej maszyny — pomijane, gdy ich nie ma.</summary>
public sealed class MagazynBibliotekTesty : IAsyncLifetime
{
    // Sciezki do prawdziwych bibliotek R2 sa dana konkretnej instalacji, wiec nie ma ich w repozytorium.
    // Podaje sie je recznie:
    //   set WU_TEST_DLL_NOWA=<...>\BIN\R2PApi10.dll    (wersja pliku 10.1.0.33)
    //   set WU_TEST_DLL_STARA=<...>\BIN\R2PApi10.dll   (wersja pliku 10.1.0.32)
    static string? Nowa => Environment.GetEnvironmentVariable("WU_TEST_DLL_NOWA");
    static string? Stara => Environment.GetEnvironmentVariable("WU_TEST_DLL_STARA");

    readonly string _tmp = Path.Combine(Path.GetTempPath(), "wu-biblioteki-" + Guid.NewGuid().ToString("N"));
    string Serwer => Path.Combine(_tmp, "serwer");
    string Bin => Path.Combine(_tmp, "BIN");
    string Magazyn => Path.Combine(_tmp, "r2api");
    readonly ECDsa _klucz = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    WebApplication _app = null!;
    Uri _manifest = null!;

    /// <summary>
    /// Czy mamy czym testowac. Gdy zmienne nie sa ustawione wcale (CI, obca maszyna) — testy sa pomijane.
    /// Gdy SA ustawione, ale plikow nie ma — test PADA, zamiast cicho przejsc.
    ///
    /// Powod tej asymetrii: wczesniej sciezki byly wpisane na sztywno, a podmiana nazw katalogow przy
    /// czyszczeniu danych klienta (17.10.2026) przestawila je na katalogi, ktorych nie ma. Osiem testow
    /// tej klasy przez kilka godzin nie sprawdzalo NICZEGO, przechodzac na zielono — czyli dokladnie
    /// najgorszy mozliwy tryb awarii testu. Wiecej taka pomylka ma byc glosna.
    /// </summary>
    static bool Sa
    {
        get
        {
            if (Nowa is null && Stara is null) return false;
            Assert.True(Nowa is not null && Stara is not null,
                "Ustaw obie zmienne albo zadnej: WU_TEST_DLL_NOWA i WU_TEST_DLL_STARA");
            Assert.True(File.Exists(Nowa), $"WU_TEST_DLL_NOWA wskazuje nieistniejacy plik: {Nowa}");
            Assert.True(File.Exists(Stara), $"WU_TEST_DLL_STARA wskazuje nieistniejacy plik: {Stara}");
            return true;
        }
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Serwer, "10.1.0.33"));
        Directory.CreateDirectory(Bin);
        _app = WebApplication.CreateBuilder().Build();
        _app.Urls.Add("http://127.0.0.1:0");
        _app.MapGet("/r2api/{**p}", (string p) =>
        {
            string f = Path.Combine(Serwer, p.Replace('/', '\\'));
            return File.Exists(f) ? Results.File(f, "application/octet-stream") : Results.NotFound();
        });
        await _app.StartAsync();
        var adres = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _manifest = new Uri(adres + "/r2api/manifest.json");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _klucz.Dispose();
        try { Directory.Delete(_tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Serwer bibliotek z biblioteka 10.1.0.33 i manifestem podpisanym kluczem testu.</summary>
    void Wystaw(DateTimeOffset? wydano = null, string? zmienSha = null, bool zlyPodpis = false)
    {
        string plik = Path.Combine(Serwer, "10.1.0.33", "R2PApi10.dll");
        if (!File.Exists(plik)) File.Copy(Nowa!, plik);
        string sha;
        using (var s = File.OpenRead(plik)) sha = Convert.ToHexString(SHA256.HashData(s));
        var m = new ManifestBibliotek(1, wydano ?? DateTimeOffset.Now,
            new() { new("10.10.33", "10.1.0.33", zmienSha ?? sha, new FileInfo(plik).Length, "10.1.0.33/R2PApi10.dll") });
        byte[] tresc = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m, ManifestBibliotek.Json));
        File.WriteAllBytes(Path.Combine(Serwer, "manifest.json"), tresc);
        string podpis = ManifestBibliotek.Podpisz(tresc, _klucz.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(Serwer, "manifest.json.sig"), zlyPodpis ? Convert.ToBase64String(new byte[64]) : podpis);
    }

    MagazynBibliotek Utworz() => new(Magazyn, _manifest, NullLogger.Instance, _klucz.ExportSubjectPublicKeyInfoPem());

    [Fact]
    public void Podpis_producenta_prawdziwej_biblioteki_i_odrzucenie_zmienionej()
    {
        if (!Sa) return;
        var p = PodpisProducenta.Sprawdz(Nowa!);
        Assert.True(p.Poprawny, p.Blad);
        Assert.Contains("SERIALNUMBER=0000228956", p.Podmiot);

        // Jeden zmieniony bajt w srodku pliku — podpis musi przestac sie zgadzac.
        string kopia = Path.Combine(_tmp, "zmieniona.dll");
        Directory.CreateDirectory(_tmp);
        File.Copy(Nowa!, kopia);
        using (var f = File.Open(kopia, FileMode.Open)) { f.Position = 5_000_000; int b = f.ReadByte(); f.Position = 5_000_000; f.WriteByte((byte)(b ^ 0xFF)); }
        Assert.False(PodpisProducenta.Sprawdz(kopia).Poprawny);

        // Plik niepodpisany (nasz proces roboczy).
        Assert.False(PodpisProducenta.Sprawdz(typeof(MagazynBibliotekTesty).Assembly.Location).Poprawny);
    }

    [Fact]
    public async Task Katalog_z_konfiguracji_ma_pierwszenstwo()
    {
        var w = await Utworz().WybierzAsync(Bin, @"D:\wskazany\", "10.10.33", default);
        Assert.Equal("konfiguracja", w.Zrodlo);
    }

    [Fact]
    public async Task Pasujacy_BIN_bez_pobierania()
    {
        if (!Sa) return;
        File.Copy(Nowa!, Path.Combine(Bin, "R2PApi10.dll"));
        var w = await Utworz().WybierzAsync(Bin, null, "10.10.33", default);
        Assert.Equal("BIN", w.Zrodlo);
        Assert.False(Directory.Exists(Magazyn));
    }

    [Fact]
    public async Task Niezgodny_BIN_pobiera_z_weryfikacja_a_potem_bierze_z_magazynu()
    {
        if (!Sa) return;
        File.Copy(Stara!, Path.Combine(Bin, "R2PApi10.dll"));   // serwer z R2 nieaktualizowany, stacje juz na .33
        Wystaw();

        var w1 = await Utworz().WybierzAsync(Bin, null, "10.10.33", default);
        Assert.Equal("pobrana", w1.Zrodlo);
        Assert.Equal("10.1.0.33", w1.WersjaPliku);
        Assert.True(File.Exists(Path.Combine(w1.Katalog!, "R2PApi10.dll")));

        var w2 = await Utworz().WybierzAsync(Bin, null, "10.10.33", default);
        Assert.Equal("magazyn", w2.Zrodlo);
    }

    [Fact]
    public async Task Podrobiony_manifest_odrzucony_zostaje_BIN_z_ostrzezeniem()
    {
        if (!Sa) return;
        File.Copy(Stara!, Path.Combine(Bin, "R2PApi10.dll"));
        Wystaw(zlyPodpis: true);
        var w = await Utworz().WybierzAsync(Bin, null, "10.10.33", default);
        Assert.Equal("BIN (niezgodna wersja)", w.Zrodlo);
        Assert.Contains("nieprawidlowy podpis", w.Uwaga);
        Assert.False(Directory.Exists(Path.Combine(Magazyn, "10.1.0.33")));
    }

    [Fact]
    public async Task Suma_niezgodna_z_manifestem_odrzucona()
    {
        if (!Sa) return;
        Wystaw(zmienSha: new string('0', 64));
        var w = await Utworz().WybierzAsync(Bin, null, "10.10.33", default);   // w BIN nic — brak biblioteki
        Assert.Equal("brak", w.Zrodlo);
        Assert.Contains("SHA-256", w.Uwaga);
        Assert.False(Directory.Exists(Path.Combine(Magazyn, "10.1.0.33")));
    }

    /// <summary>
    /// Manifest podpisany POPRAWNIE, ale obcym kluczem — przez prawdziwa sciezke magazynu, z wbudowanym
    /// kluczem produkcyjnym (<see cref="KluczManifestu.Publiczny"/>) zamiast klucza testu.
    ///
    /// To jest rzeczywisty atak: ktos stawia wlasny serwer bibliotek i podpisuje manifest swoim kluczem.
    /// Test `Podrobiony_manifest_odrzucony…` go NIE pokrywa, bo tam podpisem jest 64 bajty zer — podpis
    /// niepoprawny strukturalnie, odrzucany na innym etapie niz podpis dobrze zbudowany, lecz nie nasz.
    /// </summary>
    [Fact]
    public async Task Manifest_podpisany_obcym_kluczem_odrzucony_choc_podpis_poprawny()
    {
        if (!Sa) return;
        File.Copy(Stara!, Path.Combine(Bin, "R2PApi10.dll"));
        Wystaw();   // podpisany kluczem testu — dla magazynu z kluczem produkcyjnym jest to klucz obcy

        var zProdukcyjnym = new MagazynBibliotek(Magazyn, _manifest, NullLogger.Instance, KluczManifestu.Publiczny);
        var w = await zProdukcyjnym.WybierzAsync(Bin, null, "10.10.33", default);

        Assert.Equal("BIN (niezgodna wersja)", w.Zrodlo);
        Assert.Contains("nieprawidlowy podpis", w.Uwaga);
        // Najwazniejsze: nic nie wyladowalo w magazynie, wiec podmieniona biblioteka nie zostanie uzyta.
        Assert.False(Directory.Exists(Path.Combine(Magazyn, "10.1.0.33")));
    }

    [Fact]
    public async Task Manifest_starszy_niz_juz_widziany_odrzucony()
    {
        if (!Sa) return;
        Directory.CreateDirectory(Magazyn);
        File.WriteAllText(Path.Combine(Magazyn, "manifest-wydano.txt"), DateTimeOffset.Now.ToString("O"));
        Wystaw(wydano: DateTimeOffset.Now.AddDays(-30));
        var w = await Utworz().WybierzAsync(Bin, null, "10.10.33", default);
        Assert.Contains("starszy", w.Uwaga);
    }
}
