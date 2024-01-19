using System.Threading.Tasks;

namespace Avalon.Database.Migrator
{
    /// <summary>
    /// Represents an interface for a database migrator.
    /// </summary>
    public interface IDatabaseMigrator
    {
        /// <summary>
        /// Runs the method asynchronously.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous operation.
        /// </returns>
        Task RunAsync();
    }
}
