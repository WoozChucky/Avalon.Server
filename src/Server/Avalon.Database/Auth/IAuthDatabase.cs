namespace Avalon.Database.Auth
{
    public interface IAuthDatabase
    {
        IAccountTable Account { get; }
        IAccountAccessTable AccountAccess { get; }
    }

    public class AuthDatabase : IAuthDatabase
    {
        public IAccountTable Account { get; }
        public IAccountAccessTable AccountAccess { get; }
        
        public AuthDatabase()
        {
            Account = new AccountTable();

        }
    }
}
