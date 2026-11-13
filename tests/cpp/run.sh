#!/usr/bin/env bash
# Generates the headers of fixture.svd and checks them with a C++ compiler:
#  - semantics.cpp must compile: its static_asserts state what the headers promise;
#  - every CASE_x of must_not_compile.cpp must be rejected, with the expected message.
#
# usage: run.sh [generator command ...]
#   default generator: dotnet run on the project of this repository
# environment:
#   CXX         compiler (default g++)
#   CXXFLAGS    extra flags, e.g. -mcpu=cortex-m33 for arm-none-eabi-g++
#   BUILD_DIR   where to generate (default: build/ next to this script)
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
: "${CXX:=g++}"
build=${BUILD_DIR:-$here/build}

# dotnet and a Windows compiler want Windows paths when run from Git Bash.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

generator=("$@")
if [ ${#generator[@]} -eq 0 ]; then
  generator=(dotnet run --project "$(native "$here/../../SvdGenerator.csproj")" -c Release --no-launch-profile --)
fi

rm -rf "$build"
mkdir -p "$build/FIXTURE"
"${generator[@]}" "$(native "$here/fixture.svd")" "$(native "$build/FIXTURE")"

# A second device next to the first: two devices must compile in one
# translation unit, each with its own copy of mmio.hpp.
mkdir -p "$build/TWIN"
sed 's#<name>FIXTURE</name>#<name>TWIN</name>#' "$here/fixture.svd" > "$build/twin.svd"
"${generator[@]}" "$(native "$build/twin.svd")" "$(native "$build/TWIN")"

# The generator must leave a file it did not write alone and find nothing to change.
echo "// not generated" > "$build/FIXTURE/board.hpp"
"${generator[@]}" "$(native "$here/fixture.svd")" "$(native "$build/FIXTURE")" --check
test -f "$build/FIXTURE/board.hpp"

# Compiled for real, with the optimiser on: the check on constant field values
# is raised when code is generated, which -fsyntax-only never reaches.
# shellcheck disable=SC2206
flags=(-std=c++20 -Wall -Wextra -Wshadow -Wcast-align -Wconversion -Wsign-conversion
       -Wdouble-promotion -Werror -O1 -c -o "$(native "$build/out.o")" -I"$(native "$build")" ${CXXFLAGS:-})

fail=0

echo "semantics.cpp"
"$CXX" "${flags[@]}" "$(native "$here/semantics.cpp")" || fail=1

echo "must_not_compile.cpp, no case selected"
"$CXX" "${flags[@]}" "$(native "$here/must_not_compile.cpp")" || fail=1

# Cases named CASE_ARM_x depend on the target: they only run with a compiler for ARM.
arm=0
if "$CXX" ${CXXFLAGS:-} -dM -E -x c++ - </dev/null 2>/dev/null | grep -q '__arm__'; then arm=1; fi

while IFS= read -r line; do
  case=${line#\#ifdef }
  case=${case%% *}
  expect=${line#*// expect: }
  if [ "$arm" = 0 ] && [[ "$case" == CASE_ARM_* ]]; then
    echo "skip $case (not an ARM compiler)"
    continue
  fi
  if "$CXX" "${flags[@]}" "-D$case" "$(native "$here/must_not_compile.cpp")" 2>"$build/$case.log"; then
    echo "FAIL $case: compiled, it must not"
    fail=1
  elif ! grep -Eq -- "$expect" "$build/$case.log"; then
    echo "FAIL $case: rejected, but not with /$expect/:"
    head -5 "$build/$case.log"
    fail=1
  else
    echo "ok   $case"
  fi
done < <(grep -E '^#ifdef CASE_' "$here/must_not_compile.cpp")

exit "$fail"
