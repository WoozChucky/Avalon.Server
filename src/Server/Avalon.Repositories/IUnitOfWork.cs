namespace Avalon.Repositories
{
    public interface IUnitOfWork
    {
        IAccountRepository Accounts { get; }
    }
}
