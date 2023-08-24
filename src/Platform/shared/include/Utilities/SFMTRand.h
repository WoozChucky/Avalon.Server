#pragma once

#include <Common/Types.h>
#include <SFMT.h>
#include <new>

/*
 * C++ Wrapper for SFMT
 */
class SFMTRand
{
public:
    SFMTRand();
    U32 RandomUInt32(); // Output random bits
    void* operator new(size_t size, std::nothrow_t const&);
    void operator delete(void* ptr, std::nothrow_t const&);
    void* operator new(size_t size);
    void operator delete(void* ptr);
    void* operator new[](size_t size, std::nothrow_t const&);
    void operator delete[](void* ptr, std::nothrow_t const&);
    void* operator new[](size_t size);
    void operator delete[](void* ptr);
private:
    sfmt_t _state;
};
