cmake_minimum_required(VERSION 3.25)

project(Avalon)

set(CMAKE_CXX_STANDARD 17)

set(GLFW_BUILD_DOCS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(GLFW_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(GLFW_INSTALL OFF CACHE BOOL "" FORCE)

set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

add_subdirectory(vendor/glfw-3.3.8)			#window oppener
add_subdirectory(vendor/glad)				#opengl loader
add_subdirectory(vendor/stb_image)			#loading immaged
add_subdirectory(vendor/stb_truetype)		#loading ttf files
#add_subdirectory(vendor/enet-1.3.17)		#networking
add_subdirectory(vendor/raudio)				#audio
add_subdirectory(vendor/glm)				#math
add_subdirectory(vendor/imgui-docking)		#ui
#----------V-----------------------V------------#my libraries
add_subdirectory(vendor/profilerLib)		#profiling, just a simpe library for measuring elapsed time
add_subdirectory(vendor/gl2d)				#2D rendering library
add_subdirectory(vendor/glui)				#ui library, usefull for making game menus
add_subdirectory(vendor/boringssl)
add_subdirectory(vendor/boost)
add_subdirectory(vendor/boost_asio)
add_subdirectory(vendor/fmt)
add_subdirectory(vendor/SFMT)
add_subdirectory(vendor/utfcpp)

# Add subdirectories for each of the layers
add_subdirectory(platform_layer)
add_subdirectory(game_layer)
add_subdirectory(shared)
add_subdirectory(client)
add_subdirectory(server)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR})
