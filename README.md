# SvdGenerator

Generates typed C++ peripheral headers for ARM Cortex-M targets from a
[CMSIS-SVD](https://arm-software.github.io/CMSIS_5/SVD/html/index.html)
description file.

```cpp
#include <STM32H563/gpdma.hpp>
#include <STM32H563/gpio.hpp>
#include <STM32H563/rcc.hpp>

using namespace STM32H563;
using mmio::atomic;

rcc.ahb2enr |= rcc_p::ahb2enr_r::gpioden_f() | atomic;                      // atomic set
gpiod.moder <<= gpio_p::moder_r::mode8_f(0b10) || gpio_p::moder_r::mode9_f(0b10) || atomic; // atomic clear-and-set
while (!rcc.cr.load().hserdy()) {}                                          // typed read
gpdma1.c[2].cr = gpdma_p::c_c::cr_r::tcie_f() | gpdma_p::c_c::cr_r::en_f(); // channel 2
```

The generated headers are self-contained: they need C++20, GCC or Clang,
and the standard library. The register building blocks they are made of
(`mmio::memory`, `read_only_memory`, `write_only_memory`, `clear_set`,
`padding`, the `atomic` tag) are written next to them, in
`mmio.hpp`. The source of that file is
[`Support/mmio.hpp`](Support/mmio.hpp); its header
comment says what `atomic` guarantees and where it depends on the device.

The headers work with any runtime, or none. They were first written for
[opsy](https://github.com/Otatiaro/OpSy), whose `utility/memory.hpp` these
building blocks come from.

## Install

Each [GitHub Release](../../releases) carries:

- **Self-contained binaries** for Windows, Linux and macOS: nothing to
  install, run the file.
- **A .NET tool package** (`SvdGenerator.<version>.nupkg`), for machines
  that have the [.NET 10 SDK](https://dotnet.microsoft.com/download).
  The release workflow also pushes it to nuget.org:

  ```sh
  dotnet tool install --global SvdGenerator
  svdgenerator <input.svd> <output_dir>
  ```

  To install from the file of a release instead, download it into a
  directory and add `--add-source <directory> --version <version>`.

  A repository can pin the version it generates its headers with in a
  [tool manifest](https://learn.microsoft.com/dotnet/core/tools/local-tools-how-to-use)
  (`dotnet new tool-manifest`, then `dotnet tool install SvdGenerator`),
  so that `dotnet tool restore` gives every machine and the CI the same
  generator, run as `dotnet tool run svdgenerator`.

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build -c Release
```

## Usage

```sh
SvdGenerator <input.svd> <output_dir> [--check] [--force]
```

Reads the SVD file and writes into `<output_dir>`:

- one `.hpp` per group of peripherals (`gpio.hpp`, `tim.hpp`, ...);
- `interrupts.hpp`, the interrupt numbers;
- `device.hpp`, the name of the device and what the SVD says of its core;
- `vectors.hpp`, the vector table of the device, built at compile time;
- `mmio.hpp`, the register building blocks, the same for every
  device. Two devices generated in two directories can be included in one
  translation unit;
- `README.md`, the page of the directory: where the headers come from
  and where each peripheral is.

Everything is in a namespace named after the device, in upper case
(e.g. `STM32H563`).

### Committing the headers, or generating them during the build

Both work; they do not cost the same.

**Committed headers.** Generate once, commit the directory, and let the CI
run `--check` (below) so that the headers cannot drift from the SVD file
and the generator. Building the firmware needs no generator and no .NET,
and a change of SVD file or of generator shows as a diff to review.

**Headers generated during the build.** Only the SVD file is in the
repository. [`cmake/SvdGenerator.cmake`](cmake/SvdGenerator.cmake) does
it for CMake:

```cmake
include(SvdGenerator.cmake)
svdgenerator_add_device(stm32h563 SVD ${CMAKE_SOURCE_DIR}/vendor/STM32H563.svd)
target_link_libraries(firmware PRIVATE stm32h563)   # <STM32H563/rcc.hpp>
```

The headers go to the build directory and are generated before anything
that includes them is compiled. They are regenerated when the SVD file
changes, and a header whose content does not change is not rewritten, so
it recompiles nothing. [`examples/cmake`](examples/cmake) is a complete
project, built by the CI. What this costs:

- every machine that builds the firmware needs the generator: the .NET
  SDK and the tool, or a self-contained binary given through
  `SVDGENERATOR_COMMAND`;
- nothing shows in a pull request when the generated API changes;
- tools that skip what git ignores, code search and language models
  among them, do not see headers that live in a build directory;
- a change of the SVD file changes the checksum written at the top of
  every header, so everything that includes one is recompiled.

For another build system, `--list` prints the files a run would write,
one per line, without writing them:

```sh
SvdGenerator <input.svd> [<output_dir>] --list
```

### Finding one's way in the output

The headers of a real device are large: 9.8 MB for the STM32H563, 700 KB
and 12 800 lines for its `rcc.hpp` alone. Nobody reads that from the top,
and a language model cannot. Two generated things replace the reading:

- **`README.md`** says which SVD file, which version and checksum, which
  generator the directory comes from, reproduces the notice of the SVD,
  and lists every peripheral with its address, its class, its header, the
  line where the class starts and its interrupts.
- **Each peripheral header starts with a map**, one line per register:

  ```cpp
  // class rcc_p, lines 87-12786: rcc 0x44020C00, sec_rcc 0x54020C00
  //   offset  access  member   class      lines     fields
  //   0x000   rw      cr       cr_r       93-609    hsion[0] hsirdy[1](r) hsikeron[2] hsidiv[4:3] ...
  //   0x010   rw      hsicfgr  hsicfgr_r  611-673   hsical[11:0](r) hsitrim[22:16]
  //   0x014   r       crrcr    crrcr_r    675-714   hsi48cal[9:0]
  ```

  `lines` runs from the comment of the class to its closing brace: read
  those lines and you have the register, its fields, their descriptions
  and their named values. A cluster is one line followed by its
  registers, with offsets from its own start. `(r)` and `(w)` mark a
  field that is read-only or write-only in a register that is not.

The map of `rcc.hpp` is 58 lines; the maps of the whole STM32H563 are 2%
of its headers.

A `README.md` of your own in the output directory is not overwritten:
the run stops with exit code 2. The generated one says what yours
probably said; delete yours, or pass `--force` once.

Everything is generated in memory first: when the SVD cannot be read or
converted, the directory is left as it was and the error is one line on
stderr.

### What happens to the files already there

The generator only replaces or removes files it wrote itself, which it
recognises by their first line. A `CMakeLists.txt` or a linker script
next to the headers are left alone, and so are headers of your own. Generated headers that the SVD no longer produces are removed,
and a header that is already up to date is not rewritten, so its
timestamp does not move.

A file that has the name of a generated header but was not generated is
not overwritten: the run stops with exit code 2. `--force` overwrites it.

### Checking that committed headers are current

```sh
SvdGenerator <input.svd> <output_dir> --check
```

writes nothing and exits with 3, listing the files, when the directory is
not what this version generates from this SVD. Line endings are ignored.
Run it in CI when the generated headers are committed.

Exit codes: 0 success, 1 error, 2 refused to overwrite, 3 out of date.

### What a generated file starts with

```cpp
// Generated by SvdGenerator 1.0.0; do not edit.
// Source: STM32H563.svd (device STM32H563, version 1.7)
// SHA-256: 3daa2bdf5fab3e6995c050489e6033326628fb6471d81e922bd3639b579e740f
//
// What this file takes from the source file (names, addresses, descriptions)
// remains under the notice that file carries:
//
//   Copyright (c) 2025 STMicroelectronics.
//   ...
//
// SvdGenerator puts no license of its own on the code it generates around it.
```

The notice is the comment that precedes the root element of the SVD and
its `<licenseText>`, reproduced as it is. Line endings are LF on every
platform. See [License](#license) for whose is what in a generated file.

## The generated API

### Names

- All identifiers are `snake_case`.
- Class names carry a suffix that tells their kind: `*_p` for a
  peripheral, `*_c` for a cluster (a group of registers), `*_r` for a
  register, `*_f` for a field.
- A register does not repeat the name of its peripheral: `RCC_AHB1ENR`
  of `RCC` is `rcc.ahb1enr`, of class `rcc_p::ahb1enr_r`. The prefix
  removed is the peripheral's name or its group's, with or without the
  instance number (`TIM1_CR1`, `GPIO_MODER` in `GPIOC`, `GPDMA_C0CR` in
  `GPDMA1`).
- A name that would be a C++ keyword (`int`, `signed`, `or`, ...), clash
  with a member of its class (`value`) or start with a digit is prefixed
  with an underscore (`_int`, `_value`, `_1x`).
- Field constants: `mask`, `offset`, `width`, and `range` for a field
  wider than one bit. `reset_value` on the register class.

### One type per kind of peripheral

Peripherals with the same registers are instances of one class, whether
or not the SVD derives one from the other. The STM32H563 file derives
`GPIOD` from `GPIOC` but describes `GPIOA` and `GPIOB` in full; the
STM32H523 file describes every port in full. Both give:

```cpp
inline gpio_p& gpioa = *reinterpret_cast<gpio_p*>(0x42020000);
inline gpio_p& gpiob = *reinterpret_cast<gpio_p*>(0x42020400);
```

so that one function serves every port, and the same driver source
compiles for both devices:

```cpp
void make_output(gpio_p& port) { port.moder <<= gpio_p::moder_r::mode0_f(0b01) || atomic; }
```

The class takes the name of the SVD group when the group is one type
(`usart_p` for `USART1`, `UART4`, ...), otherwise what the names of its
peripherals have in common (`gpio_p`). When several types would get the
same name, each keeps the name of its first peripheral: `tim1_p` is the
class of `tim1` and `tim8`, `tim2_p` that of `tim2` and `tim5`, all in
`tim.hpp`.

Two peripherals are only merged when the SVD puts them in one group or
their names show they are related; a `CRC` and an `RNG` that both have a
lone `CR` stay two types.

Descriptions and reset values do not make two peripherals differ. A reset
value common to every instance is `reg_r::reset_value`; one that is not
is given per instance instead:

```cpp
namespace gpioa_reset { inline constexpr uint32_t moder = 0xABFFFFFF; }
namespace gpiob_reset { inline constexpr uint32_t moder = 0xFFFFFEBF; }
```

### Arrays

Three things become arrays or nested classes:

- **A `<cluster>`** is a nested class and a member of it; with `<dim>`,
  an array: `p0.pin_cnf[3]`, `rtc.mode0.ctrl`.
- **A `<register>` with `<dim>` and the `name[%s]` form** is an array
  of registers. The `name%s` form stays a list of separately named
  registers, as the specification intends.
- **Numbered blocks of registers.** Vendors that do not use `<cluster>`
  spell a repeated block as numbered names (`C0CR`, `C0SAR`, `C1CR`,
  `C1SAR`, ...). When the blocks are numbered from 0, sit at a constant
  distance and agree on their registers, they are an array of one class:

  ```cpp
  for (auto& channel : gpdma1.c)
      channel.cr = gpdma_p::c_c::cr_r::reset_f();
  ```

  A register or a field that only some of the blocks have (the
  2D-addressing registers of the last two GPDMA channels) is given to
  all of them, as the vendor's own C headers do.

  The array is named after what precedes the number (`c` for the GPDMA
  channels, `s` for the streams of the STM32F4 DMA). Blocks numbered
  from 1 stay separate registers: `x[0]` for a register named `X1` would
  mislead. So do runs of single registers (`CCR1` ... `CCR4`), and blocks
  that disagree on the position of a field.

### Named values

The `<enumeratedValues>` of a field are constants of the field's own
type, usable wherever a field is:

```cpp
ctl.cr = ctl_p::cr_r::mode_f::fast | ctl_p::cr_r::en_f();
if (ctl.cr.load().mode() == ctl_p::cr_r::mode_f::fast) { ... }
```

Every value and its description is also listed in the comment of the
field class, where an IDE shows it:

```cpp
/**
 * system clock and trace clock switch ...
 *
 *   0x0  HSI selected as system clock (hsi_ck) (default after reset)
 *   0x3  PLL1 selected as system clock (pll1_p_ck for sys_ck)
 */
class sw_f {
```

A value whose SVD name is only its number gets no constant. ST names
all of its values that way (`B_0x3`), so on an ST file the values are
documented but are still written as numbers: `sw_f(0b011)`.

### Interrupts and device

`interrupts.hpp` holds `irq_count` and the unscoped `enum IRQn_Type :
std::uint16_t` (`USART3_IRQn`, ...), in the device namespace.
`device.hpp` holds `device_name`, `device_version` and, when the SVD has
a `<cpu>`, `cpu_name`, `cpu_revision`, `nvic_priority_bits`, `has_mpu`
and `has_fpu`.

### Vector table

`vectors.hpp` builds the vector table as a constant: the initial stack
pointer, the reset handler, the system exceptions, one slot per device
interrupt. Nothing is copied or patched at run time.

```cpp
#include <STM32H563/vectors.hpp>

using namespace STM32H563;

[[gnu::section(".isr_vector"), gnu::used]]
constexpr auto g_vectors = vector_table<Default_Handler>(&_estack, Reset_Handler)
    .with<exception::hard_fault, Fault_Handler>()
    .with<USART3_IRQn, uart::rx_isr>()
    .with<GPDMA1_CH0_IRQn, uart::tx_isr>();
```

- **`vector_table<Default_Handler>` names the handler for the
  unhandled**: every system exception and device interrupt that is given
  no handler gets it. `vector_table<>` leaves those slots null. The slots
  the core reserves are null either way, and it is installed as it is,
  without the decoration below.
- **`with<>` only fills a slot that has no handler yet.** Giving a
  handler to an exception or an interrupt that already has one is a
  compile error (`handler_already_installed`), whoever installed the
  first: you, or a library through `with(install)`. `replace<>` is how
  to mean it:

  ```cpp
  constexpr auto g_test_vectors = g_vectors.replace<USART3_IRQn, uart::loopback_isr>();
  ```

- **`install<>` and `reinstall<>` do the same in place**, for a table
  copied to RAM and pointed to by VTOR: one store, no copy of the table.

  ```cpp
  ram_vectors.reinstall<USART3_IRQn, uart::loopback_isr>();
  ```

  At run time `install<>` on a slot that already has a handler cannot
  stop the build, so it calls `handler_already_installed`, which the
  generated header declares and your program defines, to report the
  mistake its own way. It is given what was already taken, as the
  enumerator it is, and must not return. There is one overload for the
  device interrupts and one for the system exceptions:

  ```cpp
  [[noreturn]] void STM32H563::handler_already_installed(IRQn_Type taken)
  {
      fatal("interrupt %u already has a handler", static_cast<unsigned>(taken));
  }

  [[noreturn]] void STM32H563::handler_already_installed(exception taken);
  ```

  A program defines the ones it can reach: none when it only builds
  constant tables or only uses `reinstall<>`, the first when it installs
  interrupts at run time. The check does not depend on `NDEBUG`.
- **`exception` holds the system exceptions of the core** the SVD names:
  five on a Cortex-M0+, nine on a Cortex-M4, ten with `secure_fault` on a
  Cortex-M33. An exception the core does not have does not compile.
- **`with<>` only takes this device's enumerators**: not a bare number,
  not the interrupt of another device.
- **`with(install)` is `install(*this)`.** A kernel places its own
  handlers that way, without the generated code knowing the kernel and
  without the kernel knowing the type of the table: it states what it
  expects with a concept. `vector_table::exception_type` and `irq_type`
  give generic code the enumerations.

  ```cpp
  constexpr auto g_vectors = vector_table<>(&_estack, Reset_Handler)
      .with(kernel::scheduler_isrs)        // svcall, pendsv, systick
      .with<USART3_IRQn, uart::rx_isr>();
  ```

- **`vector_table<Default_Handler, Decoration>` wraps the handlers of
  the device interrupts**, for a kernel or a tracer that runs code around
  each of them. `Decoration` is a type with a member
  `template<isr_handler_t> static constexpr isr_handler_t wrap`; system
  exceptions are never decorated.

A device whose SVD names a Cortex-A core gets no `vectors.hpp`; one whose
SVD names no core gets every Cortex-M system exception.

## What the generated code refuses

Each of these is a compile error, checked by `tests/cpp/must_not_compile.cpp`:

- **A field given to another register.** `rcc.ahb2enr |= rcc_p::ahb1enr_r::gpdma1en_f()`
  does not compile, nor does `a_f() | b_f()` between fields of two registers.
- **A register read or written against its access.** The storage type
  follows the register's access (`read_only_memory`, `write_only_memory`).
  When the SVD states the access on the fields only, as ST does, the
  register takes what they have in common: a status register made of
  read-only flags cannot be assigned, an interrupt-clear register made of
  write-only bits cannot be read.
- **A read-only field written.** It has no conversion to the register
  value, so it cannot reach `=`, `|=` or `<<=`. It is still read
  (`rcc.cr.load().hserdy()`), and its `mask` is still there. When the
  SVD is wrong about a field, build the value from the raw register
  constructor: `reg_r(reg_r::field_f::mask)`.
- **A constant that does not fit its field.** `pll1n_f(600)` on a 9-bit
  field stops the build with *this constant does not fit the register
  field it is given to*. GCC raises it when it optimises (`-Og` and
  above, not `-O0`); in a constant expression every compiler does. A
  value only known at run time is masked to the width of the field.
- **`|` where `||` is meant.** `reg <<= (a_f(1) | b_f(2)) || atomic` is
  rejected on that line.

Register values also compare and mask: `==`, `!=`, `&`, `^`, `|`, `~`.

## Tested targets

The CI smoke matrix builds the generator, runs it against a pinned
SVD from each vendor below, and syntax-checks every generated header with
`g++ -std=c++20 -Wall -Wextra -Wshadow -Wcast-align -Wconversion -Wsign-conversion -Wdouble-promotion -Werror`.

| Vendor | Core | SVD pulled from |
|---|---|---|
| STMicro / STM32F401 | Cortex-M4 | [`cmsis-svd-data/data/STMicro/STM32F401.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/STMicro/STM32F401.svd) |
| Raspberry Pi / RP2040 | Cortex-M0+ | [`cmsis-svd-data/data/RaspberryPi/rp2040.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/RaspberryPi/rp2040.svd) |
| Microchip (Atmel) / ATSAMD21G18A | Cortex-M0+ | [`cmsis-svd-data/data/Atmel/ATSAMD21G18A.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/Atmel/ATSAMD21G18A.svd) |
| NXP Kinetis / MK64F12 | Cortex-M4 | [`cmsis-svd-data/data/NXP/MK64F12.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/NXP/MK64F12.svd) |
| Nordic / nRF52840 | Cortex-M4 | [`cmsis-svd-data/data/Nordic/nrf52840.svd`](https://github.com/cmsis-svd/cmsis-svd-data/blob/main/data/Nordic/nrf52840.svd) |

The exact upstream commit the SVD files are taken from is pinned in
`.github/workflows/ci.yml` (env var `SVD_DATA_REF`), so a one-line bump
is all it takes to refresh.

### CMSIS-SVD constructs the generator handles

- `<peripheral derivedFrom>` chains of any length, and `<dim>` on a
  peripheral (one instance per index).
- `<cluster>`, nested or not, with or without `<dim>`.
- Register arrays via `<dim>` / `<dimIncrement>` / `<dimIndex>`, with
  both `name%s` (Atmel, Microchip) and `name[%s]` (Nordic) name forms,
  and `<dim>` on a field.
- `<register derivedFrom="ParentName">` with attribute inheritance
  for `size`, `fields`, `access`, `resetValue`, and `resetMask`.
- `<size>`, `<access>` and `<resetValue>` inherited from the cluster,
  the peripheral and the device; 32 bits when no `<size>` is given
  anywhere.
- `<bitRange>[msb:lsb]` field bit-range notation alongside the more
  common `<bitOffset>` / `<bitWidth>` and `<lsb>` / `<msb>` pairs.
- `<enumeratedValues>`, including `derivedFrom`.
- Optional `<groupName>`.
- Byte / half / word access aliases that overlap a wider register
  (NXP Kinetis CRC/SPI, Atmel SAM PORT), and alternate clusters (Atmel
  SAM RTC modes). They are laid out as an anonymous `union`;
  `static_assert(offsetof(...))` checks every register against the
  SVD-stated offset.
- Nordic's non-spec `<access>read-writeonce</access>` typo (lowercase
  `o`) is patched in-place before deserialization.

### Known limitations

- Overlapping registers that do not start where the widest one does
  are emitted in named `_lane_N` structs rather than truly anonymous
  ones (GCC's anonymous-struct extension forbids non-POD members, and
  `mmio::memory` embeds a `std::atomic`). They are reached
  via `peripheral._lane_N.fooN` rather than `peripheral.fooN`.
- A register size other than 8, 16, 32 or 64 bits is an error.
- A cluster that cannot be a C++ class of the right size and alignment
  (elements every 6 bytes holding a 32-bit register) is dissolved into
  registers named `<cluster><index>_<register>`.
- `<modifiedWriteValues>` and `<readAction>` are not used: nothing
  distinguishes a write-1-to-clear flag from a plain one.
- The interrupts of a peripheral are in `interrupts.hpp` only; the
  peripheral headers do not say which are theirs.
- What the SVD names differently stays different: the STM32H563 file
  calls its DMA `GPDMA1`, the STM32H523 file `GPDMA`.

## Working on the generator

### Where things are

One file per step, in the order the data goes through them:

| File | Role |
|---|---|
| `SvdReader.cs` | XML to model, with everything the SVD leaves implicit resolved (`derivedFrom`, inherited properties, `dim`) |
| `Model.cs` | The model: peripherals, registers, clusters, fields, named values |
| `Structure.cs` | The shape of the API: shared types, names, prefixes removed, numbered blocks as arrays |
| `Emitter.cs` | Model to the text of the headers, in memory, with LF line endings |
| `OutputDirectory.cs` | Writing the directory and `--check`; only touches generated files |
| `Support/mmio.hpp` | The register building blocks: a real C++ file, embedded in the tool and written as it is next to the headers |
| `Naming.cs` | C++ identifiers, SVD numbers |
| `CMSIS-SVD_Schema_1_1.cs` | XML classes generated by `xsd`; not edited by hand |

`tests/cpp/fixture.svd` and `tests/cpp/semantics.cpp` show the generated
API by example.

### Rules

- **Nothing specific to a vendor or a peripheral in the logic.** The
  rules of `Structure.cs` are structural (same registers, common name
  prefix, numbering); GPIO, TIM or GPDMA only appear in comments, as
  examples. The one exception is the Nordic `read-writeonce` misspelling
  patched in `SvdReader.Sanitize`.
- **Nothing specific to a kernel either.** What a kernel has to plug in
  goes through the generic extension points (`vector_table::with(install)`,
  the `Decoration` parameter); the kernel carries the code that uses them.
- **A change of the output is a change of API for the users.** A renaming
  breaks their code: major version, and the README section updated.
- **Every behaviour has its test**: `tests/SvdGenerator.Tests` for what
  is emitted, `tests/cpp` for what the C++ accepts (`semantics.cpp`) and
  refuses (`must_not_compile.cpp`).
- A mistake of the user or an invalid SVD is an `SvdException` with a
  one-line message, never a bare exception.
- The generated code stays C++20, for GCC and Clang.

### Checking a change

```sh
dotnet build SvdGenerator.sln -c Release      # no warning: TreatWarningsAsErrors
dotnet format SvdGenerator.sln --verify-no-changes
dotnet test SvdGenerator.sln                  # the generator, on small SVD documents
tests/cpp/run.sh                              # the generated C++, on tests/cpp/fixture.svd
```

`tests/cpp/run.sh` generates the headers of a fixture device, compiles
`semantics.cpp` (static_asserts stating what the headers promise) and
checks that every case of `must_not_compile.cpp` is rejected. It takes
the compiler from `CXX`. Without a host compiler (Windows), use the ARM
one:

```sh
CXX=arm-none-eabi-g++ CXXFLAGS="-mcpu=cortex-m33 -mthumb" bash tests/cpp/run.sh
```

A change of the emitter is also worth running on real SVD files, compiling
every header as the `smoke` job of the CI does; the files it uses are
pinned in `.github/workflows/ci.yml`.

### Pitfalls

- The check on a constant that does not fit its field is only raised from
  `-Og`: a test with `-fsyntax-only` or `-O0` does not see it.
- The version written in the headers is `InformationalVersion` without
  its `+<commit>` suffix, so that two builds of one version produce the
  same bytes.
- On Windows, `arm-none-eabi-g++` is a native executable: give it
  `D:/...` paths, not the `/d/...` of Git Bash.

## Releases

Tag a commit with a SemVer tag and push it; the release workflow
publishes self-contained `win-x64`, `linux-x64`, and `osx-arm64`
binaries and the .NET tool package to a GitHub Release.

```sh
git tag v1.0.0
git push origin v1.0.0
```

The same workflow pushes the tool package to nuget.org, in a job of its
own that only waits for the package: the tool is one platform-independent
package and has nothing to wait for from the binaries. It publishes by
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
no API key is stored, nuget.org gives one that lives an hour to a run it
recognises. It takes two things, once:

- on nuget.org, a trusted publishing policy for repository owner
  `Otatiaro`, repository `SvdGenerator`, workflow file `release.yml`, no
  environment;
- in this repository, an Actions variable `NUGET_USER` holding the
  nuget.org profile name that owns the policy. Without it the job is
  skipped.

A version pushed to nuget.org cannot be replaced nor removed, only
unlisted.

Untagged local builds carry the version `0.0.0-dev`.

## License

The generator is under the [MIT](LICENSE) license. That license covers the
generator, not what it generates. In a generated directory:

- **The code the generator writes** (the classes, the operators, the
  layout, the vector table) carries no license of SvdGenerator: the
  generator puts none of its own on its output.
- **What comes from the SVD file** is not the generator's to license. The
  names and addresses are facts about the device; the descriptions are the
  words of the author of the SVD file, copied as they are. The generated
  files therefore reproduce the notice the SVD file carries, as an
  attribution: it says where that content comes from and under which terms
  its author published it, not that the file belongs to that author. Which
  license your headers end up under follows from the terms of your SVD
  file; read them.
- **`mmio.hpp`** is the one file that is not generated from the SVD: it is
  part of SvdGenerator, copied as it is, under the MIT license written in
  its header.
