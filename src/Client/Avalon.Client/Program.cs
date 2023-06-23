using System;
using Avalon.Client;
using Mono.Nat;

/*
NatUtility.DeviceFound += (sender, args) =>
{
    Console.WriteLine($"Found NAT device: {args.Device}");

    var mappedPort = args.Device.CreatePortMap(new Mapping(Protocol.Udp, 8889, 8889, 0, "AvalonClient"));
    if (mappedPort != null)
        Console.WriteLine($"Mapped port {mappedPort.PublicPort} to {mappedPort.PrivatePort}.");
    else
        Console.WriteLine("Failed to map port.");
    //args.Device.
};
NatUtility.StartDiscovery();
*/



using var game = new AvalonGame();
game.Run();
