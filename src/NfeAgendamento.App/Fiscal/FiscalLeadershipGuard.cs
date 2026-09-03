using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App.Fiscal;

public interface IFiscalLeadershipGuard
{
    void EnsureCanStartFiscalOperation();
}

public sealed class FiscalLeadershipLostException : InvalidOperationException
{
    public FiscalLeadershipLostException(string message)
        : base(message)
    {
    }
}

public sealed class SharedQueueFiscalLeadershipGuard : IFiscalLeadershipGuard
{
    private readonly SharedQueueCentralService _centralService;

    public SharedQueueFiscalLeadershipGuard(SharedQueueCentralService centralService)
    {
        _centralService = centralService ?? throw new ArgumentNullException(nameof(centralService));
    }

    public void EnsureCanStartFiscalOperation()
    {
        if (!_centralService.CanProcessWork())
        {
            throw new FiscalLeadershipLostException(
                "A liderança da fila mudou antes do envio à SEFAZ. Nenhuma nova consulta fiscal foi iniciada; refaça a consulta explicitamente.");
        }
    }
}
