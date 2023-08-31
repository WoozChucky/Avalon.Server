#pragma once

#include "DatabaseEnvFwd.h"
#include <Common/Types.h>
#include "SQLOperation.h"

/*! Raw, ad-hoc query. */
class BasicStatementTask : public SQLOperation
{
public:
    BasicStatementTask(std::string_view sql, bool async = false);
    ~BasicStatementTask();

    bool Execute() override;
    QueryResultFuture GetFuture() const { return m_result->get_future(); }

private:
    std::string m_sql; //- Raw query to be executed
    bool m_has_result;
    QueryResultPromise* m_result;
};
