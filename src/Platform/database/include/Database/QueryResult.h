#pragma once

#include "DatabaseEnvFwd.h"
#include <Common/Types.h>
#include "Field.h"
#include <tuple>
#include <vector>

template<typename T>
struct ResultIterator
{
    using iterator_category = std::forward_iterator_tag;
    using difference_type   = std::ptrdiff_t;
    using value_type        = T;
    using pointer           = T*;
    using reference         = T&;

    explicit ResultIterator(pointer ptr) : _ptr(ptr) { }

    reference operator*() const { return *_ptr; }
    pointer operator->() { return _ptr; }
    ResultIterator& operator++() { if (!_ptr->NextRow()) _ptr = nullptr; return *this; }

    bool operator!=(const ResultIterator& right) { return _ptr != right._ptr; }

private:
    pointer _ptr;
};

class ResultSet
{
public:
    ResultSet(MySQLResult* result, MySQLField* fields, U64 rowCount, U32 fieldCount);
    ~ResultSet();

    bool NextRow();
    [[nodiscard]] U64 GetRowCount() const { return _rowCount; }
    [[nodiscard]] U32 GetFieldCount() const { return _fieldCount; }
    [[nodiscard]] std::string GetFieldName(U32 index) const;

    [[nodiscard]] Field* Fetch() const { return _currentRow; }
    Field const& operator[](std::size_t index) const;

    template<typename... Ts>
    inline std::tuple<Ts...> FetchTuple()
    {
        AssertRows(sizeof...(Ts));

        std::tuple<Ts...> theTuple = {};

        std::apply([this](Ts&... args)
        {
            U8 index{ 0 };
            ((args = _currentRow[index].Get<Ts>(), index++), ...);
        }, theTuple);

        return theTuple;
    }

    auto begin()      { return ResultIterator<ResultSet>(this); }
    static auto end() { return ResultIterator<ResultSet>(nullptr); }

protected:
    std::vector<QueryResultFieldMetadata> _fieldMetadata;
    U64 _rowCount;
    Field* _currentRow;
    U32 _fieldCount;

private:
    void CleanUp();
    void AssertRows(std::size_t sizeRows);

    MySQLResult* _result;
    MySQLField* _fields;

    ResultSet(ResultSet const& right) = delete;
    ResultSet& operator=(ResultSet const& right) = delete;
};

class PreparedResultSet
{
public:
    PreparedResultSet(MySQLStmt* stmt, MySQLResult* result, U64 rowCount, U32 fieldCount);
    ~PreparedResultSet();

    bool NextRow();
    [[nodiscard]] U64 GetRowCount() const { return m_rowCount; }
    [[nodiscard]] U32 GetFieldCount() const { return m_fieldCount; }

    [[nodiscard]] Field* Fetch() const;
    Field const& operator[](std::size_t index) const;

    template<typename... Ts>
    inline std::tuple<Ts...> FetchTuple()
    {
        AssertRows(sizeof...(Ts));

        std::tuple<Ts...> theTuple = {};

        std::apply([this](Ts&... args)
        {
            U8 index{ 0 };
            ((args = m_rows[U32(m_rowPosition) * m_fieldCount + index].Get<Ts>(), index++), ...);
        }, theTuple);

        return theTuple;
    }

    auto begin()        { return ResultIterator<PreparedResultSet>(this); }
    static auto end()   { return ResultIterator<PreparedResultSet>(nullptr); }

protected:
    std::vector<QueryResultFieldMetadata> m_fieldMetadata;
    std::vector<Field> m_rows;
    U64 m_rowCount;
    U64 m_rowPosition;
    U32 m_fieldCount;

private:
    MySQLBind* m_rBind;
    MySQLStmt* m_stmt;
    MySQLResult* m_metadataResult;    ///< Field metadata, returned by mysql_stmt_result_metadata

    void CleanUp();
    bool _NextRow();

    void AssertRows(std::size_t sizeRows);

    PreparedResultSet(PreparedResultSet const& right) = delete;
    PreparedResultSet& operator=(PreparedResultSet const& right) = delete;
};
