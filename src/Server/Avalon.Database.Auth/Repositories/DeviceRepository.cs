using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IDeviceRepository : IRepository<Device, Guid>
{

}

public class DeviceRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<Device, Guid, AuthDbContext>(contextFactory), IDeviceRepository
{
}
