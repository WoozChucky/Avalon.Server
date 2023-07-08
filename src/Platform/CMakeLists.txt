cmake_minimum_required(VERSION 3.25)

project(Game)

set(CMAKE_CXX_STANDARD 23)

# Add subdirectories for each of the layers
add_subdirectory(platform_layer)
add_subdirectory(game_layer)

include_directories(${CMAKE_SOURCE_DIR}/platform_layer/include)

add_executable(Game main.cpp platform_layer/include/PlatformWindow.h)

add_dependencies(Game PlatformLayer GameLayer)

# Link the platform and game layers to the main executable
target_link_libraries(Game PRIVATE PlatformLayer)
