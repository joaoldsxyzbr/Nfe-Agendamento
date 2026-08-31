namespace NfeAgendamento.App.Fiscal;

public sealed class FiscalOperationGate
{
    internal SemaphoreSlim Semaphore { get; } = new(1, 1);
}
