using Avalon.Domain.Auth;

namespace Avalon.Database.Auth.Repositories;

public interface IDeviceRepository : IRepository<Device, Guid>
{
    
}

public class DeviceRepository : EntityFrameworkRepository<Device, Guid>, IDeviceRepository
{
    public DeviceRepository(AuthDbContext dbContext)
        : base(dbContext)
    {
    }
}
