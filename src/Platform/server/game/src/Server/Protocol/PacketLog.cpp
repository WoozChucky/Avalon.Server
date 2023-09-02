#include <Game/Server/Protocol/PacketLog.h>

#include <Common/Configuration/ConfigManager.h>
#include <Common/Utilities/Timer.h>
#include <Game/Time/GameTime.h>
#include <Game/Server/WorldPacket.h>

#pragma pack(push, 1)

// Packet logging structures in PKT 3.1 format
struct LogHeader
{
    char Signature[3];
    U16 FormatVersion;
    U8 SnifferId;
    U32 Build;
    char Locale[4];
    U8 SessionKey[40];
    U32 SniffStartUnixtime;
    U32 SniffStartTicks;
    U32 OptionalDataSize;
};

struct PacketHeader
{
    // used to uniquely identify a connection
    struct OptionalData
    {
        U8 SocketIPBytes[16];
        U32 SocketPort;
    };

    U32 Direction;
    U32 ConnectionId;
    U32 ArrivalTicks;
    U32 OptionalDataSize;
    U32 Length;
    OptionalData OptionalData;
    U32 Opcode;
};

#pragma pack(pop)

PacketLog::PacketLog() : _file(nullptr)
{
    std::call_once(_initializeFlag, &PacketLog::Initialize, this);
}

PacketLog::~PacketLog()
{
    if (_file)
    {
        fclose(_file);
    }

    _file = nullptr;
}

PacketLog* PacketLog::instance()
{
    static PacketLog instance;
    return &instance;
}

void PacketLog::Initialize()
{
    std::string logsDir = sConfigMgr->GetOption<std::string>("LogsDir", "");

    if (!logsDir.empty() && (logsDir.at(logsDir.length() - 1) != '/') && (logsDir.at(logsDir.length() - 1) != '\\'))
    {
        logsDir.push_back('/');
    }

    std::string logname = sConfigMgr->GetOption<std::string>("PacketLogFile", "");
    if (!logname.empty())
    {
        _file = fopen((logsDir + logname).c_str(), "wb");

        LogHeader header;
        header.Signature[0] = 'P'; header.Signature[1] = 'K'; header.Signature[2] = 'T';
        header.FormatVersion = 0x0301;
        header.SnifferId = 'T';
        header.Build = 12340;
        header.Locale[0] = 'e'; header.Locale[1] = 'n'; header.Locale[2] = 'U'; header.Locale[3] = 'S';
        std::memset(header.SessionKey, 0, sizeof(header.SessionKey));
        header.SniffStartUnixtime = GameTime::GetGameTime().count();
        header.SniffStartTicks = getMSTime();
        header.OptionalDataSize = 0;

        if (CanLogPacket())
        {
            fwrite(&header, sizeof(header), 1, _file);
        }
    }
}

void PacketLog::LogPacket(WorldPacket const& packet, Direction direction, boost::asio::ip::address const& addr, U16 port)
{
    std::lock_guard<std::mutex> lock(_logPacketLock);

    PacketHeader header;
    header.Direction = direction == CLIENT_TO_SERVER ? 0x47534d43 : 0x47534d53;
    header.ConnectionId = 0;
    header.ArrivalTicks = getMSTime();

    header.OptionalDataSize = sizeof(header.OptionalData);
    memset(header.OptionalData.SocketIPBytes, 0, sizeof(header.OptionalData.SocketIPBytes));

    if (addr.is_v4())
    {
        auto bytes = addr.to_v4().to_bytes();
        memcpy(header.OptionalData.SocketIPBytes, bytes.data(), bytes.size());
    }
    else if (addr.is_v6())
    {
        auto bytes = addr.to_v6().to_bytes();
        memcpy(header.OptionalData.SocketIPBytes, bytes.data(), bytes.size());
    }

    header.OptionalData.SocketPort = port;
    header.Length = packet.size() + sizeof(header.Opcode);
    header.Opcode = packet.GetOpcode();

    fwrite(&header, sizeof(header), 1, _file);

    if (!packet.empty())
    {
        fwrite(packet.contents(), 1, packet.size(), _file);
    }

    fflush(_file);
}
