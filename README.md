# SvdGenerator

Generates typed C++ peripheral headers for ARM Cortex-M targets from a
[CMSIS-SVD](https://arm-software.github.io/CMSIS_5/SVD/html/index.html)
description file.

The output is wired to [opsy](https://github.com/Otatiaro/OpSy)'s
`utility/memory.hpp` register building blocks (`opsy::utility::memory`,
`read_only_memory`, `write_only_memory`, `clear_set`, `padding`). Drop
the generated files into a project that already exposes `<utility/memory.hpp>`
on its include path and you get strongly-typed, atomic-aware register access
out of the box.

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

Each generated header begins with `#include <utility/memory.hpp>` —
make sure that header (from opsy) is reachable.

## Generated style

- Class names: `snake_case` with a suffix —
  `*_p` for peripherals, `*_r` for registers, `*_f` for fields.
- Members: trailing underscore (`value_`).
- Statics: `Mask`, `Offset`, `Width`, `ResetValue` stay in PascalCase
  because that is what `opsy::utility`'s atomic helpers expect
  (`T::Mask`).
- All register storage types are referenced fully-qualified
  (`opsy::utility::memory<…>`, etc.).

## Example

```cpp
#include <STM32H563/rcc.hpp>

using namespace STM32H563;

rcc.rcc_ahb1enr |= rcc_p::rcc_ahb1enr_r::gpdma1en_f() | opsy::utility::atomic;
```

## Releases

Tag a commit with a SemVer tag and push it; the release workflow
publishes self-contained `win-x64`, `linux-x64`, and `osx-arm64`
binaries to a GitHub Release.

```sh
git tag v0.1.0
git push origin v0.1.0
```

Untagged local builds carry the version `0.0.0-dev`.

## License

[MIT](LICENSE).
