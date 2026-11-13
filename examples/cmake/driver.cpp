// A driver built on headers that exist only in the build directory.

#include <FIXTURE/ctl.hpp>
#include <FIXTURE/port.hpp>
#include <FIXTURE/vectors.hpp>

using namespace FIXTURE;
using mmio::atomic;

void start()
{
	ctl.cr <<= ctl_p::cr_r::mode_f::fast || ctl_p::cr_r::en_f() || atomic;
	while (!ctl.cr.load().rdy()) {}
	porta.bsrr = port_p::bsrr_r::bs0_f();
}

void on_reset() {}
void on_ctl() {}
inline std::uint32_t stack_top;

constexpr auto vectors = vector_table<>(&stack_top, on_reset).with<CTL_IRQn, on_ctl>();
