#pragma once

#include <Common/Types.h>
#include <string>

#define CONFIG_PROCESSOR_AFFINITY "UseProcessors"
#define CONFIG_HIGH_PRIORITY "ProcessPriority"

void SetProcessPriority(std::string const& logChannel, U32 affinity, bool highPriority);

