using System;
using System.Linq;
using Xunit;
using static SvdGenerator.Tests.Svd;

namespace SvdGenerator.Tests
{
    /// <summary>
    /// What the generated code refuses. The refusals themselves are checked by a
    /// compiler in tests/cpp; these tests pin down what is emitted.
    /// </summary>
    public class SafetyTests
    {
        const string ReadOnly = "<access>read-only</access>";
        const string WriteOnly = "<access>write-only</access>";
        const string ReadWrite = "<access>read-write</access>";

        static string ClassBody(string header, string className)
        {
            var start = header.IndexOf($"class {className} {{", StringComparison.Ordinal);
            Assert.True(start >= 0, $"class {className} not found");
            return header[start..header.IndexOf("\n};", start, StringComparison.Ordinal)];
        }

        [Theory]
        [InlineData(ReadOnly, ReadOnly, "read_only_memory")]
        [InlineData(WriteOnly, WriteOnly, "write_only_memory")]
        [InlineData(ReadOnly, WriteOnly, "memory")]
        [InlineData(ReadOnly, ReadWrite, "memory")]
        [InlineData("", "", "memory")]
        public void A_register_is_stored_with_the_access_its_fields_have_in_common(string first, string second, string storage)
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("A", 0, 1, first) + Field("B", 1, 1, second))));

            Assert.Equal(new[] { $"mmio::{storage}<uint32_t,r_r> r;" }, Members(header));
        }

        [Fact]
        public void The_access_of_the_fields_wins_over_the_access_of_the_register()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("A", 0, 1, ReadOnly), extra: ReadWrite)));

            Assert.Equal(new[] { "mmio::read_only_memory<uint32_t,r_r> r;" }, Members(header));
        }

        [Fact]
        public void A_field_without_access_takes_the_access_of_the_register()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("A", 0, 1), extra: ReadOnly)));

            Assert.DoesNotContain("operator r_r()", ClassBody(header, "a_f"));
        }

        [Fact]
        public void A_read_only_field_has_no_conversion_to_the_register()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("RO", 0, 2, ReadOnly) + Field("RW", 2, 2, ReadWrite) + Field("WO", 4, 2, WriteOnly))));

            var readOnly = ClassBody(header, "ro_f");
            Assert.DoesNotContain("operator r_r()", readOnly);
            Assert.DoesNotContain("clear_set", readOnly);
            Assert.Contains("constexpr operator uint8_t() const", readOnly);
            Assert.Contains("static constexpr uint8_t mask", readOnly);

            Assert.Contains("operator r_r()", ClassBody(header, "rw_f"));
            Assert.Contains("operator r_r()", ClassBody(header, "wo_f"));
            Assert.Contains("constexpr auto ro() const -> ro_f", header);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(3, true)]
        [InlineData(8, false)]
        [InlineData(9, true)]
        [InlineData(16, false)]
        [InlineData(32, false)]
        public void Only_a_field_narrower_than_its_type_checks_its_value(int width, bool checks)
        {
            var body = ClassBody(GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("F", 0, width)))), "f_f");

            Assert.Equal(checks, body.Contains("if (__builtin_constant_p(value) && value > range) mmio::detail::field_value_out_of_range();"));
            Assert.Equal(checks, body.Contains("value_(value & range)"));
        }

        [Fact]
        public void The_check_is_declared_with_the_register_building_blocks()
        {
            var files = Generate(Device(Peripheral("P", 0x1000, SimpleRegister())));

            Assert.Equal(1, Lines(files[Emitter.RegistersFile]).Count(l => l == "void field_value_out_of_range();"));
            Assert.DoesNotContain("void field_value_out_of_range();", files["p.hpp"]);
        }

        [Fact]
        public void A_register_value_compares_and_masks()
        {
            var body = GenerateOne(Peripheral("P", 0x1000, SimpleRegister()));

            Assert.Contains("constexpr auto operator &(r_r other) const -> r_r", body);
            Assert.Contains("constexpr auto operator ^(r_r other) const -> r_r", body);
            Assert.Contains("constexpr bool operator ==(const r_r&) const = default;", body);
            Assert.Contains("auto operator ||(mmio::atomic_tag) const = delete;", body);
        }
    }
}
