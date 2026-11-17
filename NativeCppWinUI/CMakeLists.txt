cmake_minimum_required(VERSION 3.24)
project(SCPSLWinUI LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_program(WINAPP_CLI winapp)
if(NOT WINAPP_CLI)
  set(WINAPP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/.winapp-tools")
  set(WINAPP_CLI "${WINAPP_DIR}/winapp.exe")
  if(NOT EXISTS "${WINAPP_CLI}")
    file(DOWNLOAD
      "https://github.com/microsoft/WinAppCli/releases/latest/download/winappcli-x64.zip"
      "${CMAKE_CURRENT_BINARY_DIR}/winappcli.zip"
      SHOW_PROGRESS STATUS WINAPP_FETCH)
    list(GET WINAPP_FETCH 0 WINAPP_FETCH_CODE)
    if(NOT WINAPP_FETCH_CODE EQUAL 0)
      message(FATAL_ERROR "Unable to download Microsoft WinApp CLI: ${WINAPP_FETCH}")
    endif()
    file(ARCHIVE_EXTRACT INPUT "${CMAKE_CURRENT_BINARY_DIR}/winappcli.zip"
         DESTINATION "${WINAPP_DIR}")
  endif()
endif()
if(NOT EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/.winapp/include")
  execute_process(COMMAND "${WINAPP_CLI}" restore
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
    RESULT_VARIABLE RESTORE_EXIT)
  if(NOT RESTORE_EXIT EQUAL 0)
    message(FATAL_ERROR "WinApp CLI restore failed with exit code ${RESTORE_EXIT}")
  endif()
endif()

add_executable(SCPSLWinUI WIN32 main.cpp app.manifest)
target_compile_definitions(SCPSLWinUI PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
if(MSVC)
  target_compile_options(SCPSLWinUI PRIVATE /bigobj /EHsc /utf-8)
endif()
target_include_directories(SCPSLWinUI PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/.winapp/include")
target_link_directories(SCPSLWinUI PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/.winapp/lib/x64")
target_link_libraries(SCPSLWinUI PRIVATE
  WindowsApp.lib OneCoreUap.lib
  Microsoft.WindowsAppRuntime.Bootstrap.lib
  Shell32.lib Ole32.lib User32.lib Uuid.lib)

add_custom_command(TARGET SCPSLWinUI POST_BUILD
  COMMAND "${CMAKE_COMMAND}" -E copy_directory
    "${CMAKE_CURRENT_SOURCE_DIR}/Assets" "$<TARGET_FILE_DIR:SCPSLWinUI>/Assets")
if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/../NativeCore/bin/win-x64/scpsl-native-core.exe")
  add_custom_command(TARGET SCPSLWinUI POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
      "${CMAKE_CURRENT_SOURCE_DIR}/../NativeCore/bin/win-x64/scpsl-native-core.exe"
      "$<TARGET_FILE_DIR:SCPSLWinUI>/scpsl-native-core.exe")
endif()