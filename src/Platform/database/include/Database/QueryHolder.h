#pragma once

#include "SQLOperation.h"
#include <vector>

class SQLQueryHolderBase
{
friend class SQLQueryHolderTask;

public:
    SQLQueryHolderBase() = default;
    virtual ~SQLQueryHolderBase();
    void SetSize(size_t size);
    PreparedQueryResult GetPreparedResult(size_t index) const;
    void SetPreparedResult(size_t index, PreparedResultSet* result);

protected:
    bool SetPreparedQueryImpl(size_t index, PreparedStatementBase* stmt);

private:
    std::vector<std::pair<PreparedStatementBase*, PreparedQueryResult>> m_queries;
};

template<typename T>
class SQLQueryHolder : public SQLQueryHolderBase
{
public:
    bool SetPreparedQuery(size_t index, PreparedStatement<T>* stmt)
    {
        return SetPreparedQueryImpl(index, stmt);
    }
};

class SQLQueryHolderTask : public SQLOperation
{
public:
    explicit SQLQueryHolderTask(std::shared_ptr<SQLQueryHolderBase> holder)
        : m_holder(std::move(holder)) { }

    ~SQLQueryHolderTask();

    bool Execute() override;
    QueryResultHolderFuture GetFuture() { return m_result.get_future(); }

private:
    std::shared_ptr<SQLQueryHolderBase> m_holder;
    QueryResultHolderPromise m_result;
};

class SQLQueryHolderCallback
{
public:
    SQLQueryHolderCallback(std::shared_ptr<SQLQueryHolderBase>&& holder, QueryResultHolderFuture&& future)
        : m_holder(std::move(holder)), m_future(std::move(future)) { }

    SQLQueryHolderCallback(SQLQueryHolderCallback&&) = default;
    SQLQueryHolderCallback& operator=(SQLQueryHolderCallback&&) = default;

    void AfterComplete(std::function<void(SQLQueryHolderBase const&)> callback) &
    {
        m_callback = std::move(callback);
    }

    bool InvokeIfReady();

    std::shared_ptr<SQLQueryHolderBase> m_holder;
    QueryResultHolderFuture m_future;
    std::function<void(SQLQueryHolderBase const&)> m_callback;
};
