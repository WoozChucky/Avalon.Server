#pragma once

#include <Common/Types.h>
#include <string>

enum AccountOpResult
{
    AOR_OK,
    AOR_NAME_TOO_LONG,
    AOR_PASS_TOO_LONG,
    AOR_NAME_ALREADY_EXIST,
    AOR_NAME_NOT_EXIST,
    AOR_DB_INTERNAL_ERROR
};

#define MAX_ACCOUNT_STR 20
#define MAX_PASS_STR 16

namespace AccountMgr
{
    AccountOpResult CreateAccount(std::string username, std::string password);
    AccountOpResult DeleteAccount(U32 accountId);
    AccountOpResult ChangeUsername(U32 accountId, std::string newUsername, std::string newPassword);
    AccountOpResult ChangePassword(U32 accountId, std::string newPassword);
    bool CheckPassword(U32 accountId, std::string password);

    U32 GetId(std::string const& username);
    U32 GetSecurity(U32 accountId);
    U32 GetSecurity(U32 accountId, S32 realmId);
    bool GetName(U32 accountId, std::string& name);
    U32 GetCharactersCount(U32 accountId);

    bool IsPlayerAccount(U32 gmlevel);
    bool IsGMAccount(U32 gmlevel);
    bool IsAdminAccount(U32 gmlevel);
    bool IsConsoleAccount(U32 gmlevel);
};
