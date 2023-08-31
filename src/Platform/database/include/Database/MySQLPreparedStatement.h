#pragma once

#include "DatabaseEnvFwd.h"
#include <Common/Types.h>
#include "MySQLWorkaround.h"
#include <string>
#include <vector>

class MySQLConnection;
class PreparedStatementBase;

//- Class of which the instances are unique per MySQLConnection
//- access to these class objects is only done when a prepared statement task
//- is executed.
class MySQLPreparedStatement
{
friend class MySQLConnection;
friend class PreparedStatementBase;

public:
    MySQLPreparedStatement(MySQLStmt* stmt, std::string_view queryString);
    ~MySQLPreparedStatement();

    void BindParameters(PreparedStatementBase* stmt);

    U32 GetParameterCount() const { return m_paramCount; }

protected:
    void SetParameter(const U8 index, bool value);
    void SetParameter(const U8 index, std::nullptr_t /*value*/);
    void SetParameter(const U8 index, std::string const& value);
    void SetParameter(const U8 index, std::vector<U8> const& value);

    template<typename T>
    void SetParameter(const U8 index, T value);

    MySQLStmt* GetSTMT() { return m_Mstmt; }
    MySQLBind* GetBind() { return m_bind; }
    PreparedStatementBase* m_stmt;
    void ClearParameters();
    void AssertValidIndex(const U8 index);
    std::string getQueryString() const;

private:
    MySQLStmt* m_Mstmt;
    U32 m_paramCount;
    std::vector<bool> m_paramsSet;
    MySQLBind* m_bind;
    std::string m_queryString{};

    MySQLPreparedStatement(MySQLPreparedStatement const& right) = delete;
    MySQLPreparedStatement& operator=(MySQLPreparedStatement const& right) = delete;
};
