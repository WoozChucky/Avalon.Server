#include <Database/AdhocStatement.h>

#include <Debugging/Errors.h>
#include <Database/MySQLConnection.h>
#include <Database/QueryResult.h>

/*! Basic, ad-hoc queries. */
BasicStatementTask::BasicStatementTask(std::string_view sql, bool async) : m_result(nullptr)
{
    m_sql = std::string(sql);
    m_has_result = async; // If the operation is async, then there's a result

    if (async)
        m_result = new QueryResultPromise();
}

BasicStatementTask::~BasicStatementTask()
{
    m_sql.clear();
    if (m_has_result && m_result)
        delete m_result;
}

bool BasicStatementTask::Execute()
{
    if (m_has_result)
    {
        ResultSet* result = m_conn->Query(m_sql);
        if (!result || !result->GetRowCount() || !result->NextRow())
        {
            delete result;
            m_result->set_value(QueryResult(nullptr));
            return false;
        }

        m_result->set_value(QueryResult(result));
        return true;
    }

    return m_conn->Execute(m_sql);
}
