set(This Server)
project(${This})

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "include/*.h")

include_directories(${CMAKE_SOURCE_DIR}/shared/include)

set(BOOST_ROOT ${CMAKE_SOURCE_DIR}/vendor/boost)

find_package(Boost REQUIRED)

if(Boost_FOUND)
    include_directories(${Boost_INCLUDE_DIRS})
endif()

include_directories(${CMAKE_SOURCE_DIR}/vendor/boringssl/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/boost_asio/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/fmt/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/SFMT)
include_directories(${CMAKE_SOURCE_DIR}/vendor/utfcpp/source)

add_executable(${This} ${SOURCES})

# Link the platform and game layers to the main executable
target_link_libraries(${This} PRIVATE Shared fmt Boost::boost ssl crypto)
