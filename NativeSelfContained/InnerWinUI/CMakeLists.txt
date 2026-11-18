cmake_minimum_required(VERSION 3.24)
project(SCPSLSelfContainedInner LANGUAGES CXX RC)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
get_filename_component(REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
set(RUNTIME_BUILD "${REPO_ROOT}/NativeSelfContained/build")
set(WINUI_ROOT "${REPO_ROOT}/NativeCppWinUI")
set(RUST_CORE "${REPO_ROOT}/NativeCore/bin/win-x64/scpsl-native-core.exe")
set(ACTIVATION_MANIFEST "${RUNTIME_BUILD}/app-selfcontained.manifest")
foreach(required
    "${WINUI_ROOT}/.winapp/include"
    "${WINUI_ROOT}/.winapp/lib/x64"
    "${RUNTIME_BUILD}/runtime/resources.pri"
    "${ACTIVATION_MANIFEST}" "${RUST_CORE}")
  if(NOT EXISTS "${required}")
    message(FATAL_ERROR "Missing required self-contained build input: ${required}")
  endif()
endforeach()
file(TO_CMAKE_PATH "${WINUI_ROOT}/Assets/app.ico" ICON_RES)
file(TO_CMAKE_PATH "${WINUI_ROOT}/Assets/researcher.jpg" RESEARCHER_RES)
file(TO_CMAKE_PATH "${WINUI_ROOT}/Assets/scp-emblem.png" EMBLEM_RES)
file(TO_CMAKE_PATH "${RUST_CORE}" RUST_RES)
file(TO_CMAKE_PATH "${ACTIVATION_MANIFEST}" MANIFEST_RES)
configure_file("${CMAKE_CURRENT_SOURCE_DIR}/inner-resources.rc.in"
               "${CMAKE_CURRENT_BINARY_DIR}/inner-resources.rc" @ONLY)
add_executable(SCPSLWinUI WIN32
    "${WINUI_ROOT}/main.cpp"
    "${WINUI_ROOT}/markdown_renderer.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/resource_loader.cpp"
    "${CMAKE_CURRENT_BINARY_DIR}/inner-resources.rc")
set_property(TARGET SCPSLWinUI PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_compile_definitions(SCPSLWinUI PRIVATE UNICODE _UNICODE
    WIN32_LEAN_AND_MEAN NOMINMAX)
if(MSVC)
    target_compile_options(SCPSLWinUI PRIVATE /bigobj /EHsc /utf-8)
endif()
target_include_directories(SCPSLWinUI PRIVATE
    "${WINUI_ROOT}" "${WINUI_ROOT}/.winapp/include")
target_link_directories(SCPSLWinUI PRIVATE "${WINUI_ROOT}/.winapp/lib/x64")
target_link_libraries(SCPSLWinUI PRIVATE
    WindowsApp.lib OneCoreUap.lib Shell32.lib Ole32.lib User32.lib Uuid.lib)
