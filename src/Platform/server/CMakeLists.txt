set(This Server)
project(${This})

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "include/*.h")

include_directories(${CMAKE_SOURCE_DIR}/platform_layer/include)

add_executable(${This} ${SOURCES})

add_dependencies(${This} PlatformLayer)

# Link the platform and game layers to the main executable
target_link_libraries(${This} PRIVATE PlatformLayer glm glfw glad stb_image stb_truetype gl2d raudio imgui profilerLib glui)
