#include <Utilities/Random.h>

#include <Utilities/SFMTRand.h>
#include <Debugging/Errors.h>

#include <memory>
#include <random>

static thread_local std::unique_ptr<SFMTRand> sfmtRand;
static RandomEngine engine;

static SFMTRand* GetRng()
{
    if (!sfmtRand)
    {
        sfmtRand = std::make_unique<SFMTRand>();
    }

    return sfmtRand.get();
}

S32 irand(S32 min, S32 max)
{
    ASSERT(max >= min);
    std::uniform_int_distribution<S32> uid(min, max);
    return uid(engine);
}

U32 urand(U32 min, U32 max)
{
    ASSERT(max >= min);
    std::uniform_int_distribution<U32> uid(min, max);
    return uid(engine);
}

U32 urandms(U32 min, U32 max)
{
    ASSERT(std::numeric_limits<U32>::max() / Milliseconds::period::den >= max);
    return urand(min * Milliseconds::period::den, max * Milliseconds::period::den);
}

float frand(float min, float max)
{
    ASSERT(max >= min);
    std::uniform_real_distribution<float> urd(min, max);
    return urd(engine);
}

Milliseconds randtime(Milliseconds min, Milliseconds max)
{
    long long diff = max.count() - min.count();
    ASSERT(diff >= 0);
    ASSERT(diff <= (U32) - 1);
    return min + Milliseconds(urand(0, diff));
}

U32 rand32()
{
    return GetRng()->RandomUInt32();
}

double rand_norm()
{
    std::uniform_real_distribution<double> urd;
    return urd(engine);
}

double rand_chance()
{
    std::uniform_real_distribution<double> urd(0.0, 100.0);
    return urd(engine);
}

U32 urandweighted(size_t count, double const* chances)
{
    std::discrete_distribution<U32> dd(chances, chances + count);
    return dd(engine);
}

RandomEngine& RandomEngine::Instance()
{
    return engine;
}
