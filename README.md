# SvdGenerator

Generates typed C++ peripheral headers for ARM Cortex-M targets from a
[CMSIS-SVD](https://arm-software.github.io/CMSIS_5/SVD/html/index.html)
description file.

The output is wired to [opsy](https://github.com/Otatiaro/OpSy)'s
[`utility/memory.hpp`](https://github.com/Otatiaro/OpSy/blob/9df189a2dc78ff306d8982fc025e164371c5d3fc/utility/memory.hpp)
register building blocks (`opsy::utility::memory`, `read_only_memory`,
`write_only_memory`, `clear_set`, `padding`). The link is pinned to the
exact commit the generator targets, so it survives any future breaking
change.

You have two ways to provide that header:

- **With opsy on your include path**, no extra step — the generated
  files use `<utility/memory.hpp>`.
- **Without opsy**, drop a copy of `memory.hpp` next to the generated
  headers (lowercase, same directory). Each file probes
  `__has_include("memory.hpp")` first and falls back to
  `<utility/memory.hpp>` only if no local copy is found.

Either way you get strongly-typed, atomic-aware register access out of
the box.

## Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build -c Release
```

Pre-built self-contained binaries for Windows, Linux, and macOS are
attached to each [GitHub Release](../../releases).

## Usage

```sh
SvdGenerator <input.svd> <output_dir>
```

Reads the SVD file, deletes any existing files in `<output_dir>`, and
writes one `.hpp` per peripheral group plus an `interrupts.hpp`. The
output namespace is the device name in upper case (e.g. `STM32H563`).

Each generated header begins with:

```cpp
#if __has_include("memory.hpp")
#include "memory.hpp"
#else
#include <utility/memory.hpp>
#endif
```

so it picks up either the local copy you dropped in the same directory
or the opsy-shipped version on the include path.

## Generated style

- All identifiers are `snake_case`. Same convention as opsy core.
- Class names carry a suffix to disambiguate the kind:
  `*_p` for peripherals, `*_r` for registers, `*_f` for fields.
- Members use a trailing underscore (`value_`).
- Field constants on the field class: `mask`, `offset`, `width`,
  `range`, and `reset_value` on the register class.
- All register storage types are referenced fully qualified
  (`opsy::utility::memory<…>`, etc.).
- Field accessors that would shadow a C++ keyword (`int`, `signed`,
  …) or the register's own `value()` method are prefixed with an
  underscore (`_int`, `_value`).

## Example

```cpp
#include <STM32H563/rcc.hpp>

using namespace STM32H563;

rcc.rcc_ahb1enr |= rcc_p::rcc_ahb1enr_r::gpdma1en_f() | opsy::utility::atomic;
```

## Tested targets

The CI smoke matrix builds the generator, runs it against a pinned
SVD from each vendor below, drops the opsy `memory.hpp` next to the
output, and syntax-checks every generated peripheral header with
`g++ -std=c++20 -Wall -Wextra -Wshadow -Wcast-align -Wconversion -Wsign-conversion -Wdouble-promotion -Werror`
(the strictest set opsy's own BSPs compile with).

| Vendor | Core | SVD pulled from |
|---|---|---|
| STMicro / STM32F401 | Cortex-M4 | [`cmsis-svd-data/data/STMicro/STM32F401.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/STMicro/STM32F401.svd) |
| Raspberry Pi / RP2040 | Cortex-M0+ | [`cmsis-svd-data/data/RaspberryPi/rp2040.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/RaspberryPi/rp2040.svd) |
| Microchip (Atmel) / ATSAMD21G18A | Cortex-M0+ | [`cmsis-svd-data/data/Atmel/ATSAMD21G18A.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/Atmel/ATSAMD21G18A.svd) |
| NXP Kinetis / MK64F12 | Cortex-M4 | [`cmsis-svd-data/data/NXP/MK64F12.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/NXP/MK64F12.svd) |
| Nordic / nRF52840 | Cortex-M4 | [`cmsis-svd-data/data/Nordic/nrf52840.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/Nordic/nrf52840.svd) |

The exact upstream commit the smoke compiles against is pinned in
`.github/workflows/ci.yml` (env vars `OPSY_REF` and `SVD_DATA_REF`),
so a one-line bump is all it takes to refresh.

### CMSIS-SVD constructs the generator handles

- Register arrays via `<dim>` / `<dimIncrement>` / `<dimIndex>`, with
  both `name%s` (Atmel, Microchip) and `name[%s]` (Nordic) name
  forms. The `[ ]` brackets are stripped from the resulting C++
  identifier.
- `<register derivedFrom="ParentName">` with attribute inheritance
  for `size`, `fields`, `access`, `resetValue`, and `resetMask`.
- `<bitRange>[msb:lsb]` field bit-range notation alongside the more
  common `<bitOffset>` / `<bitWidth>` and `<lsb>` / `<msb>` pairs.
- Optional `<size>` / `<groupName>`: defaults to 32 bits for the
  former and to the peripheral's own name for the latter.
- Byte / half / word access aliases that overlap a wider register
  (NXP Kinetis CRC/SPI, Atmel SAM PORT). They are laid out as an
  anonymous `union` of named lane structs (`_lane_N`); `static_assert(offsetof(...))`
  checks every register against the SVD-stated offset.
- Nordic's non-spec `<access>read-writeonce</access>` typo (lowercase
  `o`) is patched in-place before deserialization.

### Known limitations

- Register-level `<cluster>` is not handled — the generator only
  walks direct `<register>` children of a peripheral.
- Anonymous structs are emitted as named `_lane_N` fields rather
  than truly anonymous (GCC's anonymous-struct extension forbids
  non-POD members, and `opsy::utility::memory` embeds a
  `std::atomic`). Byte/half aliases are reached via
  `peripheral._lane_N.fooN` rather than `peripheral.fooN`.
- The `<dimIndex>` form `A,B,C` (named indices) collapses to
  `A`/`B`/`C`; the range form `0-3` is expanded; everything else
  falls back to numeric indices.
- Peripheral-level `<dim>` is not expanded.

## Releases

Tag a commit with a SemVer tag and push it; the release workflow
publishes self-contained `win-x64`, `linux-x64`, and `osx-arm64`
binaries to a GitHub Release.

```sh
git tag v0.2.0
git push origin v0.2.0
```

Untagged local builds carry the version `0.0.0-dev`.

## License

[MIT](LICENSE).
