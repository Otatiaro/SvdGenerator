// What the headers generated from fixture.svd promise, as static_asserts.
// Compiled by run.sh; nothing here runs.

#include <FIXTURE/bank.hpp>
#include <FIXTURE/ctl.hpp>
#include <FIXTURE/device.hpp>
#include <FIXTURE/dma.hpp>
#include <FIXTURE/interrupts.hpp>
#include <FIXTURE/port.hpp>

// A second device generated from the same registers, with its own copy of
// mmio.hpp: both must live in one translation unit.
#include <TWIN/ctl.hpp>
#include <TWIN/interrupts.hpp>

#include <string_view>
#include <type_traits>
#include <utility>

static_assert(!std::is_same_v<FIXTURE::ctl_p, TWIN::ctl_p>);
static_assert(std::is_same_v<decltype(FIXTURE::ctl_p::cr), mmio::memory<uint32_t, FIXTURE::ctl_p::cr_r>>);
static_assert(std::is_same_v<decltype(TWIN::ctl_p::cr), mmio::memory<uint32_t, TWIN::ctl_p::cr_r>>);
static_assert(FIXTURE::irq_count == TWIN::irq_count);

// ── The register building blocks, on a register type written by hand ───────
struct mock_r
{
	[[maybe_unused]] static constexpr uint32_t mask = 0xFFFFFFFFu;

	constexpr mock_r(uint32_t value = 0) : value_{value} {}
	constexpr uint32_t value() const { return value_; }
	constexpr operator uint32_t() const { return value_; }
	constexpr mock_r operator|(mock_r o) const { return mock_r{value_ | o.value_}; }
	constexpr mock_r operator&(mock_r o) const { return mock_r{value_ & o.value_}; }
	constexpr mock_r operator^(mock_r o) const { return mock_r{value_ ^ o.value_}; }
	constexpr mock_r operator~() const { return mock_r{~value_}; }

	uint32_t value_;
};

static_assert(sizeof(mmio::memory<uint32_t, mock_r>) == 4 && alignof(mmio::memory<uint32_t, mock_r>) == 4);
static_assert(sizeof(mmio::read_only_memory<uint32_t, mock_r>) == 4);
static_assert(sizeof(mmio::write_only_memory<uint32_t, mock_r>) == 4);
static_assert(sizeof(mmio::padding<7>) == 7 && alignof(mmio::padding<7>) == 1);
static_assert((mmio::clear_set<mock_r>{mock_r{0x0F}, mock_r{0xF0}} || mmio::clear_set<mock_r>{mock_r{0x100}, mock_r{0x200}}).clear().value() == 0x10F);

void building_blocks(mmio::memory<uint32_t, mock_r>& rw, mmio::read_only_memory<uint32_t, mock_r>& ro, mmio::write_only_memory<uint32_t, mock_r>& wo)
{
	using mmio::atomic;
	using mmio::clear_set;

	rw = mock_r{1};
	(void)rw.load();
	(void)static_cast<mock_r>(rw);

	rw |= mock_r{0x0F};
	rw &= mock_r{0xF0};
	rw ^= mock_r{0xFF};
	rw <<= clear_set<mock_r>{mock_r{0x0F}, mock_r{0xF0}};

	rw |= mock_r{0x0F} | atomic;
	rw &= mock_r{0xF0} | atomic;
	rw ^= mock_r{0xFF} | atomic;
	rw <<= clear_set<mock_r>{mock_r{0x0F}, mock_r{0xF0}} || atomic;

	(void)ro.load();
	wo = mock_r{0x42};
}

using namespace FIXTURE;

// ── Names: the peripheral's name is not repeated in its registers ──────────
// CTL_CR of peripheral CTL is ctl.cr, of class ctl_p::cr_r.
using cr = ctl_p::cr_r;

// ── Fields: position, mask, conversion to the register value ───────────────
static_assert(cr::en_f::offset == 0 && cr::en_f::width == 1 && cr::en_f::mask == 0x1);
static_assert(cr::mode_f::offset == 4 && cr::mode_f::width == 3 && cr::mode_f::mask == 0x70);
static_assert(cr::div_f::mask == 0x01FF0000u && cr::div_f::range == 0x1FF);
static_assert(cr::int_f::mask == 0x80000000u);

static_assert(cr(cr::en_f()).value() == 0x1);
static_assert(cr(cr::en_f(false)).value() == 0x0);
static_assert(cr(cr::mode_f(5)).value() == 0x50);
static_assert(cr(cr::div_f(0x1FF)).value() == 0x01FF0000u);
static_assert((cr::en_f() | cr::mode_f(1) | cr::div_f(2)).value() == 0x00020011u);

// ── Named values: objects of the field's type, used like any field ─────────
static_assert(cr(cr::mode_f::fast).value() == 0x50);
static_assert((cr::mode_f::slow | cr::en_f()).value() == 0x11);
static_assert(cr(0x50).mode() == cr::mode_f::fast);
static_assert(cr(0x10).mode() != cr::mode_f::fast);
static_assert(std::is_same_v<decltype(cr::mode_f::off), const cr::mode_f>);
constexpr mmio::clear_set<cr> to_fast = cr::mode_f::fast || cr::en_f();
static_assert(to_fast.clear().value() == 0x71 && to_fast.set().value() == 0x51);

// ── Registers: accessors, operators, reset value ───────────────────────────
static_assert(cr(0x50).mode() == 5);
static_assert(cr(0x01FF0000u).div() == 0x1FF);
static_assert(cr(0x1).en());
static_assert(!cr(0x2).en());
static_assert(cr(0x80000000u)._int());
static_assert((~cr(0)).value() == 0xFFFFFFFFu);
static_assert((cr(0x51) & cr::mode_f(7)).value() == 0x50);
static_assert((cr(0x51) ^ cr(0x01)).value() == 0x50);
static_assert(cr(0x51) == cr(0x51) && cr(0x51) != cr(0x50));
static_assert((cr(0x51) & cr::en_f()) == cr::en_f());
static_assert(cr::reset_value == 0x10);
static_assert(std::is_same_v<decltype(cr(0).value()), uint32_t>);
static_assert(std::is_same_v<decltype(ctl_p::small_r(0).value()), uint8_t>);
static_assert(ctl_p::small_r(ctl_p::small_r::nib_f(0xA)).value() == 0xA0);

// ── clear_set: what `reg <<= a || b` clears and sets ───────────────────────
constexpr mmio::clear_set<cr> update = cr::mode_f(1) || cr::div_f(3);
static_assert(update.clear().value() == (0x70u | 0x01FF0000u));
static_assert(update.set().value() == (0x10u | 0x00030000u));

// ── Layout ─────────────────────────────────────────────────────────────────
static_assert(sizeof(ctl_p) == 0x18);
static_assert(offsetof(ctl_p, data) == 0x10);
static_assert(offsetof(ctl_p, small) == 0x14);

// ── Register storage follows the register's access ─────────────────────────
static_assert(std::is_same_v<decltype(port_p::bsrr), mmio::write_only_memory<uint32_t, port_p::bsrr_r>>);
static_assert(std::is_same_v<decltype(dma_p::isr), mmio::read_only_memory<uint32_t, dma_p::isr_r>>);
static_assert(std::is_same_v<decltype(ctl_p::cr), mmio::memory<uint32_t, cr>>);
// ... and, when the SVD states the access on the fields only, what they have in common.
static_assert(std::is_same_v<decltype(ctl_p::sr), mmio::read_only_memory<uint32_t, ctl_p::sr_r>>);
static_assert(std::is_same_v<decltype(ctl_p::icr), mmio::write_only_memory<uint32_t, ctl_p::icr_r>>);

// ── A read-only field is read, never written ───────────────────────────────
static_assert(!std::is_convertible_v<cr::rdy_f, cr>);
static_assert(!std::is_convertible_v<cr::rdy_f, mmio::clear_set<cr>>);
static_assert(std::is_convertible_v<cr::en_f, cr>);
static_assert(cr(0x2).rdy());
static_assert(cr::rdy_f::mask == 0x2);

// ── A value in range is accepted, at the limit too ─────────────────────────
static_assert(cr(cr::mode_f(7)).value() == 0x70);
static_assert(cr(cr::div_f(511)).value() == 0x01FF0000u);

// ── One type for peripherals with the same registers ───────────────────────
// PORTA and PORTB are described separately, under PORTA_ and PORTB_ names and
// with different reset values; PORTC derives from PORTA.
static_assert(std::is_same_v<decltype(porta), port_p&>);
static_assert(std::is_same_v<decltype(portb), port_p&>);
static_assert(std::is_same_v<decltype(portc), port_p&>);
// The reset value all ports share stays on the register class, the others are per port.
static_assert(port_p::bsrr_r::reset_value == 0);
static_assert(porta_reset::moder == 0xABFFFFFFu);
static_assert(portb_reset::moder == 0xFFFFFEBFu);
static_assert(portc_reset::moder == 0xABFFFFFFu);

// ── Numbered registers as an array ─────────────────────────────────────────
// DMA_C0CR, DMA_C0SAR, DMA_C1CR, ... are dma1.c[n].cr and dma1.c[n].sar.
static_assert(std::is_same_v<decltype(dma_p::c), dma_p::c_c[3]>);
static_assert(sizeof(dma_p::c_c) == 0x20);
static_assert(offsetof(dma_p, c) == 0x10);
static_assert(offsetof(dma_p::c_c, cr) == 0x0);
static_assert(offsetof(dma_p::c_c, sar) == 0x4);
// What only the last channel has is given to all, as in the vendor's C headers.
static_assert(offsetof(dma_p::c_c, tr3) == 0x8);
static_assert(dma_p::c_c::cr_r::twod_f::mask == 0x100);
static_assert(sizeof(dma_p) == 0x10 + 3 * 0x20);

// ── SVD clusters and register arrays ───────────────────────────────────────
static_assert(std::is_same_v<decltype(bank_p::slot), bank_p::slot_c[3]>);
static_assert(sizeof(bank_p::slot_c) == 0x10);
static_assert(offsetof(bank_p::slot_c, cnt) == 0x8);
static_assert(std::is_same_v<decltype(bank_p::slot_c::cnt), mmio::memory<uint16_t, bank_p::slot_c::cnt_r>>);
static_assert(std::is_same_v<decltype(bank_p::key), mmio::memory<uint32_t, bank_p::key_r>[4]>);
static_assert(offsetof(bank_p, key) == 0x40);
// Registers that overlap: the widest one directly, the others in a lane.
static_assert(offsetof(bank_p, word) == 0x50);
static_assert(offsetof(bank_p, _lane_0.byte1) == 0x51);

// ── Interrupts and device description, in the device namespace ─────────────
static_assert(FIXTURE::irq_count == 9);
static_assert(FIXTURE::CTL_IRQn == 2 && FIXTURE::CTL_ERR_IRQn == 5 && FIXTURE::DMA1_CH0_IRQn == 8);
static_assert(std::is_same_v<std::underlying_type_t<FIXTURE::IRQn_Type>, std::uint16_t>);
static_assert(std::string_view(FIXTURE::device_name) == "FIXTURE");
static_assert(std::string_view(FIXTURE::cpu_name) == "CM33");
static_assert(FIXTURE::nvic_priority_bits == 3);
static_assert(FIXTURE::has_mpu && !FIXTURE::has_fpu);

// ── Vector table ───────────────────────────────────────────────────────────
#include <FIXTURE/vectors.hpp>

void on_reset() {}
void on_fault() {}
void on_ctl() {}
void on_dma() {}
void on_svcall() {}
void on_pendsv() {}
void on_systick() {}
inline std::uint32_t stack_top;

constexpr auto vectors = vector_table<>(&stack_top, on_reset)
	.with<exception::hard_fault, on_fault>()
	.with<exception::secure_fault, on_fault>()
	.with<CTL_IRQn, on_ctl>()
	.with<DMA1_CH0_IRQn, on_dma>();

// The table is what the core reads: stack pointer, exceptions 1 to 15, then the interrupts.
static_assert(sizeof(vectors) == (16 + irq_count) * sizeof(void*));
static_assert(std::is_standard_layout_v<vector_table<>>);
static_assert(vectors.handler(1) == on_reset);
static_assert(vectors.handler(3) == on_fault && vectors.handler(7) == on_fault);
static_assert(vectors.handler(16 + 2) == on_ctl && vectors.handler(16 + 8) == on_dma);
// What was given no handler is null.
static_assert(vectors.handler(2) == nullptr && vectors.handler(16) == nullptr);
// The fixture is a CM33: it has the exceptions of Armv8-M Mainline.
static_assert(static_cast<int>(exception::secure_fault) == 7 && static_cast<int>(exception::systick) == 15);

// A handler for the unhandled, a parameter of the table, fills what with<>() does not.
void on_unhandled() {}

constexpr auto guarded_vectors = vector_table<on_unhandled>(&stack_top, on_reset)
	.with<exception::hard_fault, on_fault>()
	.with<CTL_IRQn, on_ctl>();
static_assert(guarded_vectors.handler(1) == on_reset);
static_assert(guarded_vectors.handler(3) == on_fault && guarded_vectors.handler(16 + 2) == on_ctl);
// System exceptions and device interrupts left alone, numbers no interrupt has included.
static_assert(guarded_vectors.handler(2) == on_unhandled && guarded_vectors.handler(15) == on_unhandled);
static_assert(guarded_vectors.handler(16 + 0) == on_unhandled && guarded_vectors.handler(16 + 8) == on_unhandled);
// The slots the core reserves stay null (8, 9, 10 and 13 on a CM33).
static_assert(guarded_vectors.handler(8) == nullptr && guarded_vectors.handler(9) == nullptr);
static_assert(guarded_vectors.handler(10) == nullptr && guarded_vectors.handler(13) == nullptr);

// A decoration wraps the handlers of the device interrupts, and only those.
template<isr_handler_t Handler>
void traced() { Handler(); }

struct tracing
{
	template<isr_handler_t Handler>
	static constexpr isr_handler_t wrap = traced<Handler>;
};

constexpr auto traced_vectors = vector_table<nullptr, tracing>(&stack_top, on_reset)
	.with<exception::hard_fault, on_fault>()
	.with<CTL_IRQn, on_ctl>();
static_assert(traced_vectors.handler(16 + 2) == traced<on_ctl>);
static_assert(traced_vectors.handler(3) == on_fault);
// The handler for the unhandled is not decorated.
static_assert(vector_table<on_unhandled, tracing>(&stack_top, on_reset).handler(16 + 5) == on_unhandled);

// with<>() only fills a slot that has no handler yet; replace<>() says that
// taking the place of another handler is meant.
constexpr auto replaced_vectors = guarded_vectors
	.replace<exception::hard_fault, on_systick>()
	.replace<CTL_IRQn, on_dma>()
	.replace<exception::nmi, on_fault>();   // nothing was there: replace does not require it
static_assert(replaced_vectors.handler(3) == on_systick && replaced_vectors.handler(16 + 2) == on_dma);
static_assert(replaced_vectors.handler(2) == on_fault);
// The original table is a value: it is unchanged.
static_assert(guarded_vectors.handler(3) == on_fault);
// replace<>() goes through the decoration like with<>().
static_assert(traced_vectors.replace<CTL_IRQn, on_dma>().handler(16 + 2) == traced<on_dma>);
// In place, for a table in RAM: install<>() and reinstall<>() follow the same rule.
constexpr auto in_place_vectors = []
{
	auto table = guarded_vectors;
	table.install<exception::nmi, on_svcall>();
	table.install<DMA1_CH0_IRQn, on_dma>();
	table.reinstall<exception::hard_fault, on_systick>();
	table.reinstall<CTL_IRQn, on_dma>();
	return table;
}();
static_assert(in_place_vectors.handler(2) == on_svcall && in_place_vectors.handler(16 + 8) == on_dma);
static_assert(in_place_vectors.handler(3) == on_systick && in_place_vectors.handler(16 + 2) == on_dma);

// On a table in RAM the mistake cannot stop the build: the program says what
// happens, by defining the function the generated header only declares.
// It is given what was already taken, as the enumerator it is.
[[noreturn]] void FIXTURE::handler_already_installed(IRQn_Type taken)
{
	static_cast<void>(taken); // a real program reports it: its assert, its fault handler
	while (true) {}
}

[[noreturn]] void FIXTURE::handler_already_installed(exception taken)
{
	static_cast<void>(taken);
	while (true) {}
}

void patch_at_run_time(vector_table<on_unhandled>& ram_vectors)
{
	ram_vectors.install<CTL_IRQn, on_ctl>();
	ram_vectors.install<exception::svcall, on_svcall>();
	ram_vectors.reinstall<CTL_IRQn, on_dma>();
}

// What the in-place functions return is nothing: a forgotten assignment cannot hide a change.
static_assert(std::is_void_v<decltype(std::declval<vector_table<>&>().install<CTL_IRQn, on_ctl>())>);

// Installing the handler for the unhandled itself leaves the slot free.
static_assert(guarded_vectors.with<exception::nmi, on_unhandled>().with<exception::nmi, on_fault>().handler(2) == on_fault);

// A library installs its own handlers without the generated code knowing it,
// and without knowing the type of the table: it states what it expects of it.
namespace kernel
{
template<typename Table>
concept vector_table = requires(Table table)
{
	table.template with<Table::exception_type::svcall, on_svcall>();
	table.template with<Table::exception_type::pendsv, on_pendsv>();
	table.template with<Table::exception_type::systick, on_systick>();
};

inline constexpr auto scheduler_isrs = [](vector_table auto table)
{
	using e = typename decltype(table)::exception_type;
	return table.template with<e::svcall, on_svcall>()
	            .template with<e::pendsv, on_pendsv>()
	            .template with<e::systick, on_systick>();
};
} // namespace kernel

constexpr auto kernel_vectors = vector_table<nullptr, tracing>(&stack_top, on_reset)
	.with(kernel::scheduler_isrs)
	.with<CTL_IRQn, on_ctl>();
static_assert(kernel_vectors.handler(11) == on_svcall && kernel_vectors.handler(14) == on_pendsv && kernel_vectors.handler(15) == on_systick);
static_assert(kernel_vectors.handler(16 + 2) == traced<on_ctl>);
static_assert(std::is_same_v<decltype(kernel_vectors), const vector_table<nullptr, tracing>>);

// ── The statements a driver writes must compile ────────────────────────────
template<typename TPort>
void make_output(TPort& port)
{
	using mmio::atomic;
	port.moder <<= port_p::moder_r::mode0_f(1) || atomic;
}

void take_irq(std::uint32_t) {}

void driver()
{
	using mmio::atomic;

	ctl.cr = cr::en_f() | cr::mode_f(1);
	ctl.cr = cr::en_f() | cr::mode_f::fast;
	ctl.cr |= cr::en_f();
	ctl.cr |= cr::en_f() | atomic;
	ctl.cr <<= cr::mode_f(2);
	ctl.cr <<= cr::mode_f::slow || cr::div_f(7);
	ctl.cr <<= cr::mode_f(2) || cr::div_f(7) || atomic;
	while (!ctl.cr.load().rdy()) {}
	if (ctl.cr.load().mode() != 2u) {}
	if (ctl.cr.load().mode() == cr::mode_f::fast) {}
	if ((ctl.cr.load() & cr::en_f()) == cr::en_f()) {}
	while (ctl.sr.load().busy()) {}
	ctl.icr = ctl_p::icr_r::cbusy_f();
	// A value only known at run time is masked to the field, not rejected.
	ctl.cr <<= cr::mode_f(static_cast<uint8_t>(ctl.sr.load().level()));

	// One function for every port.
	make_output(porta);
	make_output(portb);
	porta.bsrr = port_p::bsrr_r::bs0_f();

	// One loop for every channel.
	using ch = dma_p::c_c;
	for (auto& channel : dma1.c)
		channel.cr = ch::cr_r::en_f() | ch::cr_r::prio_f(3);
	dma1.c[1].sar = ch::sar_r::sa_f(0x20000000u);

	bank.slot[2].cfg |= bank_p::slot_c::cfg_r::on_f();
	bank.key[3] = bank_p::key_r::k_f(0xCAFEu);

	take_irq(DMA1_CH0_IRQn);
}
