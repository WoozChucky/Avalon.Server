file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "src/*.hpp")

include_directories(${CMAKE_SOURCE_DIR}/platform_layer/include)
include_directories(${CMAKE_SOURCE_DIR}/platform_layer/mac/vendor/metal-cpp)
include_directories(${CMAKE_SOURCE_DIR}/platform_layer/mac/vendor/metal-cpp-extensions)
include_directories(${CMAKE_SOURCE_DIR}/game_layer/include)

add_library(PlatformLayer STATIC ${SOURCES})

# Add an installation rule to copy the shared library to the output directory
install(TARGETS PlatformLayer
    RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
    LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
    ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)

# Link with DirectX libraries
#target_link_libraries(PlatformLayer PRIVATE -static d3d12 dxgi)
target_link_libraries(PlatformLayer PRIVATE
    "-framework Metal"
    "-framework MetalKit"
    "-framework AppKit"
    "-framework Foundation"
    "-framework QuartzCore"
)

# Copy the PlatformLayer.dll to the output directory during build
add_custom_command(TARGET PlatformLayer POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
    $<TARGET_FILE:PlatformLayer>
    ${CMAKE_CURRENT_BINARY_DIR}/Game
)
