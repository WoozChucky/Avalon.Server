set(This PlatformWin32)

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h")

include_directories(${CMAKE_SOURCE_DIR}/platform_layer/include)
include_directories(${CMAKE_SOURCE_DIR}/game_layer/include)

add_library(${This} STATIC ${SOURCES})

# Add an installation rule to copy the shared library to the output directory
install(TARGETS ${This}
    RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
    LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
    ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)

# Link with DirectX libraries
target_link_libraries(${This} PRIVATE -static d3d12 dxgi)

# Copy the PlatformLayer.dll to the output directory during build
add_custom_command(TARGET ${This} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
    $<TARGET_FILE:${This}>
    ${CMAKE_CURRENT_BINARY_DIR}/Game
)
