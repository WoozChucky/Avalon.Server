#include <Common/Utilities/StringFormat.h>

String Avalon::TrimRightInPlace(String& str)
{
    int pos = int(str.size()) - 1;

    while (pos >= 0 && std::isspace(str[pos]))
    {
        --pos;
    }

    str.resize(static_cast<std::basic_string<char, std::char_traits<char>, std::allocator<char>>::size_type>(pos) + 1);

    return str;
}

String Avalon::AddSuffixIfNotExists(String str, const char suffix) {
    if (str.empty() || (str.at(str.length() - 1) != suffix))
        str.push_back(suffix);

    return str;
}
