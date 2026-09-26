using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>Vendor stock repositories for tests that build StaticData or World.</summary>
internal static class VendorRepositories
{
    /// <summary>Read on every call, so a test can change what the next reload sees.</summary>
    public static IVendorStockRepository Of(Func<IReadOnlyCollection<VendorStock>> rows)
    {
        var repository = Substitute.For<IVendorStockRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(rows()));
        return repository;
    }
}
