// What the headers generated from fixture.svd promise, as static_asserts.
// Compiled by run.sh; nothing here runs.

#include <FIXTURE/bank.hpp>
#include <FIXTURE/ctl.hpp>
#include <FIXTURE/dma.hpp>
#include <FIXTURE/interrupts.hpp>
#include <FIXTURE/port.hpp>

#include <type_traits>

using namespace FIXTURE;

// ── Fields: position, mask, conversion to the register value ───────────────
using cr = ctl_p::ctl_cr_r;

static_assert(cr::en_f::offset == 0 && cr::en_f::width == 1 && cr::en_f::mask == 0x1);
static_assert(cr::mode_f::offset == 4 && cr::mode_f::width == 3 && cr::mode_f::mask == 0x70);
static_assert(cr::div_f::mask == 0x01FF0000u && cr::div_f::range == 0x1FF);
static_assert(cr::int_f::mask == 0x80000000u);

static_assert(cr(cr::en_f()).value() == 0x1);
static_assert(cr(cr::en_f(false)).value() == 0x0);
static_assert(cr(cr::mode_f(5)).value() == 0x50);
static_assert(cr(cr::div_f(0x1FF)).value() == 0x01FF0000u);
static_assert((cr::en_f() | cr::mode_f(1) | cr::div_f(2)).value() == 0x00020011u);

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
static_assert(std::is_same_v<decltype(ctl_p::ctl_small_r(0).value()), uint8_t>);
static_assert(ctl_p::ctl_small_r(ctl_p::ctl_small_r::nib_f(0xA)).value() == 0xA0);

// ── clear_set: what `reg <<= a || b` clears and sets ───────────────────────
constexpr opsy::utility::clear_set<cr> update = cr::mode_f(1) || cr::div_f(3);
static_assert(update.clear().value() == (0x70u | 0x01FF0000u));
static_assert(update.set().value() == (0x10u | 0x00030000u));

// ── Layout ─────────────────────────────────────────────────────────────────
static_assert(sizeof(ctl_p) == 0x18);
static_assert(offsetof(ctl_p, ctl_data) == 0x10);
static_assert(offsetof(ctl_p, ctl_small) == 0x14);
static_assert(offsetof(dma_p, dma_c2tr3) == 0x58);
static_assert(offsetof(bank_p, slot2_cnt) == 0x28);
static_assert(offsetof(bank_p, key3) == 0x4C);
static_assert(offsetof(bank_p, word) == 0x50);

// ── Register storage follows the register's access ─────────────────────────
static_assert(std::is_same_v<decltype(porta_p::porta_bsrr), opsy::utility::write_only_memory<uint32_t, porta_p::porta_bsrr_r>>);
static_assert(std::is_same_v<decltype(dma_p::dma_isr), opsy::utility::read_only_memory<uint32_t, dma_p::dma_isr_r>>);
static_assert(std::is_same_v<decltype(ctl_p::ctl_cr), opsy::utility::memory<uint32_t, cr>>);
// ... and, when the SVD states the access on the fields only, what they have in common.
static_assert(std::is_same_v<decltype(ctl_p::ctl_sr), opsy::utility::read_only_memory<uint32_t, ctl_p::ctl_sr_r>>);
static_assert(std::is_same_v<decltype(ctl_p::ctl_icr), opsy::utility::write_only_memory<uint32_t, ctl_p::ctl_icr_r>>);

// ── A read-only field is read, never written ───────────────────────────────
static_assert(!std::is_convertible_v<cr::rdy_f, cr>);
static_assert(!std::is_convertible_v<cr::rdy_f, opsy::utility::clear_set<cr>>);
static_assert(std::is_convertible_v<cr::en_f, cr>);
static_assert(cr(0x2).rdy());
static_assert(cr::rdy_f::mask == 0x2);

// ── A value in range is accepted, at the limit too ─────────────────────────
static_assert(cr(cr::mode_f(7)).value() == 0x70);
static_assert(cr(cr::div_f(511)).value() == 0x01FF0000u);

// ── Instances ──────────────────────────────────────────────────────────────
static_assert(std::is_same_v<decltype(porta), porta_p&>);
static_assert(std::is_same_v<decltype(portc), porta_p&>);

// ── Interrupts ─────────────────────────────────────────────────────────────
static_assert(irq_count == 9);
static_assert(CTL_IRQn == 2 && CTL_ERR_IRQn == 5 && DMA1_CH0_IRQn == 8);

// ── The statements a driver writes must compile ────────────────────────────
void driver()
{
	using opsy::utility::atomic;

	ctl.ctl_cr = cr::en_f() | cr::mode_f(1);
	ctl.ctl_cr |= cr::en_f();
	ctl.ctl_cr |= cr::en_f() | atomic;
	ctl.ctl_cr <<= cr::mode_f(2);
	ctl.ctl_cr <<= cr::mode_f(2) || cr::div_f(7);
	ctl.ctl_cr <<= cr::mode_f(2) || cr::div_f(7) || atomic;
	while (!ctl.ctl_cr.load().rdy()) {}
	if (ctl.ctl_cr.load().mode() != 2u) {}
	if ((ctl.ctl_cr.load() & cr::en_f()) == cr::en_f()) {}
	while (ctl.ctl_sr.load().busy()) {}
	ctl.ctl_icr = ctl_p::ctl_icr_r::cbusy_f();
	// A value only known at run time is masked to the field, not rejected.
	ctl.ctl_cr <<= cr::mode_f(static_cast<uint8_t>(ctl.ctl_sr.load().level()));
	porta.porta_bsrr = porta_p::porta_bsrr_r::bs0_f();
	dma1.dma_c1cr = dma_p::dma_cxcr_r::en_f() | dma_p::dma_cxcr_r::prio_f(3);
}
