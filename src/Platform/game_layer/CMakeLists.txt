file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "include/*.h")

include_directories(${CMAKE_SOURCE_DIR}/game_layer/include)

add_library(GameLayer SHARED ${SOURCES})

# Add an installation rule to copy the shared library to the output directory
install(TARGETS GameLayer
        RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)

# Copy the GameLayer.dll to the output directory during build
add_custom_command(TARGET GameLayer POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
    $<TARGET_FILE:GameLayer>
    $<TARGET_FILE_DIR:Client>
)
