#pragma once

#include <Database/DatabaseEnvFwd.h>
#include <Common/Types.h>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

template <typename T>
class ProducerConsumerQueue;

class DatabaseWorker;
class MySQLPreparedStatement;
class SQLOperation;

enum ConnectionFlags
{
    CONNECTION_ASYNC = 0x1,
    CONNECTION_SYNCH = 0x2,
    CONNECTION_BOTH = CONNECTION_ASYNC | CONNECTION_SYNCH
};

struct MySQLConnectionInfo
{
    explicit MySQLConnectionInfo(std::string_view infoString);

    std::string user;
    std::string password;
    std::string database;
    std::string host;
    std::string port_or_socket;
    std::string ssl;
};

class MySQLConnection
{
template <class T>
friend class DatabaseWorkerPool;

friend class PingOperation;

public:
    MySQLConnection(MySQLConnectionInfo& connInfo);                               //! Constructor for synchronous connections.
    MySQLConnection(ProducerConsumerQueue<SQLOperation*>* queue, MySQLConnectionInfo& connInfo);  //! Constructor for asynchronous connections.
    virtual ~MySQLConnection();

    virtual U32 Open();
    void Close();

    bool PrepareStatements();

    bool Execute(std::string_view sql);
    bool Execute(PreparedStatementBase* stmt);
    ResultSet* Query(std::string_view sql);
    PreparedResultSet* Query(PreparedStatementBase* stmt);
    bool _Query(std::string_view sql, MySQLResult** pResult, MySQLField** pFields, U64* pRowCount, U32* pFieldCount);
    bool _Query(PreparedStatementBase* stmt, MySQLPreparedStatement** mysqlStmt, MySQLResult** pResult, U64* pRowCount, U32* pFieldCount);

    void BeginTransaction();
    void RollbackTransaction();
    void CommitTransaction();
    int ExecuteTransaction(std::shared_ptr<TransactionBase> transaction);
    size_t EscapeString(char* to, const char* from, size_t length);
    void Ping();

    U32 GetLastError();

protected:
    /// Tries to acquire lock. If lock is acquired by another thread
    /// the calling parent will just try another connection
    bool LockIfReady();

    /// Called by parent databasepool. Will let other threads access this connection
    void Unlock();

    [[nodiscard]] U32 GetServerVersion() const;
    MySQLPreparedStatement* GetPreparedStatement(U32 index);
    void PrepareStatement(U32 index, std::string_view sql, ConnectionFlags flags);

    virtual void DoPrepareStatements() = 0;
    virtual bool _HandleMySQLErrno(U32 errNo, U8 attempts = 5);

    typedef std::vector<std::unique_ptr<MySQLPreparedStatement>> PreparedStatementContainer;

    PreparedStatementContainer m_stmts; //! PreparedStatements storage
    bool m_reconnecting;  //! Are we reconnecting?
    bool m_prepareError;  //! Was there any error while preparing statements?
    MySQLHandle* m_Mysql; //! MySQL Handle.

private:
    ProducerConsumerQueue<SQLOperation*>* m_queue;      //! Queue shared with other asynchronous connections.
    std::unique_ptr<DatabaseWorker> m_worker;           //! Core worker task.
    MySQLConnectionInfo& m_connectionInfo;              //! Connection info (used for logging)
    ConnectionFlags m_connectionFlags;                  //! Connection flags (for preparing relevant statements)
    std::mutex m_Mutex;

    MySQLConnection(MySQLConnection const& right) = delete;
    MySQLConnection& operator=(MySQLConnection const& right) = delete;
};
