#pragma once

#include <Common/Types.h>
#include <array>
#include <string>
#include <vector>

struct bignum_st;

class BigNumber
{
public:
    BigNumber();
    BigNumber(BigNumber const& bn);
    BigNumber(U32 v) : BigNumber() { SetDword(v); }
    BigNumber(S32 v) : BigNumber() { SetDword(v); }
    BigNumber(std::string const& v) : BigNumber() { SetHexStr(v); }

    template <size_t Size>
    BigNumber(std::array<U8, Size> const& v, bool littleEndian = true) : BigNumber() { SetBinary(v.data(), Size, littleEndian); }

    ~BigNumber();

    void SetDword(S32);
    void SetDword(U32);
    void SetQword(U64);
    void SetBinary(U8 const* bytes, S32 len, bool littleEndian = true);

    template <typename Container>
    auto SetBinary(Container const& c, bool littleEndian = true) -> std::enable_if_t<!std::is_pointer_v<std::decay_t<Container>>> { SetBinary(std::data(c), std::size(c), littleEndian); }

    bool SetHexStr(char const* str);
    bool SetHexStr(std::string const& str) { return SetHexStr(str.c_str()); }

    void SetRand(S32 numbits);

    BigNumber& operator=(BigNumber const& bn);

    BigNumber& operator+=(BigNumber const& bn);
    BigNumber operator+(BigNumber const& bn) const
    {
        BigNumber t(*this);
        return t += bn;
    }

    BigNumber& operator-=(BigNumber const& bn);
    BigNumber operator-(BigNumber const& bn) const
    {
        BigNumber t(*this);
        return t -= bn;
    }

    BigNumber& operator*=(BigNumber const& bn);
    BigNumber operator*(BigNumber const& bn) const
    {
        BigNumber t(*this);
        return t *= bn;
    }

    BigNumber& operator/=(BigNumber const& bn);
    BigNumber operator/(BigNumber const& bn) const
    {
        BigNumber t(*this);
        return t /= bn;
    }

    BigNumber& operator%=(BigNumber const& bn);
    BigNumber operator%(BigNumber const& bn) const
    {
        BigNumber t(*this);
        return t %= bn;
    }

    BigNumber& operator<<=(int n);
    BigNumber operator<<(int n) const
    {
        BigNumber t(*this);
        return t <<= n;
    }

    [[nodiscard]] int CompareTo(BigNumber const& bn) const;
    bool operator<=(BigNumber const& bn) const { return (CompareTo(bn) <= 0); }
    bool operator==(BigNumber const& bn) const { return (CompareTo(bn) == 0); }
    bool operator>=(BigNumber const& bn) const { return (CompareTo(bn) >= 0); }
    bool operator<(BigNumber const& bn) const { return (CompareTo(bn) < 0); }
    bool operator>(BigNumber const& bn) const { return (CompareTo(bn) > 0); }

    [[nodiscard]] bool IsZero() const;
    [[nodiscard]] bool IsNegative() const;

    [[nodiscard]] BigNumber ModExp(BigNumber const& bn1, BigNumber const& bn2) const;
    [[nodiscard]] BigNumber Exp(BigNumber const&) const;

    [[nodiscard]] S32 GetNumBytes() const;

    struct bignum_st* BN() { return _bn; }
    [[nodiscard]] struct bignum_st const* BN() const { return _bn; }

    [[nodiscard]] U32 AsDword() const;

    void GetBytes(U8* buf, size_t bufsize, bool littleEndian = true) const;
    [[nodiscard]] std::vector<U8> ToByteVector(S32 minSize = 0, bool littleEndian = true) const;

    template <std::size_t Size>
    std::array<U8, Size> ToByteArray(bool littleEndian = true) const
    {
        std::array<U8, Size> buf;
        GetBytes(buf.data(), Size, littleEndian);
        return buf;
    }

    [[nodiscard]] std::string AsHexStr() const;
    [[nodiscard]] std::string AsDecStr() const;

private:
    struct bignum_st* _bn;

};
