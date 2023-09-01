#pragma once

#include <Common/Types.h>

#include <string>
#include <vector>

struct IpLocationRecord
{
    IpLocationRecord() :
        IpFrom(0), IpTo(0) { }
    IpLocationRecord(U32 ipFrom, U32 ipTo, std::string countryCode, std::string countryName) :
        IpFrom(ipFrom), IpTo(ipTo), CountryCode(std::move(countryCode)), CountryName(std::move(countryName)) { }

    U32 IpFrom;
    U32 IpTo;
    std::string CountryCode;
    std::string CountryName;
};

class IpLocationStore
{
public:
    IpLocationStore();
    ~IpLocationStore();
    static IpLocationStore* instance();

    void Load();
    IpLocationRecord const* GetLocationRecord(std::string const& ipAddress) const;

private:
    std::vector<IpLocationRecord> _ipLocationStore;
};

#define sIPLocation IpLocationStore::instance()
