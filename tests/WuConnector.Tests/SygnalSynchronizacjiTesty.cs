using WuConnector.Core.Cykl;

namespace WuConnector.Tests;

public class SygnalSynchronizacjiTesty
{
    [Fact]
    public async Task Zlecenie_przerywa_oczekiwanie()
    {
        var s = new SygnalSynchronizacji();
        var czekanie = s.CzekajAsync(TimeSpan.FromMinutes(30), default);
        s.Zlec();
        Assert.True(await czekanie.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Bez_zlecenia_mija_odstep()
        => Assert.False(await new SygnalSynchronizacji().CzekajAsync(TimeSpan.FromMilliseconds(50), default));

    [Fact]
    public async Task Kilka_zlecen_w_trakcie_cyklu_to_jeden_kolejny_cykl()
    {
        var s = new SygnalSynchronizacji();
        s.Zlec(); s.Zlec(); s.Zlec();
        Assert.True(await s.CzekajAsync(TimeSpan.FromSeconds(1), default));
        Assert.False(await s.CzekajAsync(TimeSpan.FromMilliseconds(50), default));
    }
}
