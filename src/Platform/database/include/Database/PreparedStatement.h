#pragma once

#include <Common/Types.h>
#include <Utilities/Optional.h>
#include <Utilities/Duration.h>
#include "SQLOperation.h"
#include <future>
#include <tuple>
#include <variant>
#include <vector>

namespace Avalon::Types
{
    template <typename T>
    using is_default = std::enable_if_t<std::is_arithmetic_v<T> || std::is_same_v<std::vector<U8>, T>>;

    template <typename T>
    using is_enum_v = std::enable_if_t<std::is_enum_v<T>>;

    template <typename T>
    using is_non_string_view_v = std::enable_if_t<!std::is_base_of_v<std::string_view, T>>;
}

struct PreparedStatementData
{
    std::variant<
        bool,
        U8,
        U16,
        U32,
        U64,
        S8,
        S16,
        S32,
        S64,
        float,
        double,
        std::string,
        std::vector<U8>,
        std::nullptr_t
    > data;

    template<typename T>
    static std::string ToString(T value);

    static std::string ToString(std::nullptr_t /*value*/);
};

//- Upper-level class that is used in code
class PreparedStatementBase
{
friend class PreparedStatementTask;

public:
    explicit PreparedStatementBase(U32 index, U8 capacity);
    virtual ~PreparedStatementBase();

    // Set numerlic and default binary
    template<typename T>
    inline Avalon::Types::is_default<T> SetData(const U8 index, T value)
    {
        SetValidData(index, value);
    }

    // Set enums
    template<typename T>
    inline Avalon::Types::is_enum_v<T> SetData(const U8 index, T value)
    {
        SetValidData(index, std::underlying_type_t<T>(value));
    }

    // Set string_view
    inline void SetData(const U8 index, std::string_view value)
    {
        SetValidData(index, value);
    }

    // Set nullptr
    inline void SetData(const U8 index, std::nullptr_t = nullptr)
    {
        SetValidData(index);
    }

    // Set non default binary
    template<std::size_t Size>
    inline void SetData(const U8 index, std::array<U8, Size> const& value)
    {
        std::vector<U8> vec(value.begin(), value.end());
        SetValidData(index, vec);
    }

    // Set duration
    template<class _Rep, class _Period>
    inline void SetData(const U8 index, std::chrono::duration<_Rep, _Period> const& value, bool convertToUin32 = true)
    {
        SetValidData(index, convertToUin32 ? static_cast<U8>(value.count()) : value.count());
    }

    // Set all
    template <typename... Args>
    inline void SetArguments(Args&&... args)
    {
        SetDataTuple(std::make_tuple(std::forward<Args>(args)...));
    }

    [[nodiscard]] U32 GetIndex() const { return m_index; }
    [[nodiscard]] std::vector<PreparedStatementData> const& GetParameters() const { return statement_data; }

protected:
    template<typename T>
    Avalon::Types::is_non_string_view_v<T> SetValidData(const U8 index, T const& value);

    void SetValidData(const U8 index);
    void SetValidData(const U8 index, std::string_view value);

    template<typename... Ts>
    inline void SetDataTuple(std::tuple<Ts...> const& argsList)
    {
        std::apply
        (
            [this](Ts const&... arguments)
            {
                U8 index{ 0 };
                ((SetData(index, arguments), index++), ...);
            }, argsList
        );
    }

    U32 m_index;

    //- Buffer of parameters, not tied to MySQL in any way yet
    std::vector<PreparedStatementData> statement_data;

    PreparedStatementBase(PreparedStatementBase const& right) = delete;
    PreparedStatementBase& operator=(PreparedStatementBase const& right) = delete;
};

template<typename T>
class PreparedStatement : public PreparedStatementBase
{
public:
    explicit PreparedStatement(U32 index, U8 capacity) : PreparedStatementBase(index, capacity)
    {
    }

private:
    PreparedStatement(PreparedStatement const& right) = delete;
    PreparedStatement& operator=(PreparedStatement const& right) = delete;
};

//- Lower-level class, enqueuable operation
class PreparedStatementTask : public SQLOperation
{
public:
    PreparedStatementTask(PreparedStatementBase* stmt, bool async = false);
    ~PreparedStatementTask() override;

    bool Execute() override;
    PreparedQueryResultFuture GetFuture() { return m_result->get_future(); }

protected:
    PreparedStatementBase* m_stmt;
    bool m_has_result;
    PreparedQueryResultPromise* m_result;
};
