set(This worldserver)
project(${This})

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "include/*.h")

include_directories(${CMAKE_SOURCE_DIR}/common/include)
include_directories(${CMAKE_SOURCE_DIR}/database/include)
include_directories(${CMAKE_SOURCE_DIR}/server/shared/include)
include_directories("/opt/homebrew/Cellar/openssl@3/3.1.2/include")

set(BOOST_ROOT ${CMAKE_SOURCE_DIR}/vendor/boost)

find_package(Boost REQUIRED)

if(Boost_FOUND)
    include_directories(${Boost_INCLUDE_DIRS})
endif()

find_package(MariaDBClient REQUIRED)

if(MariaDBClient_FOUND)
    include_directories(${MariaDBClient_INCLUDE_DIRS})
endif()

include_directories(${CMAKE_SOURCE_DIR}/vendor/boost_asio/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/fmt/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/SFMT)
include_directories(${CMAKE_SOURCE_DIR}/vendor/utfcpp/source)

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

add_executable(${This} ${SOURCES})

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

    # Link the platform and game layers to the main executable
    target_link_libraries(${This} PRIVATE Common Database fmt Boost::boost ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES} libmariadb)
endif ()

if(UNIX)
    # Link the platform and game layers to the main executable
    target_link_libraries(${This} PRIVATE Common Database fmt Boost::boost ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES} ${MariaDBClient_LIBRARY})
endif ()


