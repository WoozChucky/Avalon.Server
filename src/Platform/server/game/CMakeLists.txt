set(This Game)

find_package(MariaDBClient REQUIRED)

if(MariaDBClient_FOUND)
    include_directories(${MariaDBClient_INCLUDE_DIRS})
endif()

set(BOOST_ROOT ${CMAKE_SOURCE_DIR}/vendor/boost)

find_package(Boost REQUIRED)

if(Boost_FOUND)
    include_directories(${Boost_INCLUDE_DIRS})
endif()

include_directories(${CMAKE_SOURCE_DIR}/vendor/utfcpp/source)
include_directories(${CMAKE_SOURCE_DIR}/vendor/fmt/include)
include_directories("/opt/homebrew/Cellar/openssl@3/3.1.2/include")

include_directories(${CMAKE_SOURCE_DIR}/common/include)
include_directories(${CMAKE_SOURCE_DIR}/database/include)
include_directories(${CMAKE_SOURCE_DIR}/server/shared/include)

include_directories(include)

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "src/**/*.h" "include/*.h" "include/**/*.h")

if (WIN32)
    add_definitions(-DAV_PLATFORM_WIN)
elseif(UNIX AND NOT APPLE)
    add_definitions(-DAV_PLATFORM_UNIX)
elseif(APPLE)
    add_definitions(-DAV_PLATFORM_UNIX)
    set(SSL_LIBRARIES "/opt/homebrew/Cellar/openssl@3/3.1.2/lib/libssl.a")
    set(CRYPTO_LIBRARIES "/opt/homebrew/Cellar/openssl@3/3.1.2/lib/libcrypto.a")
else()
    message(FATAL_ERROR "Unsupported platform")
endif()

add_library(${This} STATIC ${SOURCES})

# Add an installation rule to copy the database library to the output directory
install(TARGETS ${This}
        RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)

if(WIN32)
    add_custom_command(
            TARGET ${This} PRE_LINK
            COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${MariaDBClient_ROOT_DIR}/lib
            ${CMAKE_BINARY_DIR})

    add_custom_command(
            TARGET ${This} POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different
            ${MariaDBClient_LIBRARY}
            ${CMAKE_BINARY_DIR}/bin)

    target_link_libraries(${This} PRIVATE fmt Common Shared Boost::boost libmariadb ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES})
endif ()

if(UNIX)
    target_link_libraries(${This} PRIVATE fmt Common Shared Boost::boost ${MariaDBClient_LIBRARY} ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES})
endif ()
