#pragma once

#include <Common/Types.h>
#include <boost/asio/ip/address.hpp>
#include <mutex>

enum Direction
{
    CLIENT_TO_SERVER,
    SERVER_TO_CLIENT
};

class WorldPacket;

class PacketLog
{
private:
    PacketLog();
    ~PacketLog();
    std::mutex _logPacketLock;
    std::once_flag _initializeFlag;

public:
    static PacketLog* instance();

    void Initialize();
    bool CanLogPacket() const { return (_file != nullptr); }
    void LogPacket(WorldPacket const& packet, Direction direction, boost::asio::ip::address const& addr, U16 port);

private:
    FILE* _file;
};

#define sPacketLog PacketLog::instance()
