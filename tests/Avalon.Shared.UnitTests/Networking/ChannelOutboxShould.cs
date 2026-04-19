// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class ChannelOutboxShould
{
    [Fact]
    public async Task WriteEnqueuedPacket_ToStream_AfterConnect()
    {
        var ms = new MemoryStream();
        var stream = new PacketStream(ms);
        var outbox = new ChannelOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64);

        outbox.Connect(stream);

        outbox.Enqueue(SPingPacket.Create(0L, 0L, 0L, 0L));

        // Poll until bytes arrive or 2s elapses
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (ms.Length == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(ms.Length > 0, "Expected bytes written to stream");
        await outbox.DisposeAsync();
    }
}
