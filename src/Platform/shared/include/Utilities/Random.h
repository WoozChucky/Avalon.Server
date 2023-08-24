#pragma once

#include <Common/Types.h>
#include "Duration.h"
#include <limits>

/* Return a random number in the range min..max. */
S32 irand(S32 min, S32 max);

/* Return a random number in the range min..max (inclusive). */
U32 urand(U32 min, U32 max);

/* Return a random millisecond value between min and max seconds. Functionally equivalent to urand(min*IN_MILLISECONDS, max*IN_MILLISECONDS). */
U32 urandms(U32 min, U32 max);

/* Return a random number in the range 0 .. UINT32_MAX. */
U32 rand32();

/* Return a random time in the range min..max (up to millisecond precision). Only works for values where millisecond difference is a valid uint32. */
Milliseconds randtime(Milliseconds min, Milliseconds max);

/* Return a random number in the range min..max */
float frand(float min, float max);

/* Return a random double from 0.0 to 1.0 (exclusive). */
double rand_norm();

/* Return a random double from 0.0 to 100.0 (exclusive). */
double rand_chance();

/* Return a random number in the range 0..count (exclusive) with each value having a different chance of happening */
U32 urandweighted(size_t count, double const* chances);

/* Return true if a random roll fits in the specified chance (range 0-100). */
inline bool roll_chance_f(float chance)
{
    return chance > rand_chance();
}

/* Return true if a random roll fits in the specified chance (range 0-100). */
inline bool roll_chance_i(int chance)
{
    return chance > irand(0, 99);
}

/*
* Wrapper satisfying UniformRandomNumberGenerator concept for use in <random> algorithms
*/
class RandomEngine
{
public:
    using result_type = U32;

    static constexpr result_type min() { return std::numeric_limits<result_type>::min(); }
    static constexpr result_type max() { return std::numeric_limits<result_type>::max(); }
    result_type operator()() const { return rand32(); }

    static RandomEngine& Instance();
};
