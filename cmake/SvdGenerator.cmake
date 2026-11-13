# SvdGenerator.cmake - generate the register headers of a device as part of a CMake build.
#
#   include(SvdGenerator.cmake)
#   svdgenerator_add_device(stm32h563 SVD ${CMAKE_SOURCE_DIR}/vendor/STM32H563.svd)
#   target_link_libraries(firmware PRIVATE stm32h563)
#
# and the sources of `firmware` include <STM32H563/rcc.hpp>.
#
# svdgenerator_add_device(<target>
#     SVD <file>                the CMSIS-SVD file
#     [NAME <name>]             directory the headers are included through, <name>/rcc.hpp;
#                               default: the name of the SVD file without its extension
#     [OUTPUT_DIRECTORY <dir>]  where <name>/ is created;
#                               default: ${CMAKE_CURRENT_BINARY_DIR}/svd
# )
#
# creates the INTERFACE library <target>: linking to it gives the include path and makes the
# headers be generated before anything that uses them is compiled.
#
# The generator is run by SVDGENERATOR_COMMAND, a list. When it is not set, `svdgenerator` is
# looked for on the PATH (dotnet tool install --global SvdGenerator), then `dotnet tool run
# svdgenerator` is used (a tool manifest in the repository, restored with `dotnet tool restore`).
# Set it to the path of a self-contained binary to do without the .NET SDK.
#
# The headers are regenerated when the SVD file changes. A header whose content does not change
# is not rewritten, so only what includes a header that did change is recompiled.

if(NOT DEFINED SVDGENERATOR_COMMAND)
  find_program(SVDGENERATOR_EXECUTABLE NAMES svdgenerator SvdGenerator
    DOC "SvdGenerator, as installed by `dotnet tool install SvdGenerator`")
  if(SVDGENERATOR_EXECUTABLE)
    set(SVDGENERATOR_COMMAND "${SVDGENERATOR_EXECUTABLE}")
  else()
    set(SVDGENERATOR_COMMAND dotnet tool run svdgenerator)
  endif()
endif()

function(svdgenerator_add_device target)
  cmake_parse_arguments(PARSE_ARGV 1 ARG "" "SVD;NAME;OUTPUT_DIRECTORY" "")
  if(NOT ARG_SVD)
    message(FATAL_ERROR "svdgenerator_add_device(${target}): SVD <file> is required")
  endif()
  if(ARG_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "svdgenerator_add_device(${target}): unknown arguments ${ARG_UNPARSED_ARGUMENTS}")
  endif()

  get_filename_component(svd "${ARG_SVD}" ABSOLUTE)
  if(NOT ARG_NAME)
    get_filename_component(ARG_NAME "${svd}" NAME_WLE)
  endif()
  if(NOT ARG_OUTPUT_DIRECTORY)
    set(ARG_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/svd")
  endif()
  get_filename_component(root "${ARG_OUTPUT_DIRECTORY}" ABSOLUTE)
  set(directory "${root}/${ARG_NAME}")

  # The files depend on the SVD file: ask the generator, which writes nothing here.
  execute_process(
    COMMAND ${SVDGENERATOR_COMMAND} "${svd}" "${directory}" --list
    OUTPUT_VARIABLE listed
    ERROR_VARIABLE failure
    RESULT_VARIABLE status
    OUTPUT_STRIP_TRAILING_WHITESPACE)
  if(NOT status EQUAL 0)
    message(FATAL_ERROR "svdgenerator_add_device(${target}): `${SVDGENERATOR_COMMAND}` failed (${status}).\n${failure}")
  endif()
  string(REPLACE "\r" "" listed "${listed}")
  string(REPLACE "\n" ";" files "${listed}")

  # The list was made from this SVD file: configure again when it changes.
  set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${svd}")

  # A stamp is the output of the rule, the headers its byproducts: the generator does not touch
  # a header that is up to date, so their dates cannot say that the rule has run.
  set(stamp "${root}/${ARG_NAME}.stamp")
  add_custom_command(
    OUTPUT "${stamp}"
    BYPRODUCTS ${files}
    COMMAND ${SVDGENERATOR_COMMAND} "${svd}" "${directory}"
    COMMAND "${CMAKE_COMMAND}" -E touch "${stamp}"
    DEPENDS "${svd}"
    COMMENT "Generating the register headers of ${ARG_NAME}"
    VERBATIM)
  add_custom_target(${target}_headers DEPENDS "${stamp}")

  add_library(${target} INTERFACE)
  target_include_directories(${target} INTERFACE "${root}")
  add_dependencies(${target} ${target}_headers)
endfunction()
