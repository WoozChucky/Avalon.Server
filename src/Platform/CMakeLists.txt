cmake_minimum_required(VERSION 3.25)

project(Avalon)

set(CMAKE_CXX_STANDARD 17)

set(GLFW_BUILD_DOCS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(GLFW_INSTALL OFF CACHE BOOL "" FORCE)

set(CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake;${CMAKE_MODULE_PATH}")

include(cmake/genrev.cmake)

set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

add_subdirectory(vendor/glfw-3.3.8)			#window oppener
add_subdirectory(vendor/glad)				#opengl loader
add_subdirectory(vendor/stb_image)			#loading immaged
add_subdirectory(vendor/stb_truetype)		#loading ttf files
add_subdirectory(vendor/raudio)				#audio
add_subdirectory(vendor/glm)				#math
add_subdirectory(vendor/imgui-docking)		#ui
#----------V-----------------------V------------#my libraries
add_subdirectory(vendor/profilerLib)		#profiling, just a simpe library for measuring elapsed time
add_subdirectory(vendor/gl2d)				#2D rendering library
add_subdirectory(vendor/glui)				#ui library, usefull for making game menus
add_subdirectory(vendor/boost)
add_subdirectory(vendor/boost_asio)
add_subdirectory(vendor/fmt)
add_subdirectory(vendor/SFMT)
add_subdirectory(vendor/utfcpp)

# Add subdirectories for each of the layers
add_subdirectory(platform_layer)
add_subdirectory(game_layer)

include_directories(${CMAKE_SOURCE_DIR}/vendor/boost_asio/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/fmt/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/SFMT)
include_directories(${CMAKE_SOURCE_DIR}/vendor/utfcpp/source)

set(BOOST_ROOT ${CMAKE_SOURCE_DIR}/vendor/boost)

find_package(Boost REQUIRED)

if(Boost_FOUND)
    include_directories(${Boost_INCLUDE_DIRS})
endif()

find_package(MariaDBClient REQUIRED)

if(MariaDBClient_FOUND)
    include_directories(${MariaDBClient_INCLUDE_DIRS})
endif()

if (WIN32)
    add_definitions(-DAV_PLATFORM_WIN)
    set(OPENSSL_INCLUDE_DIR "C:/Program Files/OpenSSL-Win64/include")
    include_directories(${OPENSSL_INCLUDE_DIR})
    set(SSL_LIBRARIES "C:/Program Files/OpenSSL-Win64/lib/libssl.lib")
    set(CRYPTO_LIBRARIES "C:/Program Files/OpenSSL-Win64/lib/libcrypto.lib")
elseif(UNIX AND NOT APPLE)
    add_definitions(-DAV_PLATFORM_UNIX)
elseif(APPLE)
    add_definitions(-DAV_PLATFORM_UNIX)
    set(OPENSSL_INCLUDE_DIR "/opt/homebrew/Cellar/openssl@3/3.1.2/include")
    include_directories(${OPENSSL_INCLUDE_DIR})
    set(SSL_LIBRARIES "/opt/homebrew/Cellar/openssl@3/3.1.2/lib/libssl.a")
    set(CRYPTO_LIBRARIES "/opt/homebrew/Cellar/openssl@3/3.1.2/lib/libcrypto.a")
else()
    message(FATAL_ERROR "Unsupported platform")
endif()

add_definitions(-D_BUILD_DIRECTIVE="${CMAKE_BUILD_TYPE}")

add_subdirectory(common)
add_subdirectory(database)
add_subdirectory(client)
add_subdirectory(server)
add_subdirectory(genrev)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR})
