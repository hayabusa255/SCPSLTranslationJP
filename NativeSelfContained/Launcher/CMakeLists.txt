cmake_minimum_required(VERSION 3.24)
project(SCPSLSelfContainedLauncher LANGUAGES CXX RC)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
get_filename_component(REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
set(RUNTIME_BUILD "${REPO_ROOT}/NativeSelfContained/build")
set(RUST_CORE "${REPO_ROOT}/NativeCore/bin/win-x64/scpsl-native-core.exe")
set(RUNTIME_ZIP "${RUNTIME_BUILD}/SCPSL-WinUI-1.8-selfcontained-bundle.zip")
if(NOT EXISTS "${RUST_CORE}" OR NOT EXISTS "${RUNTIME_ZIP}")
    message(FATAL_ERROR "Build Rust core and self-contained runtime ZIP first")
endif()
file(TO_CMAKE_PATH "${REPO_ROOT}/NativeCppWinUI/Assets/app.ico" ICON_RES)
file(TO_CMAKE_PATH "${RUST_CORE}" RUST_RES)
file(TO_CMAKE_PATH "${RUNTIME_ZIP}" RUNTIME_RES)
file(TO_CMAKE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/app.manifest" MANIFEST_RES)
configure_file("${CMAKE_CURRENT_SOURCE_DIR}/launcher-resources.rc.in"
               "${CMAKE_CURRENT_BINARY_DIR}/launcher-resources.rc" @ONLY)
add_executable(SCPSLNativeSelfContained WIN32
    "${CMAKE_CURRENT_SOURCE_DIR}/main.cpp"
    "${CMAKE_CURRENT_BINARY_DIR}/launcher-resources.rc")
set_property(TARGET SCPSLNativeSelfContained PROPERTY OUTPUT_NAME "SCPSL")
set_property(TARGET SCPSLNativeSelfContained PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_compile_definitions(SCPSLNativeSelfContained PRIVATE
    UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
if(MSVC)
    target_compile_options(SCPSLNativeSelfContained PRIVATE /EHsc /utf-8)
endif()
target_link_libraries(SCPSLNativeSelfContained PRIVATE
    Kernel32.lib User32.lib Shell32.lib Ole32.lib Uuid.lib)
