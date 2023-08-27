#pragma once

#include "Packets/ByteBuffer.h"
#include "Common/Types.h"
#include "Utilities/Duration.h"

#include "Protocol/Opcodes.h"

class WorldPacket : public ByteBuffer
{
public:
    // just container for later use
    WorldPacket() : ByteBuffer(0) { }

    explicit WorldPacket(U16 opcode, size_t res = 200) :
        ByteBuffer(res), m_opcode(opcode) { }

    WorldPacket(WorldPacket&& packet) noexcept :
        ByteBuffer(std::move(packet)), m_opcode(packet.m_opcode) { }

    WorldPacket(WorldPacket&& packet, TimePoint receivedTime) :
        ByteBuffer(std::move(packet)), m_opcode(packet.m_opcode), m_receivedTime(receivedTime) { }

    WorldPacket(WorldPacket const& right) :
        ByteBuffer(right), m_opcode(right.m_opcode) { }

    WorldPacket& operator=(WorldPacket const& right)
    {
        if (this != &right)
        {
            m_opcode = right.m_opcode;
            ByteBuffer::operator=(right);
        }

        return *this;
    }

    WorldPacket& operator=(WorldPacket&& right) noexcept
    {
        if (this != &right)
        {
            m_opcode = right.m_opcode;
            ByteBuffer::operator=(std::move(right));
        }

        return *this;
    }

    WorldPacket(U16 opcode, MessageBuffer&& buffer) :
        ByteBuffer(std::move(buffer)), m_opcode(opcode) { }

    void Initialize(U16 opcode, size_t newres = 200)
    {
        clear();
        _storage.reserve(newres);
        m_opcode = opcode;
    }

    [[nodiscard]] U16 GetOpcode() const { return m_opcode; }
    void SetOpcode(U16 opcode) { m_opcode = opcode; }

    [[nodiscard]] TimePoint GetReceivedTime() const { return m_receivedTime; }

protected:
    U16 m_opcode{NULL_OPCODE};
    TimePoint m_receivedTime; // only set for a specific set of opcodes, for performance reasons.
};
