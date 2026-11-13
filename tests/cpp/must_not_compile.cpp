// Mistakes the generated headers must turn into compile errors.
// run.sh compiles this file once per CASE_x and expects each to be rejected with
// a message matching the regular expression after "expect:".

#include <FIXTURE/ctl.hpp>
#include <FIXTURE/dma.hpp>
#include <FIXTURE/port.hpp>

using namespace FIXTURE;
using opsy::utility::atomic;

void cases()
{
#ifdef CASE_FIELD_OF_ANOTHER_REGISTER // expect: no match for .operator\|=
	ctl.ctl_cr |= ctl_p::ctl_sr_r::busy_f();
#endif
#ifdef CASE_WRITE_ONLY_REGISTER_READ // expect: has no member named .load
	(void)porta.porta_bsrr.load();
#endif
#ifdef CASE_READ_ONLY_REGISTER_WRITTEN // expect: no match for .operator=
	dma1.dma_isr = dma_p::dma_isr_r(0);
#endif
}
