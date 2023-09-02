#include <Game/Server/Packet.h>

#include <Common/Debugging/Errors.h>

WorldPackets::Packet::Packet(WorldPacket&& worldPacket) : _worldPacket(std::move(worldPacket))
{
}

WorldPackets::ServerPacket::ServerPacket(OpcodeServer opcode, size_t initialSize /*= 200*/) : Packet(WorldPacket(opcode, initialSize))
{
}

void WorldPackets::ServerPacket::Read()
{
    ASSERT(!"Read not implemented for server packets.");
}

WorldPackets::ClientPacket::ClientPacket(OpcodeClient expectedOpcode, WorldPacket&& packet) : Packet(std::move(packet))
{
    ASSERT(GetOpcode() == expectedOpcode);
}

WorldPackets::ClientPacket::ClientPacket(WorldPacket&& packet)
    : Packet(std::move(packet))
{
}

WorldPacket const* WorldPackets::ClientPacket::Write()
{
    ASSERT(!"Write not allowed for client packets.");
    // Shut up some compilers
    return nullptr;
}
