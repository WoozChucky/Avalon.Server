include(FetchContent)
FetchContent_Declare(
        googletest
        URL https://github.com/google/googletest/archive/03597a01ee50ed33e9dfd640b249b4be3799d395.zip
)

if (WIN32)
    set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
    FetchContent_MakeAvailable(googletest)

    add_subdirectory(windows)
elseif(UNIX AND NOT APPLE)
    add_subdirectory(linux)
elseif(APPLE)
    add_subdirectory(mac)
else()
    message(FATAL_ERROR "Unsupported platform")
endif()
