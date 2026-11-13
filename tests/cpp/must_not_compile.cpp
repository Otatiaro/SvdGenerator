// Mistakes the generated headers must turn into compile errors.
// run.sh compiles this file once per CASE_x and expects each to be rejected with
// a message matching the regular expression after "expect:".

#include <FIXTURE/ctl.hpp>
#include <FIXTURE/dma.hpp>
#include <FIXTURE/port.hpp>

using namespace FIXTURE;
using mmio::atomic;

void cases()
{
#ifdef CASE_FIELD_OF_ANOTHER_REGISTER // expect: no match for .operator\|=
	ctl.cr |= ctl_p::sr_r::busy_f();
#endif
#ifdef CASE_WRITE_ONLY_REGISTER_READ // expect: has no member named .load
	(void)porta.bsrr.load();
#endif
#ifdef CASE_READ_ONLY_REGISTER_WRITTEN // expect: no match for .operator=
	dma1.isr = dma_p::isr_r(0);
#endif
#ifdef CASE_REGISTER_OF_READ_ONLY_FIELDS_WRITTEN // expect: no match for .operator=
	ctl.sr = ctl_p::sr_r(0);
#endif
#ifdef CASE_REGISTER_OF_WRITE_ONLY_FIELDS_READ // expect: has no member named .load
	(void)ctl.icr.load();
#endif
#ifdef CASE_READ_ONLY_FIELD_ASSIGNED // expect: no match for .operator=
	ctl.cr = ctl_p::cr_r::rdy_f();
#endif
#ifdef CASE_READ_ONLY_FIELD_SET // expect: no match for .operator\|=
	ctl.cr |= ctl_p::cr_r::rdy_f();
#endif
#ifdef CASE_READ_ONLY_FIELD_COMBINED // expect: use of deleted function .*operator\|
	ctl.cr = ctl_p::cr_r::en_f() | ctl_p::cr_r::rdy_f();
#endif
#ifdef CASE_READ_ONLY_FIELD_COMBINED_FIRST // expect: use of deleted function .*operator\|
	ctl.cr = ctl_p::cr_r::rdy_f() | ctl_p::cr_r::en_f();
#endif
#ifdef CASE_FIELDS_OF_TWO_REGISTERS_COMBINED // expect: use of deleted function .*operator\|
	ctl.cr = ctl_p::cr_r::en_f() | ctl_p::icr_r::cbusy_f();
#endif
#ifdef CASE_READ_ONLY_FIELD_CLEAR_SET // expect: no match for .operator<<=
	ctl.cr <<= ctl_p::cr_r::rdy_f(false);
#endif
#ifdef CASE_CONSTANT_OUT_OF_RANGE // expect: this constant does not fit the register field
	ctl.cr = ctl_p::cr_r::mode_f(8);
#endif
#ifdef CASE_CONSTANT_OUT_OF_RANGE_WIDE_FIELD // expect: this constant does not fit the register field
	ctl.cr <<= ctl_p::cr_r::div_f(600) || atomic;
#endif
#ifdef CASE_CONSTANT_OUT_OF_RANGE_IN_A_CONSTANT_EXPRESSION // expect: field_value_out_of_range
	constexpr ctl_p::cr_r value = ctl_p::cr_r::mode_f(8);
	(void)value;
#endif
#ifdef CASE_SINGLE_BAR_BETWEEN_FIELDS_OF_AN_ATOMIC_CLEAR_SET // expect: use of deleted function .*operator\|\|
	ctl.cr <<= (ctl_p::cr_r::mode_f(1) | ctl_p::cr_r::div_f(2)) || atomic;
#endif
}

// A register must be accessed in one instruction: a width the core cannot load
// and store atomically is refused. Depends on the target, hence ARM only.
struct wide_r
{
	constexpr wide_r(std::uint64_t value = 0) : value_(value) {}
	constexpr std::uint64_t value() const { return value_; }
	std::uint64_t value_;
};
#ifdef CASE_ARM_REGISTER_WIDER_THAN_THE_CORE_ACCESSES_ATOMICALLY // expect: TInt is not lock-free on this target
[[gnu::used]] mmio::memory<std::uint64_t, wide_r> g_wide;
#endif
