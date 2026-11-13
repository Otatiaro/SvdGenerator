using System;
using System.Linq;
using Xunit;
using static SvdGenerator.Tests.Svd;

namespace SvdGenerator.Tests
{
    public class ReaderTests
    {
        [Fact]
        public void A_device_without_interrupts_generates()
        {
            var files = Generate(Device(Peripheral("P", 0x1000, SimpleRegister())));

            Assert.Contains("irq_count = 0;", files["interrupts.hpp"]);
        }

        [Fact]
        public void A_chain_of_derived_peripherals_keeps_every_instance()
        {
            var header = Generate(Device(
                Peripheral("A", 0x1000, SimpleRegister()) +
                Peripheral("B", 0x2000, derivedFrom: "A") +
                Peripheral("C", 0x3000, derivedFrom: "B")))["a.hpp"];

            Assert.Contains("inline a_p& a = *reinterpret_cast<a_p*>(0x1000);", header);
            Assert.Contains("inline a_p& b = *reinterpret_cast<a_p*>(0x2000);", header);
            Assert.Contains("inline a_p& c = *reinterpret_cast<a_p*>(0x3000);", header);
        }

        [Fact]
        public void A_derived_peripheral_naming_nothing_is_an_error()
        {
            var e = Assert.Throws<SvdException>(() => Generate(Device(Peripheral("B", 0x2000, derivedFrom: "NOPE"))));

            Assert.Contains("derivedFrom 'NOPE'", e.Message);
        }

        [Fact]
        public void Register_families_with_the_same_pattern_get_distinct_classes()
        {
            // CH0A/CH1A, CH2A/CH3A and CH4A/CH5A are three families (their field
            // widths differ) that all reduce to "chxa": this used to never return.
            var registers = string.Concat(Enumerable.Range(0, 6).Select(i => Register($"CH{i}A", 4 * i, Field("F", 0, 1 + i / 2))));

            var header = GenerateOne(Peripheral("P", 0x1000, registers));

            Assert.Contains("class chxa_r {", header);
            Assert.Contains("class chxxa_r {", header);
            Assert.Contains("class chxxxa_r {", header);
        }

        [Fact]
        public void A_register_takes_its_size_from_the_peripheral()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Register("R0", 0, Field("F", 0, 1), size: null) + Register("R1", 1, Field("G", 0, 1), size: null),
                extra: "<size>8</size>"));

            Assert.Equal(new[] { "mmio::memory<uint8_t,r0_r> r0;", "mmio::memory<uint8_t,r1_r> r1;" }, Members(header));
        }

        [Fact]
        public void A_register_takes_its_size_from_the_device()
        {
            var header = Generate(Device(Peripheral("P", 0x1000, Register("R0", 0, Field("F", 0, 1), size: null)), extra: "<size>16</size>"))["p.hpp"];

            Assert.Equal(new[] { "mmio::memory<uint16_t,r0_r> r0;" }, Members(header));
        }

        [Fact]
        public void A_register_takes_its_access_from_the_peripheral()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, SimpleRegister(), extra: "<access>read-only</access>"));

            Assert.Equal(new[] { "mmio::read_only_memory<uint32_t,r_r> r;" }, Members(header));
        }

        [Fact]
        public void A_cluster_is_a_nested_class()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                SimpleRegister() + Cluster("CL", 0x10, Register("X", 0, Field("A", 0, 2)) + Register("Y", 4, Field("B", 0, 3)))));

            Assert.Contains("class cl_c {", header);
            Assert.Contains("\tcl_c cl;\n", header);
            Assert.Contains("static_assert(offsetof(p_p, cl) == 16);", header);
            Assert.Contains("static_assert(offsetof(p_p::cl_c, y) == 4);", header);
            Assert.Contains("mmio::memory<uint32_t,y_r> y;", header);
        }

        [Fact]
        public void A_cluster_array_is_an_array_of_its_class()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Cluster("CH[%s]", 0x20, SimpleRegister("X"), extra: "<dim>2</dim><dimIncrement>8</dimIncrement>")));

            Assert.Contains("\tch_c ch[2];\n", header);
            Assert.Contains("static_assert(offsetof(p_p, ch) == 32);", header);
            Assert.Contains("static_assert(sizeof(p_p::ch_c) == 8);", header);
            Assert.Contains("\tmmio::padding<4> _p4;\n", header);
        }

        [Fact]
        public void A_register_array_is_a_list_or_an_array_depending_on_its_name_form()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Register("A%s", 0, Field("F", 0, 1), extra: "<dim>2</dim><dimIncrement>4</dimIncrement><dimIndex>X,Y</dimIndex>") +
                Register("B[%s]", 8, Field("F", 0, 2), extra: "<dim>2</dim><dimIncrement>4</dimIncrement>")));

            Assert.Contains("static_assert(offsetof(p_p, ax) == 0);", header);
            Assert.Contains("static_assert(offsetof(p_p, ay) == 4);", header);
            Assert.Contains("mmio::memory<uint32_t,b_r> b[2];", header);
            Assert.Contains("static_assert(offsetof(p_p, b) == 8);", header);
        }

        [Fact]
        public void A_peripheral_array_gives_one_instance_per_index()
        {
            var header = Generate(Device(Peripheral("P%s", 0x1000, SimpleRegister(), extra: "<dim>2</dim><dimIncrement>0x400</dimIncrement>")))["p.hpp"];

            Assert.Contains("inline p_p& p0 = *reinterpret_cast<p_p*>(0x1000);", header);
            Assert.Contains("inline p_p& p1 = *reinterpret_cast<p_p*>(0x1400);", header);
        }

        [Fact]
        public void A_derived_register_inherits_fields_and_size()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Register("A", 0, Field("F", 0, 3), size: "8") +
                Register("B", 1, size: null, derivedFrom: "A")));

            Assert.Contains("mmio::memory<uint8_t,x_r> b;", header);
        }

        [Fact]
        public void A_derived_register_naming_nothing_is_an_error()
        {
            var e = Assert.Throws<SvdException>(() => GenerateOne(Peripheral("P", 0x1000, Register("B", 0, derivedFrom: "NOPE"))));

            Assert.Contains("register P.B", e.Message);
        }

        [Fact]
        public void Bit_positions_are_read_in_their_three_forms()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0,
                Field("A", 0, 2) +
                "<field><name>B</name><bitRange>[7:5]</bitRange></field>" +
                "<field><name>C</name><lsb>9</lsb><msb>9</msb></field>")));

            Assert.Contains("class b_f {\npublic:\n\tstatic constexpr std::size_t offset = 5;\n\tstatic constexpr std::size_t width = 3;", header);
            Assert.Contains("class c_f {\npublic:\n\tstatic constexpr std::size_t offset = 9;\n\tstatic constexpr std::size_t width = 1;", header);
        }

        [Fact]
        public void A_field_array_expands()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0,
                Field("PIN%s", 4, 2, extra: "<dim>3</dim><dimIncrement>2</dimIncrement>"))));

            Assert.Contains("class pin2_f {\npublic:\n\tstatic constexpr std::size_t offset = 8;", header);
        }

        [Theory]
        [InlineData("24", "a size of 24 bits is not supported")]
        [InlineData("zz", "'zz' is not a number")]
        public void An_unusable_register_size_is_reported(string size, string message)
        {
            var e = Assert.Throws<SvdException>(() => GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("F", 0, 1), size: size))));

            Assert.Contains("register P.R", e.Message);
            Assert.Contains(message, e.Message);
        }

        [Fact]
        public void A_field_that_does_not_fit_is_reported()
        {
            var e = Assert.Throws<SvdException>(() => GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("F", 30, 4)))));

            Assert.Contains("field F of register P.R", e.Message);
        }

        [Fact]
        public void A_file_that_is_not_xml_is_reported_without_a_stack_trace()
        {
            var e = Assert.Throws<SvdException>(() => Generate("<device><name>"));

            Assert.StartsWith("Can't read test.svd", e.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Nordic_access_misspelling_is_accepted()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("F", 0, 1), extra: "<access>read-writeonce</access>")));

            Assert.Contains("mmio::memory<uint32_t,r_r> r;", header);
        }

        [Theory]
        [InlineData("0x1F", 31)]
        [InlineData("0XFFFFFFFF", 4294967295)]
        [InlineData("#101", 5)]
        [InlineData("42", 42)]
        public void Numbers_are_read_in_every_svd_form(string text, long expected)
        {
            Assert.Equal(expected, Naming.Number(text, "test"));
        }
    }
}
