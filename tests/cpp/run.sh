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
#   MEMORY_HPP  path to opsy's utility/memory.hpp; downloaded at OPSY_REF when unset
#   OPSY_REF    opsy commit to download memory.hpp from
#   BUILD_DIR   where to generate (default: build/ next to this script)
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
: "${CXX:=g++}"
: "${OPSY_REF:=9df189a2dc78ff306d8982fc025e164371c5d3fc}"
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

if [ -n "${MEMORY_HPP:-}" ]; then
  cp "$MEMORY_HPP" "$build/FIXTURE/memory.hpp"
else
  curl -fsSL "https://raw.githubusercontent.com/Otatiaro/OpSy/${OPSY_REF}/utility/memory.hpp" -o "$build/FIXTURE/memory.hpp"
fi

# The generator must leave the memory.hpp alone and find nothing to change.
"${generator[@]}" "$(native "$here/fixture.svd")" "$(native "$build/FIXTURE")" --check
test -f "$build/FIXTURE/memory.hpp"

# shellcheck disable=SC2206
flags=(-std=c++20 -Wall -Wextra -Wshadow -Wcast-align -Wconversion -Wsign-conversion
       -Wdouble-promotion -Werror -O1 -fsyntax-only -I"$(native "$build")" ${CXXFLAGS:-})

fail=0

echo "semantics.cpp"
"$CXX" "${flags[@]}" "$(native "$here/semantics.cpp")" || fail=1

echo "must_not_compile.cpp, no case selected"
"$CXX" "${flags[@]}" "$(native "$here/must_not_compile.cpp")" || fail=1

while IFS= read -r line; do
  case=${line#\#ifdef }
  case=${case%% *}
  expect=${line#*// expect: }
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
