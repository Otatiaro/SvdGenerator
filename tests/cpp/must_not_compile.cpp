// Mistakes the generated headers must turn into compile errors.
// run.sh compiles this file once per CASE_x and expects each to be rejected with
// a message matching the regular expression after "expect:".

#include <FIXTURE/ctl.hpp>
#include <FIXTURE/dma.hpp>
#include <FIXTURE/port.hpp>
#include <FIXTURE/vectors.hpp>
#include <TWIN/interrupts.hpp>

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

// The vector table only takes the exceptions and the interrupts of its device.
void on_interrupt() {}
inline std::uint32_t stack_top;
#ifdef CASE_VECTOR_GIVEN_A_BARE_NUMBER // expect: no matching function for call to .*with<3, on_interrupt>
constexpr auto by_number = vector_table<>(&stack_top, on_interrupt).with<3, on_interrupt>();
#endif
#ifdef CASE_VECTOR_GIVEN_THE_INTERRUPT_OF_ANOTHER_DEVICE // expect: no matching function for call to .*with<
constexpr auto of_twin = vector_table<>(&stack_top, on_interrupt).with<TWIN::CTL_IRQn, on_interrupt>();
#endif
#ifdef CASE_VECTOR_EXCEPTION_INSTALLED_TWICE // expect: handler_already_installed
constexpr auto twice = vector_table<>(&stack_top, on_interrupt)
	.with<exception::hard_fault, on_interrupt>()
	.with<exception::hard_fault, on_interrupt>();
#endif
#ifdef CASE_VECTOR_INTERRUPT_INSTALLED_TWICE // expect: handler_already_installed
void on_default() {}
constexpr auto twice = vector_table<on_default>(&stack_top, on_interrupt)
	.with<CTL_IRQn, on_interrupt>()
	.with<CTL_IRQn, on_default>();
#endif
#ifdef CASE_VECTOR_INSTALLED_IN_PLACE_TWICE // expect: handler_already_installed
constexpr auto twice = []
{
	auto table = vector_table<>(&stack_top, on_interrupt);
	table.install<CTL_IRQn, on_interrupt>();
	table.install<CTL_IRQn, on_interrupt>();
	return table;
}();
#endif
#ifdef CASE_VECTOR_INSTALLED_IN_PLACE_IN_A_CONSTANT_TABLE // expect: discards qualifiers|no matching function
constexpr auto constant = vector_table<>(&stack_top, on_interrupt);
void change() { constant.reinstall<CTL_IRQn, on_interrupt>(); }
#endif
#ifdef CASE_VECTOR_INSTALLER_OVER_A_HANDLER_OF_THE_USER // expect: handler_already_installed
constexpr auto clash = vector_table<>(&stack_top, on_interrupt)
	.with<exception::svcall, on_interrupt>()
	.with([](auto table) { return table.template with<exception::svcall, on_interrupt>(); });
#endif
#ifdef CASE_VECTOR_INSTALLER_EXPECTING_AN_EXCEPTION_THE_TABLE_LACKS // expect: constraints not satisfied|no match
template<typename Table>
concept has_tick64 = requires(Table table) { table.template with<Table::exception_type::tick64, on_interrupt>(); };
constexpr auto lacking = vector_table<>(&stack_top, on_interrupt).with([](has_tick64 auto table) { return table; });
#endif

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
