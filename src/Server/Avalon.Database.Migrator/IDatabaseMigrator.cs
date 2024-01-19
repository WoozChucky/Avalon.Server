using System.Threading.Tasks;

namespace Avalon.Database.Migrator
{
    public interface IDatabaseMigrator
    {
        Task RunAsync();
    }
}
