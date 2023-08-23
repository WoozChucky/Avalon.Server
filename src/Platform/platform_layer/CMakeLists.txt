set(This PlatformLayer)

# Add include directories
include_directories(${CMAKE_SOURCE_DIR}/platform_layer/include)
include_directories(${CMAKE_SOURCE_DIR}/game_layer/include)

# Vendor includes
include_directories(${CMAKE_SOURCE_DIR}/vendor/glfw-3.3.8/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/glad/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/gl2d/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/glm)
include_directories(${CMAKE_SOURCE_DIR}/vendor/stb_image/include)
include_directories(${CMAKE_SOURCE_DIR}/vendor/stb_truetype/include)

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "include/*.h")

if (WIN32)
    file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "windows/src/*.cpp" "windows/src/*.h")
elseif(UNIX AND NOT APPLE)
    file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "linux/src/*.cpp" "linux/src/*.h")
elseif(APPLE)
    file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "mac/src/*.cpp" "mac/src/*.h")
else()
    message(FATAL_ERROR "Unsupported platform")
endif()

add_library(${This} STATIC ${SOURCES})



# Add an installation rule to copy the shared library to the output directory
install(TARGETS ${This}
        RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)
