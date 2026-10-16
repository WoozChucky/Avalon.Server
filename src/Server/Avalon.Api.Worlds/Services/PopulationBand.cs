using Avalon.Api.Contract;
using DomainStatus = Avalon.Domain.Auth.WorldStatus;

namespace Avalon.Api.Worlds.Services;

/// <summary>Bands a world's places taken into the coarse population the public realm list shows.</summary>
public static class PopulationBand
{
    /// <summary>
    /// The band of <paramref name="placesTaken"/> out of the world's <paramref name="playerLimit"/>, or, for a world with no
    /// limit, out of the configured <paramref name="reference"/> (such a world never reads Full). Thirds of the capacity:
    /// Low below one, Medium below two, High from there. Null for a world that is not up, or with neither a limit nor a reference.
    /// </summary>
    public static PublicPopulation? Of(DomainStatus status, int placesTaken, int? playerLimit, int? reference)
    {
        if (status is not (DomainStatus.Online or DomainStatus.Full))
        {
            return null;
        }

        if (status == DomainStatus.Full)
        {
            return PublicPopulation.Full;
        }

        int? capacity = playerLimit ?? reference;
        if (capacity is not > 0)
        {
            return null;
        }

        long taken = (long)placesTaken * 3;
        if (taken < capacity)
        {
            return PublicPopulation.Low;
        }

        return taken < (long)capacity * 2 ? PublicPopulation.Medium : PublicPopulation.High;
    }
}
