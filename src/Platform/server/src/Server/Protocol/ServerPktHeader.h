#pragma once

#include <Logging/Log.h>

#pragma pack(push, 1)

struct ServerPktHeader
{
    /**
     * size is the length of the payload _plus_ the length of the opcode
     */
    ServerPktHeader(U32 size, U16 cmd) : size(size)
    {
        U8 headerIndex=0;
        if (isLargePacket())
        {
            LOG_DEBUG("network", "initializing large server to client packet. Size: {}, cmd: {}", size, cmd);
            header[headerIndex++] = 0x80 | (0xFF & (size >> 16));
        }
        header[headerIndex++] = 0xFF &(size >> 8);
        header[headerIndex++] = 0xFF & size;

        header[headerIndex++] = 0xFF & cmd;
        header[headerIndex++] = 0xFF & (cmd >> 8);
    }

    U8 getHeaderLength()
    {
        // cmd = 2 bytes, size= 2||3bytes
        return 2 + (isLargePacket() ? 3 : 2);
    }

    bool isLargePacket() const
    {
        return size > 0x7FFF;
    }

    const U32 size;
    U8 header[5];
};

#pragma pack(pop)
