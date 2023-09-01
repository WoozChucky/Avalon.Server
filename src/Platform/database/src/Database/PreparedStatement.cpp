#include <Database/PreparedStatement.h>

#include <Common/Debugging/Errors.h>
#include <Common/Logging/Log.h>
#include <Database/MySQLConnection.h>
#include <Database/MySQLPreparedStatement.h>
#include <Database/MySQLWorkaround.h>
#include <Database/QueryResult.h>

PreparedStatementBase::PreparedStatementBase(U32 index, U8 capacity) :
    m_index(index),
    statement_data(capacity) { }

PreparedStatementBase::~PreparedStatementBase() { }

//- Bind to buffer
template<typename T>
Avalon::Types::is_non_string_view_v<T> PreparedStatementBase::SetValidData(const U8 index, T const& value)
{
    ASSERT(index < statement_data.size());
    statement_data[index].data.emplace<T>(value);
}

// Non template functions
void PreparedStatementBase::SetValidData(const U8 index)
{
    ASSERT(index < statement_data.size());
    statement_data[index].data.emplace<std::nullptr_t>(nullptr);
}

void PreparedStatementBase::SetValidData(const U8 index, std::string_view value)
{
    ASSERT(index < statement_data.size());
    statement_data[index].data.emplace<std::string>(value);
}

template void PreparedStatementBase::SetValidData(const U8 index, U8 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, S8 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, U16 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, S16 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, U32 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, S32 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, U64 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, S64 const& value);
template void PreparedStatementBase::SetValidData(const U8 index, bool const& value);
template void PreparedStatementBase::SetValidData(const U8 index, float const& value);
template void PreparedStatementBase::SetValidData(const U8 index, std::string const& value);
template void PreparedStatementBase::SetValidData(const U8 index, std::vector<U8> const& value);

//- Execution
PreparedStatementTask::PreparedStatementTask(PreparedStatementBase* stmt, bool async) :
    m_stmt(stmt),
    m_result(nullptr)
{
    m_has_result = async; // If it's async, then there's a result

    if (async)
        m_result = new PreparedQueryResultPromise();
}

PreparedStatementTask::~PreparedStatementTask()
{
    delete m_stmt;

    if (m_has_result && m_result)
        delete m_result;
}

bool PreparedStatementTask::Execute()
{
    if (m_has_result)
    {
        PreparedResultSet* result = m_conn->Query(m_stmt);
        if (!result || !result->GetRowCount())
        {
            delete result;
            m_result->set_value(PreparedQueryResult(nullptr));
            return false;
        }

        m_result->set_value(PreparedQueryResult(result));
        return true;
    }

    return m_conn->Execute(m_stmt);
}

template<typename T>
std::string PreparedStatementData::ToString(T value)
{
    return Avalon::StringFormatFmt("{}", value);
}

template<>
std::string PreparedStatementData::ToString(std::vector<U8> /*value*/)
{
    return "BINARY";
}

template std::string PreparedStatementData::ToString(U8);
template std::string PreparedStatementData::ToString(U16);
template std::string PreparedStatementData::ToString(U32);
template std::string PreparedStatementData::ToString(U64);
template std::string PreparedStatementData::ToString(S8);
template std::string PreparedStatementData::ToString(S16);
template std::string PreparedStatementData::ToString(S32);
template std::string PreparedStatementData::ToString(S64);
template std::string PreparedStatementData::ToString(std::string);
template std::string PreparedStatementData::ToString(float);
template std::string PreparedStatementData::ToString(double);
template std::string PreparedStatementData::ToString(bool);

std::string PreparedStatementData::ToString(std::nullptr_t /*value*/)
{
    return "NULL";
}
