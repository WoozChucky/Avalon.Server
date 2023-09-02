set(This Game)

include_directories(${CMAKE_SOURCE_DIR}/common/include)
include_directories(${CMAKE_SOURCE_DIR}/database/include)
include_directories(${CMAKE_SOURCE_DIR}/server/shared/include)

include_directories(include)

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "src/**/*.h" "include/*.h" "include/**/*.h")

add_library(${This} STATIC ${SOURCES})

# Add an installation rule to copy the database library to the output directory
install(TARGETS ${This}
        RUNTIME DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        LIBRARY DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
        ARCHIVE DESTINATION ${CMAKE_CURRENT_BINARY_DIR}
)

if(WIN32)
    add_custom_command(
            TARGET ${This} PRE_LINK
            COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${MariaDBClient_ROOT_DIR}/lib
            ${CMAKE_BINARY_DIR})

    add_custom_command(
            TARGET ${This} POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different
            ${MariaDBClient_LIBRARY}
            ${CMAKE_BINARY_DIR}/bin)

    target_link_libraries(${This} PRIVATE fmt Common Shared Boost::boost libmariadb ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES})
endif ()

if(UNIX)
    target_link_libraries(${This} PRIVATE fmt Common Shared Boost::boost ${MariaDBClient_LIBRARY} ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES})
endif ()
