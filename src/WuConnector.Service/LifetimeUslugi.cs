using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using WuConnector.Core.Cykl;

namespace WuConnector.Service;

/// <summary>Cykl uslugi Windows z obsluga polecenia „synchronizuj teraz" z okna connectora.</summary>
public sealed class LifetimeUslugi(IHostEnvironment env, IHostApplicationLifetime zycie, ILoggerFactory logi,
                                   IOptions<HostOptions> opcje, IOptions<WindowsServiceLifetimeOptions> opcjeUslugi,
                                   SygnalSynchronizacji sygnal)
    : WindowsServiceLifetime(env, zycie, logi, opcje, opcjeUslugi)
{
    readonly ILogger _log = logi.CreateLogger<LifetimeUslugi>();

    protected override void OnCustomCommand(int command)
    {
        if (command == SygnalSynchronizacji.Polecenie)
        {
            _log.LogInformation("Synchronizacja zlecona ręcznie z okna connectora");
            sygnal.Zlec();
        }
        else base.OnCustomCommand(command);
    }
}
