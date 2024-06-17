namespace Avalon.Database.Auth
{
    public interface IAuthDatabase
    {
        IAccountRepository Account { get; }
        IMFASetupRepository MFASetup { get; }
        IDeviceRepository Device { get; }
        IWorldRepository World { get; }
    }

    public class AuthDatabase : IAuthDatabase
    {
        public IAccountRepository Account { get; }
        public IMFASetupRepository MFASetup { get; }
        public IDeviceRepository Device { get; }
        public IWorldRepository World { get; }

        public AuthDatabase(
            IAccountRepository accountRepository, 
            IMFASetupRepository mfaSetupRepository, 
            IDeviceRepository deviceRepository, 
            IWorldRepository worldRepository
        )
        {
            Account = accountRepository;
            MFASetup = mfaSetupRepository;
            Device = deviceRepository;
            World = worldRepository;
        }
    }
}
