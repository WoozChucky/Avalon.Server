using Avalon.Configuration;

namespace Avalon.Database.Auth
{
    public interface IAuthDatabase
    {
        IAccountRepository Account { get; }
        IMFASetupRepository MFASetup { get; }
    }

    public class AuthDatabase : IAuthDatabase
    {
        public IAccountRepository Account { get; }
        public IMFASetupRepository MFASetup { get; }
        
        public AuthDatabase(IAccountRepository accountRepository, IMFASetupRepository mfaSetupRepository)
        {
            Account = accountRepository;
            MFASetup = mfaSetupRepository;
        }
    }
}
