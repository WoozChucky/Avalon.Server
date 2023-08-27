#pragma once

#include <Common/Types.h>
#include <Utilities/ByteConverter.h>

#include <array>
#include <cstring>
#include <string>
#include <vector>

class MessageBuffer;

// Root of ByteBuffer exception hierarchy
class ByteBufferException : public std::exception
{
public:
    ~ByteBufferException() noexcept override = default;

    [[nodiscard]] char const* what() const noexcept override { return msg_.c_str(); }

protected:
    std::string & message() noexcept { return msg_; }

private:
    std::string msg_;
};

class ByteBufferPositionException : public ByteBufferException
{
public:
    ByteBufferPositionException(bool add, size_t pos, size_t size, size_t valueSize);

    ~ByteBufferPositionException() noexcept override = default;
};

class ByteBufferSourceException : public ByteBufferException
{
public:
    ByteBufferSourceException(size_t pos, size_t size, size_t valueSize);

    ~ByteBufferSourceException() noexcept override = default;
};

class ByteBufferInvalidValueException : public ByteBufferException
{
public:
    ByteBufferInvalidValueException(char const* type, char const* value);

    ~ByteBufferInvalidValueException() noexcept override = default;
};

class ByteBuffer
{
public:
    constexpr static size_t DEFAULT_SIZE = 0x1000;

    // constructor
    ByteBuffer()
    {
        _storage.reserve(DEFAULT_SIZE);
    }

    ByteBuffer(size_t reserve) : _rpos(0), _wpos(0)
    {
        _storage.reserve(reserve);
    }

    ByteBuffer(ByteBuffer&& buf) noexcept :
        _rpos(buf._rpos), _wpos(buf._wpos), _storage(std::move(buf._storage))
    {
        buf._rpos = 0;
        buf._wpos = 0;
    }

    ByteBuffer(ByteBuffer const& right) = default;
    ByteBuffer(MessageBuffer&& buffer);
    virtual ~ByteBuffer() = default;

    ByteBuffer& operator=(ByteBuffer const& right)
    {
        if (this != &right)
        {
            _rpos = right._rpos;
            _wpos = right._wpos;
            _storage = right._storage;
        }

        return *this;
    }

    ByteBuffer& operator=(ByteBuffer&& right) noexcept
    {
        if (this != &right)
        {
            _rpos = right._rpos;
            right._rpos = 0;
            _wpos = right._wpos;
            right._wpos = 0;
            _storage = std::move(right._storage);
        }

        return *this;
    }

    void clear()
    {
        _storage.clear();
        _rpos = _wpos = 0;
    }

    template <typename T>
    void append(T value)
    {
        static_assert(std::is_fundamental<T>::value, "append(compound)");
        EndianConvert(value);
        append((U8*)&value, sizeof(value));
    }

    template <typename T>
    void put(std::size_t pos, T value)
    {
        static_assert(std::is_fundamental<T>::value, "append(compound)");
        EndianConvert(value);
        put(pos, (U8*)&value, sizeof(value));
    }

    ByteBuffer& operator<<(bool value)
    {
        append<U8>(value ? 1 : 0);
        return *this;
    }

    ByteBuffer& operator<<(U8 value)
    {
        append<U8>(value);
        return *this;
    }

    ByteBuffer& operator<<(U16 value)
    {
        append<U16>(value);
        return *this;
    }

    ByteBuffer& operator<<(U32 value)
    {
        append<U32>(value);
        return *this;
    }

    ByteBuffer& operator<<(U64 value)
    {
        append<U64>(value);
        return *this;
    }

    // signed as in 2e complement
    ByteBuffer& operator<<(S8 value)
    {
        append<S8>(value);
        return *this;
    }

    ByteBuffer& operator<<(S16 value)
    {
        append<S16>(value);
        return *this;
    }

    ByteBuffer& operator<<(S32 value)
    {
        append<S32>(value);
        return *this;
    }

    ByteBuffer& operator<<(S64 value)
    {
        append<S64>(value);
        return *this;
    }

    // floating points
    ByteBuffer& operator<<(float value)
    {
        append<float>(value);
        return *this;
    }

    ByteBuffer& operator<<(double value)
    {
        append<double>(value);
        return *this;
    }

    ByteBuffer& operator<<(std::string_view value)
    {
        if (size_t len = value.length())
        {
            append(reinterpret_cast<U8 const*>(value.data()), len);
        }

        append(static_cast<U8>(0));
        return *this;
    }

    ByteBuffer& operator<<(std::string const& str)
    {
        return operator<<(std::string_view(str));
    }

    ByteBuffer& operator<<(char const* str)
    {
        return operator<<(std::string_view(str ? str : ""));
    }

    ByteBuffer& operator>>(bool& value)
    {
        value = read<char>() > 0;
        return *this;
    }

    ByteBuffer& operator>>(U8& value)
    {
        value = read<U8>();
        return *this;
    }

    ByteBuffer& operator>>(U16& value)
    {
        value = read<U16>();
        return *this;
    }

    ByteBuffer& operator>>(U32& value)
    {
        value = read<U32>();
        return *this;
    }

    ByteBuffer& operator>>(U64& value)
    {
        value = read<U64>();
        return *this;
    }

    //signed as in 2e complement
    ByteBuffer& operator>>(S8& value)
    {
        value = read<S8>();
        return *this;
    }

    ByteBuffer& operator>>(S16& value)
    {
        value = read<S16>();
        return *this;
    }

    ByteBuffer& operator>>(S32& value)
    {
        value = read<S32>();
        return *this;
    }

    ByteBuffer& operator>>(S64& value)
    {
        value = read<S64>();
        return *this;
    }

    ByteBuffer& operator>>(float& value);
    ByteBuffer& operator>>(double& value);

    ByteBuffer& operator>>(std::string& value)
    {
        value = ReadCString(true);
        return *this;
    }

    U8& operator[](size_t const pos)
    {
        if (pos >= size())
        {
            throw ByteBufferPositionException(false, pos, 1, size());
        }

        return _storage[pos];
    }

    U8 const& operator[](size_t const pos) const
    {
        if (pos >= size())
        {
            throw ByteBufferPositionException(false, pos, 1, size());
        }

        return _storage[pos];
    }

    [[nodiscard]] size_t rpos() const { return _rpos; }

    size_t rpos(size_t rpos_)
    {
        _rpos = rpos_;
        return _rpos;
    }

    void rfinish()
    {
        _rpos = wpos();
    }

    [[nodiscard]] size_t wpos() const { return _wpos; }

    size_t wpos(size_t wpos_)
    {
        _wpos = wpos_;
        return _wpos;
    }

    template<typename T>
    void read_skip() { read_skip(sizeof(T)); }

    void read_skip(size_t skip)
    {
        if (_rpos + skip > size())
        {
            throw ByteBufferPositionException(false, _rpos, skip, size());
        }

        _rpos += skip;
    }

    template <typename T> T read()
    {
        T r = read<T>(_rpos);
        _rpos += sizeof(T);
        return r;
    }

    template <typename T> [[nodiscard]] T read(size_t pos) const
    {
        if (pos + sizeof(T) > size())
        {
            throw ByteBufferPositionException(false, pos, sizeof(T), size());
        }

        T val = *((T const*)&_storage[pos]);
        EndianConvert(val);
        return val;
    }

    void read(U8* dest, size_t len)
    {
        if (_rpos  + len > size())
        {
            throw ByteBufferPositionException(false, _rpos, len, size());
        }

        std::memcpy(dest, &_storage[_rpos], len);
        _rpos += len;
    }

    template <size_t Size>
    void read(std::array<U8, Size>& arr)
    {
        read(arr.data(), Size);
    }

    void readPackGUID(U64& guid)
    {
        if (rpos() + 1 > size())
        {
            throw ByteBufferPositionException(false, _rpos, 1, size());
        }

        guid = 0;

        U8 guidmark = 0;
        (*this) >> guidmark;

        for (int i = 0; i < 8; ++i)
        {
            if (guidmark & (U8(1) << i))
            {
                if (rpos() + 1 > size())
                {
                    throw ByteBufferPositionException(false, _rpos, 1, size());
                }

                U8 bit;
                (*this) >> bit;
                guid |= (U64(bit) << (i * 8));
            }
        }
    }

    std::string ReadCString(bool requireValidUtf8 = true);
    U32 ReadPackedTime();

    ByteBuffer& ReadPackedTime(U32& time)
    {
        time = ReadPackedTime();
        return *this;
    }

    U8* contents()
    {
        if (_storage.empty())
        {
            throw ByteBufferException();
        }

        return _storage.data();
    }

    [[nodiscard]] U8 const* contents() const
    {
        if (_storage.empty())
        {
            throw ByteBufferException();
        }

        return _storage.data();
    }

    [[nodiscard]] size_t size() const { return _storage.size(); }
    [[nodiscard]] bool empty() const { return _storage.empty(); }

    void resize(size_t newsize)
    {
        _storage.resize(newsize, 0);
        _rpos = 0;
        _wpos = size();
    }

    void reserve(size_t ressize)
    {
        if (ressize > size())
        {
            _storage.reserve(ressize);
        }
    }

    void shrink_to_fit()
    {
        _storage.shrink_to_fit();
    }

    void append(const char *src, size_t cnt)
    {
        return append((const U8 *)src, cnt);
    }

    template<class T> void append(const T* src, size_t cnt)
    {
        return append((const U8*)src, cnt * sizeof(T));
    }

    void append(U8 const* src, size_t cnt);

    void append(ByteBuffer const& buffer)
    {
        if (buffer.wpos())
        {
            append(buffer.contents(), buffer.wpos());
        }
    }

    template <size_t Size>
    void append(std::array<U8, Size> const& arr)
    {
        append(arr.data(), Size);
    }

    // can be used in SMSG_MONSTER_MOVE opcode
    void appendPackXYZ(float x, float y, float z)
    {
        U32 packed = 0;
        packed |= ((int)(x / 0.25f) & 0x7FF);
        packed |= ((int)(y / 0.25f) & 0x7FF) << 11;
        packed |= ((int)(z / 0.25f) & 0x3FF) << 22;
        *this << packed;
    }

    void appendPackGUID(U64 guid)
    {
        U8 packGUID[8 + 1];
        packGUID[0] = 0;
        size_t size = 1;

        for (U8 i = 0; guid != 0;++i)
        {
            if (guid & 0xFF)
            {
                packGUID[0] |= U8(1 << i);
                packGUID[size] =  U8(guid & 0xFF);
                ++size;
            }

            guid >>= 8;
        }

        append(packGUID, size);
    }

    void AppendPackedTime(time_t time);
    void put(size_t pos, const U8 *src, size_t cnt);
    void print_storage() const;
    void textlike() const;
    void hexlike() const;

protected:
    size_t _rpos{0}, _wpos{0};
    std::vector<U8> _storage;
};

/// @todo Make a ByteBuffer.cpp and move all this inlining to it.
template<>
inline std::string ByteBuffer::read<std::string>()
{
    std::string tmp;
    *this >> tmp;
    return tmp;
}

template<>
inline void ByteBuffer::read_skip<char*>()
{
    std::string temp;
    *this >> temp;
}

template<>
inline void ByteBuffer::read_skip<char const*>()
{
    read_skip<char*>();
}

template<>
inline void ByteBuffer::read_skip<std::string>()
{
    read_skip<char*>();
}
