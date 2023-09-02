set(This Common)

include_directories(include)
include_directories(${CMAKE_BINARY_DIR})

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "src/**/*.h" "include/*.h" "include/**/*.h")

add_library(${This} STATIC ${SOURCES})

target_link_libraries(${This} PRIVATE Boost::boost ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES})

# Add an installation rule to copy the shared library to the output directory
install(TARGETS ${This}
        RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)
