#pragma once

#include <Debugging/Errors.h>
#include <vector>

class ChatHandler
{
public:
    explicit ChatHandler() {}
    virtual ~ChatHandler() { }

    // function with different implementation for chat/console
    virtual char const* GetAvalonString(U32 entry) const { return ""; };
    virtual void SendSysMessage(std::string_view str, bool escapeCharacters = false) { };

    template<typename... Args>
    void PSendSysMessage(char const* fmt, Args&&... args)
    {
        SendSysMessage(Avalon::StringFormat(fmt, std::forward<Args>(args)...).c_str());
    }

    template<typename... Args>
    void PSendSysMessage(U32 entry, Args&&... args)
    {
        SendSysMessage(PGetParseString(entry, std::forward<Args>(args)...).c_str());
    }

    virtual bool ParseCommands(std::string_view text) { return true;};
    // function with different implementation for chat/console
    virtual std::string GetNameLink() const { return ""; }
    virtual bool needReportToTarget() const { return false;};
    virtual int GetSessionDbLocaleIndex() const { return 0; };
};

class CliHandler : public ChatHandler
{
public:
    using Print = void(void*, std::string_view);
    explicit CliHandler(void* callbackArg, Print* zprint) : m_callbackArg(callbackArg), m_print(zprint) { }

    // overwrite functions
    char const* GetAvalonString(U32 entry) const override { return ""; };
    void SendSysMessage(std::string_view, bool escapeCharacters) override {};
    bool ParseCommands(std::string_view str) override { return true; };
    std::string GetNameLink() const override { return ""; };
    bool needReportToTarget() const override { return false; };
    int GetSessionDbLocaleIndex() const override { return 0;};
    bool HasSentErrorMessage() { return false; };

private:
    void* m_callbackArg;
    Print* m_print;
};
