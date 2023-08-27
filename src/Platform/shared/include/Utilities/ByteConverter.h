#pragma once

#include <Common/Types.h>

#include <algorithm>

namespace ByteConverter
{
    template<size_t T>
    inline void convert(char* val)
    {
        std::swap(*val, *(val + T - 1));
        convert < T - 2 > (val + 1);
    }

    template<> inline void convert<0>(char*) { }
    template<> inline void convert<1>(char*) { }            // ignore central byte

    template<typename T> inline void apply(T* val)
    {
        convert<sizeof(T)>((char*)(val));
    }
}

#warning This is a hack to get the server to compile. It should be fixed properly.
#if 0
template<typename T> inline void EndianConvert(T& val) { ByteConverter::apply<T>(&val); }
template<typename T> inline void EndianConvertReverse(T&) { }
template<typename T> inline void EndianConvertPtr(void* val) { ByteConverter::apply<T>(val); }
template<typename T> inline void EndianConvertPtrReverse(void*) { }
#else
template<typename T> inline void EndianConvert(T&) { }
template<typename T> inline void EndianConvertReverse(T& val) { ByteConverter::apply<T>(&val); }
template<typename T> inline void EndianConvertPtr(void*) { }
template<typename T> inline void EndianConvertPtrReverse(void* val) { ByteConverter::apply<T>(val); }
#endif

template<typename T> void EndianConvert(T*);         // will generate link error
template<typename T> void EndianConvertReverse(T*);  // will generate link error

inline void EndianConvert(U8&) { }
inline void EndianConvert( S8&) { }
inline void EndianConvertReverse(U8&) { }
inline void EndianConvertReverse( S8&) { }
