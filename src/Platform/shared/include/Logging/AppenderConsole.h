#pragma once

#include "Appender.h"

// EnumUtils: DESCRIBE THIS
enum ColorTypes
{
    BLACK,
    RED,
    GREEN,
    BROWN,
    BLUE,
    MAGENTA,
    CYAN,
    GREY,
    YELLOW,
    LRED,
    LGREEN,
    LBLUE,
    LMAGENTA,
    LCYAN,
    WHITE,
    NUM_COLOR_TYPES // SKIP
};

class AppenderConsole : public Appender
{
public:
    static constexpr AppenderType type = APPENDER_CONSOLE;

    AppenderConsole(U8 _id, std::string const& name, LogLevel level, AppenderFlags flags, std::vector<std::string_view> const& args);
    void InitColors(std::string const& name, std::string_view init_str);
    AppenderType getType() const override { return type; }

private:
    void SetColor(bool stdout_stream, ColorTypes color);
    void ResetColor(bool stdout_stream);
    void _write(LogMessage const* message) override;
    bool _colored;
    ColorTypes _colors[NUM_ENABLED_LOG_LEVELS];
};
