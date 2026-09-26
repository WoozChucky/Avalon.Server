namespace Avalon.World.Vendors;

/// <summary>
/// An instance that keeps vendor stock (spec #432). MapInstance is the only one. The interface lets
/// the vendor code be tested against a substituted instance, and keeps the stock out of
/// IMapInstance, which is part of the modding API. Tick thread only.
/// </summary>
public interface IVendorHost
{
    VendorStocks Vendors { get; }
}
