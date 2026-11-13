# Register headers generated during the build, from an SVD file kept in the repository.
# Nothing generated is committed. Built by the CI of SvdGenerator.
cmake_minimum_required(VERSION 3.20)
project(svdgenerator_example CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(${CMAKE_CURRENT_SOURCE_DIR}/../../cmake/SvdGenerator.cmake)

# Headers included as <FIXTURE/ctl.hpp>, generated in ${CMAKE_CURRENT_BINARY_DIR}/svd/FIXTURE.
svdgenerator_add_device(fixture
  SVD ${CMAKE_CURRENT_SOURCE_DIR}/../../tests/cpp/fixture.svd
  NAME FIXTURE)

# An object library: the example touches registers at fixed addresses, it is compiled, not run.
add_library(driver OBJECT driver.cpp)
target_link_libraries(driver PRIVATE fixture)
