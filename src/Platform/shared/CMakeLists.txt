set(This Shared)

# Vendor includes
include_directories(${CMAKE_SOURCE_DIR}/vendor/boringssl/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/boost_asio/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/fmt/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/SFMT)
include_directories(${CMAKE_SOURCE_DIR}/vendor/utfcpp/source)
include_directories(include)

set(BOOST_ROOT ${CMAKE_SOURCE_DIR}/vendor/boost)

find_package(Boost REQUIRED)

if(Boost_FOUND)
    include_directories(${Boost_INCLUDE_DIRS})
endif()

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "src/**/*.h" "include/*.h" "include/**/*.h")

if (WIN32)
#    file(GLOB_RECURSE SOURCES "src/*.cpp" "src/**/*.h" "src/*.h" "windows/src/*.cpp" "windows/src/*.h")
    add_definitions(-DAV_PLATFORM=AV_WIN)
elseif(UNIX AND NOT APPLE)
#    file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "linux/src/*.cpp" "linux/src/*.h")
    add_definitions(-DAV_PLATFORM=AV_UNIX)
elseif(APPLE)
#    file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "mac/src/*.cpp" "mac/src/*.h")
    add_definitions(-DAV_PLATFORM=AV_UNIX)
else()
    message(FATAL_ERROR "Unsupported platform")
endif()

add_library(${This} STATIC ${SOURCES})

target_link_libraries(${This} PRIVATE Boost::boost ssl crypto)

# Add an installation rule to copy the shared library to the output directory
install(TARGETS ${This}
        RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)
