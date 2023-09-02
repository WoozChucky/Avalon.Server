set(This Worldserver)
project(${This})

file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.h" "include/*.h")

include_directories(${CMAKE_SOURCE_DIR}/common/include)
include_directories(${CMAKE_SOURCE_DIR}/database/include)
include_directories(${CMAKE_SOURCE_DIR}/server/shared/include)
include_directories(${CMAKE_SOURCE_DIR}/server/game/include)

add_executable(${This} ${SOURCES})

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

    find_package(Boost REQUIRED COMPONENTS filesystem)

    # Link the platform and game layers to the main executable
    # Boost::boost
    # "C:/local/Personal/Avalon.Server/src/Platform/vendor/boost/stage/lib/libboost_filesystem-mt-x64.lib"
    # "C:/local/Personal/Avalon.Server/src/Platform/vendor/boost/stage/lib/libboost_filesystem-clang16-mt-s-x64-1_84.lib"
    # "C:/local/Personal/Avalon.Server/src/Platform/vendor/boost/stage/lib/libboost_filesystem-clang16-mt-x64-1_84.lib"
    # ${CMAKE_SOURCE_DIR}/vendor/boost/stage/lib/libboost_filesystem-clang16-mt-x64-1_83.lib
    # ${CMAKE_SOURCE_DIR}/vendor/boost/stage/lib/libboost_filesystem-clangw16-mt-x64-1_83.lib
    # libboost_filesystem-clang16-mt-s-x64-1_84.lib
    target_link_libraries(${This} PRIVATE Common Database Game Boost::filesystem Boost::boost fmt ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES} libmariadb)
endif ()

if(UNIX)
    # Link the platform and game layers to the main executable
    target_link_libraries(${This} PRIVATE Common Database Game Boost::boost fmt ${SSL_LIBRARIES} ${CRYPTO_LIBRARIES} ${MariaDBClient_LIBRARY})
endif ()


