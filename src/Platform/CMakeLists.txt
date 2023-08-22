cmake_minimum_required(VERSION 3.25)

project(Game)

set(CMAKE_CXX_STANDARD 23)

add_compile_options(-mfma)

set(GLFW_BUILD_DOCS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(GLFW_INSTALL OFF CACHE BOOL "" FORCE)

add_subdirectory(vendor/glfw-3.3.2)			#window oppener
add_subdirectory(vendor/glad)				#opengl loader
add_subdirectory(vendor/stb_image)			#loading immaged
add_subdirectory(vendor/stb_truetype)		#loading ttf files
#add_subdirectory(vendor/enet-1.3.17)		#networking
add_subdirectory(vendor/raudio)				#audio
add_subdirectory(vendor/glm)				#math
add_subdirectory(vendor/imgui-docking)		#ui
#----------V-----------------------V------------#my libraries
add_subdirectory(vendor/safeSave)			#saving data, designed for gamedev. It also makes backups so the player won't loose saves.
add_subdirectory(vendor/profilerLib)		#profiling, just a simpe library for measuring elapsed time
add_subdirectory(vendor/gl2d)				#2D rendering library
add_subdirectory(vendor/glui)				#ui library, usefull for making game menus

# Add subdirectories for each of the layers
add_subdirectory(platform_layer)
add_subdirectory(game_layer)

include_directories(${CMAKE_SOURCE_DIR}/platform_layer/include)

add_executable(Game main.cpp platform_layer/include/PlatformWindow.h)

add_dependencies(Game PlatformLayer GameLayer)

# Link the platform and game layers to the main executable
target_link_libraries(Game PRIVATE PlatformLayer glm glfw glad stb_image stb_truetype gl2d raudio imgui safeSave profilerLib glui)
