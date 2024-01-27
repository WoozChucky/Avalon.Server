namespace Avalon.Api.Services;

public interface IWorkerService
{
    Task StartWorker(CancellationToken cancellationToken);
}
