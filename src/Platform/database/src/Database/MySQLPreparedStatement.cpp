#include <Database/MySQLPreparedStatement.h>

#include <Common/Debugging/Errors.h>
#include <Common/Logging/Log.h>
#include <Database/MySQLHacks.h>
#include <Database/PreparedStatement.h>

template<typename T>
struct MySQLType { };

template<> struct MySQLType<U8> : std::integral_constant<enum_field_types, MYSQL_TYPE_TINY> { };
template<> struct MySQLType<U16> : std::integral_constant<enum_field_types, MYSQL_TYPE_SHORT> { };
template<> struct MySQLType<U32> : std::integral_constant<enum_field_types, MYSQL_TYPE_LONG> { };
template<> struct MySQLType<U64> : std::integral_constant<enum_field_types, MYSQL_TYPE_LONGLONG> { };
template<> struct MySQLType<S8> : std::integral_constant<enum_field_types, MYSQL_TYPE_TINY> { };
template<> struct MySQLType<S16> : std::integral_constant<enum_field_types, MYSQL_TYPE_SHORT> { };
template<> struct MySQLType<S32> : std::integral_constant<enum_field_types, MYSQL_TYPE_LONG> { };
template<> struct MySQLType<S64> : std::integral_constant<enum_field_types, MYSQL_TYPE_LONGLONG> { };
template<> struct MySQLType<float> : std::integral_constant<enum_field_types, MYSQL_TYPE_FLOAT> { };
template<> struct MySQLType<double> : std::integral_constant<enum_field_types, MYSQL_TYPE_DOUBLE> { };

MySQLPreparedStatement::MySQLPreparedStatement(MySQLStmt* stmt, std::string_view queryString) :
    m_stmt(nullptr),
    m_Mstmt(stmt),
    m_bind(nullptr),
    m_queryString(std::string(queryString))
{
    /// Initialize variable parameters
    m_paramCount = mysql_stmt_param_count(stmt);
    m_paramsSet.assign(m_paramCount, false);
    m_bind = new MySQLBind[m_paramCount];
    memset(m_bind, 0, sizeof(MySQLBind) * m_paramCount);

    /// "If set to 1, causes mysql_stmt_store_result() to update the metadata MYSQL_FIELD->max_length value."
    MySQLBool bool_tmp = MySQLBool(1);
    mysql_stmt_attr_set(stmt, STMT_ATTR_UPDATE_MAX_LENGTH, &bool_tmp);
}

MySQLPreparedStatement::~MySQLPreparedStatement()
{
    ClearParameters();
    if (m_Mstmt->bind_result_done)
    {
        delete[] m_Mstmt->bind->length;
        delete[] m_Mstmt->bind->is_null;
    }

    mysql_stmt_close(m_Mstmt);
    delete[] m_bind;
}

void MySQLPreparedStatement::BindParameters(PreparedStatementBase* stmt)
{
    m_stmt = stmt;     // Cross reference them for debug output

    U8 pos = 0;
    for (PreparedStatementData const& data : stmt->GetParameters())
    {
        std::visit([&](auto&& param)
        {
            SetParameter(pos, param);
        }, data.data);

        ++pos;
    }

#ifdef _DEBUG
    if (pos < m_paramCount)
        LOG_WARN("sql.sql", "[WARNING]: BindParameters() for statement {} did not bind all allocated parameters", stmt->GetIndex());
#endif
}

void MySQLPreparedStatement::ClearParameters()
{
    for (U32 i=0; i < m_paramCount; ++i)
    {
        delete m_bind[i].length;
        m_bind[i].length = nullptr;
        delete[] (char*)m_bind[i].buffer;
        m_bind[i].buffer = nullptr;
        m_paramsSet[i] = false;
    }
}

static bool ParamenterIndexAssertFail(U32 stmtIndex, U8 index, U32 paramCount)
{
    LOG_ERROR("sql.driver", "Attempted to bind parameter {}{} on a PreparedStatement {} (statement has only {} parameters)",
        U32(index) + 1, (index == 1 ? "st" : (index == 2 ? "nd" : (index == 3 ? "rd" : "nd"))), stmtIndex, paramCount);

    return false;
}

//- Bind on mysql level
void MySQLPreparedStatement::AssertValidIndex(U8 index)
{
    ASSERT(index < m_paramCount || ParamenterIndexAssertFail(m_stmt->GetIndex(), index, m_paramCount));

    if (m_paramsSet[index])
        LOG_ERROR("sql.sql", "[ERROR] Prepared Statement (id: {}) trying to bind value on already bound index ({}).", m_stmt->GetIndex(), index);
}

template<typename T>
void MySQLPreparedStatement::SetParameter(const U8 index, T value)
{
    AssertValidIndex(index);
    m_paramsSet[index] = true;
    MYSQL_BIND* param = &m_bind[index];
    U32 len = U32(sizeof(T));
    param->buffer_type = MySQLType<T>::value;
    delete[] static_cast<char*>(param->buffer);
    param->buffer = new char[len];
    param->buffer_length = 0;
    param->is_null_value = 0;
    param->length = nullptr; // Only != NULL for strings
    param->is_unsigned = std::is_unsigned_v<T>;

    memcpy(param->buffer, &value, len);
}

void MySQLPreparedStatement::SetParameter(const U8 index, bool value)
{
    SetParameter(index, U8(value ? 1 : 0));
}

void MySQLPreparedStatement::SetParameter(const U8 index, std::nullptr_t /*value*/)
{
    AssertValidIndex(index);
    m_paramsSet[index] = true;
    MYSQL_BIND* param = &m_bind[index];
    param->buffer_type = MYSQL_TYPE_NULL;
    delete[] static_cast<char*>(param->buffer);
    param->buffer = nullptr;
    param->buffer_length = 0;
    param->is_null_value = 1;
    delete param->length;
    param->length = nullptr;
}

void MySQLPreparedStatement::SetParameter(U8 index, std::string const& value)
{
    AssertValidIndex(index);
    m_paramsSet[index] = true;
    MYSQL_BIND* param = &m_bind[index];
    U32 len = U32(value.size());
    param->buffer_type = MYSQL_TYPE_VAR_STRING;
    delete[] static_cast<char*>(param->buffer);
    param->buffer = new char[len];
    param->buffer_length = len;
    param->is_null_value = 0;
    delete param->length;
    param->length = new unsigned long(len);

    memcpy(param->buffer, value.c_str(), len);
}

void MySQLPreparedStatement::SetParameter(U8 index, std::vector<U8> const& value)
{
    AssertValidIndex(index);
    m_paramsSet[index] = true;
    MYSQL_BIND* param = &m_bind[index];
    U32 len = U32(value.size());
    param->buffer_type = MYSQL_TYPE_BLOB;
    delete[] static_cast<char*>(param->buffer);
    param->buffer = new char[len];
    param->buffer_length = len;
    param->is_null_value = 0;
    delete param->length;
    param->length = new unsigned long(len);

    memcpy(param->buffer, value.data(), len);
}

std::string MySQLPreparedStatement::getQueryString() const
{
    std::string queryString(m_queryString);

    size_t pos = 0;

    for (PreparedStatementData const& data : m_stmt->GetParameters())
    {
        pos = queryString.find('?', pos);

        std::string replaceStr = std::visit([&](auto&& data)
        {
            return PreparedStatementData::ToString(data);
        }, data.data);

        queryString.replace(pos, 1, replaceStr);
        pos += replaceStr.length();
    }

    return queryString;
}
