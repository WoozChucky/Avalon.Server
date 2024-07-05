using Avalon.Database;
using Avalon.Domain.Auth;

namespace Avalon.Auth.Database.Repositories;

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
