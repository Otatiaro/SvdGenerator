using System;
using System.Linq;
using Xunit;
using static SvdGenerator.Tests.Svd;

namespace SvdGenerator.Tests
{
    /// <summary>
    /// The shape of the generated API: shared types, names, arrays, named values.
    /// </summary>
    public class StructureTests
    {
        static string Group(string name) => $"<groupName>{name}</groupName>";

        static string Reset(string value) => $"<resetValue>{value}</resetValue>";

        static string[] Classes(string header, string suffix) =>
            Lines(header).Where(l => l.StartsWith("class ", StringComparison.Ordinal) && l.EndsWith($"{suffix} {{", StringComparison.Ordinal))
                .Select(l => l[6..^2]).ToArray();

        static string[] Instances(string header) =>
            Lines(header).Where(l => l.StartsWith("inline ", StringComparison.Ordinal) && l.Contains("reinterpret_cast"))
                .Select(l => l[7..l.IndexOf(" = ", StringComparison.Ordinal)]).ToArray();

        // ── Register names ─────────────────────────────────────────────────

        [Theory]
        [InlineData("RCC", "", "RCC_CR", "cr")]
        [InlineData("TIM1", "", "TIM1_CR", "cr")]
        [InlineData("GPDMA1", "", "GPDMA_CR", "cr")]
        [InlineData("GPIOC", "GPIO", "GPIO_CR", "cr")]
        [InlineData("RCC", "", "CR", "cr")]
        [InlineData("RCC", "", "PWR_CR", "pwr_cr")]
        [InlineData("RCC", "", "RCC_1X", "rcc_1x")]
        public void The_name_of_the_peripheral_is_removed_from_its_registers(string peripheral, string group, string register, string member)
        {
            var files = Generate(Device(Peripheral(peripheral, 0x1000, SimpleRegister(register), extra: group.Length == 0 ? "" : Group(group))));

            var header = files.Single(f => f.Key != "device.hpp" && f.Key != "interrupts.hpp" && f.Key != Emitter.RegistersFile).Value;
            Assert.Contains($"> {member};\n", header);
            Assert.Contains($"class {member}_r {{", header);
        }

        [Fact]
        public void A_prefix_stays_when_removing_it_would_give_two_registers_one_name()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("P_CR", 0, Field("A", 0, 1)) + Register("CR", 4, Field("B", 0, 2))));

            Assert.Contains("> p_cr;\n", header);
            Assert.Contains("> cr;\n", header);
        }

        // ── One type for peripherals with the same registers ───────────────

        [Fact]
        public void Peripherals_described_separately_with_the_same_registers_share_a_type()
        {
            var files = Generate(Device(
                Peripheral("GPIOA", 0x1000, Register("GPIOA_MODER", 0, Field("MODE0", 0, 2))) +
                Peripheral("GPIOB", 0x2000, Register("GPIOB_MODER", 0, Field("MODE0", 0, 2))) +
                Peripheral("GPIOC", 0x3000, derivedFrom: "GPIOB")));

            Assert.Equal(new[] { "device.hpp", "gpio.hpp", "interrupts.hpp", "mmio.hpp" }, files.Keys);
            Assert.Equal(new[] { "gpio_p" }, Classes(files["gpio.hpp"], "_p"));
            Assert.Equal(new[] { "gpio_p& gpioa", "gpio_p& gpiob", "gpio_p& gpioc" }, Instances(files["gpio.hpp"]));
        }

        [Fact]
        public void A_difference_in_the_registers_makes_two_types()
        {
            var header = Generate(Device(
                Peripheral("GPIOA", 0x1000, Register("MODER", 0, Field("MODE0", 0, 2)), extra: Group("GPIO")) +
                Peripheral("GPIOB", 0x2000, Register("MODER", 0, Field("MODE0", 0, 3)), extra: Group("GPIO"))))["gpio.hpp"];

            Assert.Equal(new[] { "gpioa_p", "gpiob_p" }, Classes(header, "_p"));
        }

        [Fact]
        public void Unrelated_peripherals_that_look_alike_keep_their_own_type()
        {
            var files = Generate(Device(
                Peripheral("CRC", 0x1000, Register("CR", 0, Field("EN", 0, 1))) +
                Peripheral("RNG", 0x2000, Register("CR", 0, Field("EN", 0, 1)))));

            Assert.Equal(new[] { "crc.hpp", "device.hpp", "interrupts.hpp", "mmio.hpp", "rng.hpp" }, files.Keys);
        }

        [Fact]
        public void A_group_makes_peripherals_with_different_names_related()
        {
            var header = Generate(Device(
                Peripheral("USART1", 0x1000, SimpleRegister("CR"), extra: Group("USART")) +
                Peripheral("UART4", 0x2000, SimpleRegister("CR"), extra: Group("USART"))))["usart.hpp"];

            Assert.Equal(new[] { "usart_p" }, Classes(header, "_p"));
            Assert.Equal(new[] { "usart_p& uart4", "usart_p& usart1" }, Instances(header));
        }

        [Fact]
        public void Types_that_would_get_the_same_name_keep_the_name_of_their_first_peripheral()
        {
            // TIM1 and TIM8 share a type, TIM2 and TIM5 another: both would be "tim".
            string Timer(string name, long address, int width) => Peripheral(name, address, Register($"{name}_CNT", 0, Field("CNT", 0, width)));

            var files = Generate(Device(Timer("TIM1", 0x1000, 16) + Timer("TIM2", 0x2000, 32) + Timer("TIM5", 0x5000, 32) + Timer("TIM8", 0x8000, 16)));

            Assert.Equal(new[] { "device.hpp", "interrupts.hpp", "mmio.hpp", "tim.hpp" }, files.Keys);
            Assert.Equal(new[] { "tim1_p", "tim2_p" }, Classes(files["tim.hpp"], "_p"));
            Assert.Equal(new[] { "tim1_p& tim1", "tim1_p& tim8", "tim2_p& tim2", "tim2_p& tim5" }, Instances(files["tim.hpp"]));
        }

        [Fact]
        public void A_lone_peripheral_named_like_the_members_of_a_family_goes_in_their_header()
        {
            var files = Generate(Device(
                Peripheral("GPIOA", 0x1000, Register("MODER", 0, Field("MODE0", 0, 2))) +
                Peripheral("GPIOB", 0x2000, Register("MODER", 0, Field("MODE0", 0, 2))) +
                Peripheral("GPIOI", 0x9000, Register("MODER", 0, Field("MODE0", 0, 1)))));

            Assert.Equal(new[] { "device.hpp", "gpio.hpp", "interrupts.hpp", "mmio.hpp" }, files.Keys);
            Assert.Equal(new[] { "gpio_p", "gpioi_p" }, Classes(files["gpio.hpp"], "_p"));
        }

        [Fact]
        public void Instances_are_listed_with_their_numbers_in_order()
        {
            var header = Generate(Device(
                Peripheral("UART12", 0x3000, derivedFrom: "UART4") +
                Peripheral("UART4", 0x1000, SimpleRegister()) +
                Peripheral("UART5", 0x2000, derivedFrom: "UART4")))["uart.hpp"];

            Assert.Equal(new[] { "uart_p& uart4", "uart_p& uart5", "uart_p& uart12" }, Instances(header));
        }

        [Fact]
        public void A_reset_value_that_differs_between_peripherals_is_given_per_peripheral()
        {
            string Port(string name, long address, string moder, string derivedFrom = "") => derivedFrom.Length > 0
                ? Peripheral(name, address, derivedFrom: derivedFrom)
                : Peripheral(name, address,
                    Register("MODER", 0, Field("MODE0", 0, 2), extra: Reset(moder)) +
                    Register("ODR", 4, Field("OD0", 0, 1), extra: Reset("0")));

            var header = Generate(Device(Port("GPIOA", 0x1000, "0xABFFFFFF") + Port("GPIOB", 0x2000, "0xFFFFFEBF") + Port("GPIOC", 0x3000, "", "GPIOB")))["gpio.hpp"];

            Assert.Contains("static constexpr uint32_t reset_value = 0; // 0 0x0", header);
            Assert.DoesNotContain("reset_value = 0b", header);
            Assert.Contains("namespace gpioa_reset\n{\ninline constexpr uint32_t moder = 0xABFFFFFF;\n}", header);
            Assert.Contains("namespace gpiob_reset\n{\ninline constexpr uint32_t moder = 0xFFFFFEBF;\n}", header);
            Assert.Contains("namespace gpioc_reset\n{\ninline constexpr uint32_t moder = 0xFFFFFEBF;\n}", header);
        }

        [Fact]
        public void A_reset_value_is_inherited_from_the_device()
        {
            var header = Generate(Device(Peripheral("P", 0x1000, SimpleRegister()), extra: Reset("0x12")))["p.hpp"];

            Assert.Contains("static constexpr uint32_t reset_value = 0b10010; // 18 0x12", header);
        }

        // ── Numbered registers as an array ─────────────────────────────────

        static string Channel(int index, int offset, string extraControlField = "") =>
            Register($"C{index}CR", offset, Field("EN", 0, 1) + extraControlField) +
            Register($"C{index}SAR", offset + 4, Field("SA", 0, 32));

        [Fact]
        public void Numbered_blocks_of_registers_become_an_array()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                SimpleRegister("ISR") + Channel(0, 0x10) + Channel(1, 0x30) + Channel(2, 0x50)));

            Assert.Contains("class c_c {", header);
            Assert.Contains("\tc_c c[3];\n", header);
            Assert.Contains("static_assert(offsetof(p_p, c) == 16);", header);
            Assert.Contains("static_assert(sizeof(p_p::c_c) == 32);", header);
            Assert.Contains("static_assert(offsetof(p_p::c_c, sar) == 4);", header);
            Assert.Equal(new[] { "isr_r", "cr_r", "sar_r" }, Classes(header, "_r"));
            Assert.Contains(" * Registers Cx, x = 0 to 2\n", header);
        }

        [Fact]
        public void A_register_or_a_field_that_only_some_blocks_have_is_given_to_all()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Channel(0, 0x10) + Channel(1, 0x30, Field("TWOD", 8, 1)) + Register("C1TR3", 0x38, Field("SAO", 0, 13))));

            Assert.Contains("\tc_c c[2];\n", header);
            Assert.Contains("class twod_f {", header);
            Assert.Contains("static_assert(offsetof(p_p::c_c, tr3) == 8);", header);
        }

        [Fact]
        public void Blocks_not_numbered_from_zero_stay_separate_registers()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Channel(1, 0x10) + Channel(2, 0x30)));

            Assert.DoesNotContain("class c_c {", header);
            Assert.Contains("static_assert(offsetof(p_p, c2sar) == 52);", header);
        }

        [Fact]
        public void Blocks_at_irregular_distances_stay_separate_registers()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Channel(0, 0x10) + Channel(1, 0x30) + Channel(2, 0x60)));

            Assert.DoesNotContain("class c_c {", header);
        }

        [Fact]
        public void Blocks_with_a_foreign_register_among_them_stay_separate_registers()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Channel(0, 0x10) + SimpleRegister("OTHER", 0x20) + Channel(1, 0x30)));

            Assert.DoesNotContain("class c_c {", header);
        }

        [Fact]
        public void Blocks_whose_registers_disagree_on_a_field_stay_separate_registers()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Register("C0CR", 0x10, Field("EN", 0, 1)) + Register("C0SAR", 0x14, Field("SA", 0, 32)) +
                Register("C1CR", 0x30, Field("EN", 4, 1)) + Register("C1SAR", 0x34, Field("SA", 0, 32))));

            Assert.DoesNotContain("class c_c {", header);
        }

        [Fact]
        public void A_run_of_single_numbered_registers_is_not_an_array()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Register("CCR1", 0, Field("A", 0, 16)) + Register("CCR2", 4, Field("A", 0, 16)) +
                Register("AHB1ENR", 8, Field("X", 0, 1)) + Register("AHB2ENR", 12, Field("Y", 0, 1))));

            Assert.Empty(Classes(header, "_c"));
        }

        // ── Clusters ───────────────────────────────────────────────────────

        [Fact]
        public void A_cluster_array_with_named_indices_is_one_cluster_per_name()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Cluster("CH%s", 0, SimpleRegister("X"), extra: "<dim>2</dim><dimIncrement>8</dimIncrement><dimIndex>A,B</dimIndex>")));

            Assert.Contains("\tcha_c cha;\n", header);
            Assert.Contains("\tchb_c chb;\n", header);
            Assert.Contains("static_assert(offsetof(p_p, chb) == 8);", header);
        }

        [Fact]
        public void Clusters_at_the_same_address_are_alternatives_in_a_union()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Cluster("MODE0", 0, Register("CTRL", 0, Field("A", 0, 1))) +
                Cluster("MODE1", 0, Register("CTRL", 0, Field("B", 0, 2)))));

            Assert.Contains("\tunion {\n\t\tmode0_c mode0;\n\t\tmode1_c mode1;\n\t};\n", header);
        }

        [Fact]
        public void A_cluster_array_that_cannot_be_a_struct_array_becomes_plain_registers()
        {
            // 32-bit registers every 6 bytes: no C++ class has that size.
            var header = GenerateOne(Peripheral("P", 0x1000,
                Cluster("CH[%s]", 0, SimpleRegister("X"), extra: "<dim>2</dim><dimIncrement>6</dimIncrement>")));

            Assert.Empty(Classes(header, "_c"));
            Assert.Contains("> ch0_x;\n", header);
            Assert.Contains("> ch1_x;\n", header);
        }

        // ── Named values ───────────────────────────────────────────────────

        static string Values(params (string Name, string Value)[] values) =>
            "<enumeratedValues>" + string.Concat(values.Select(v => $"<enumeratedValue><name>{v.Name}</name><description>{v.Name} described</description><value>{v.Value}</value></enumeratedValue>")) + "</enumeratedValues>";

        [Fact]
        public void Enumerated_values_are_constants_of_the_field()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("MODE", 4, 2, Values(("Off", "0"), ("Fast", "0x3"))))));

            Assert.Contains("\tstatic const mode_f off, fast;\n", header);
            Assert.Contains("inline constexpr p_p::r_r::mode_f p_p::r_r::mode_f::off{0x0};\n", header);
            Assert.Contains("inline constexpr p_p::r_r::mode_f p_p::r_r::mode_f::fast{0x3};\n", header);
            Assert.Contains(" * MODE field\n *\n *   0x0  off: Off described\n *   0x3  fast: Fast described\n */\nclass mode_f {", header);
        }

        [Fact]
        public void A_one_bit_field_takes_true_and_false()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("EN", 0, 1, Values(("Disabled", "0"), ("Enabled", "1"))))));

            Assert.Contains("p_p::r_r::en_f::disabled{false};", header);
            Assert.Contains("p_p::r_r::en_f::enabled{true};", header);
        }

        [Fact]
        public void A_value_named_after_its_number_is_documented_but_gets_no_constant()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("SW", 0, 2, Values(("B_0x0", "0x0"), ("B_0x3", "0x3"))))));

            Assert.DoesNotContain("static const sw_f", header);
            Assert.Contains(" *   0x3  B_0x3 described\n", header);
        }

        [Fact]
        public void A_value_name_that_cannot_be_used_as_is_is_prefixed()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("F", 0, 2, Values(("default", "0"), ("mask", "1"), ("1x", "2"))))));

            Assert.Contains("\tstatic const f_f _default, _mask, _1x;\n", header);
        }

        [Fact]
        public void A_value_that_names_no_single_number_or_does_not_fit_is_left_out()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0, Field("F", 0, 2, Values(("Any", "#1x"), ("Big", "7"), ("Ok", "#10"))))));

            Assert.Contains("\tstatic const f_f ok;\n", header);
        }

        [Fact]
        public void Enumerated_values_can_be_derived_from_another_field()
        {
            var header = GenerateOne(Peripheral("P", 0x1000, Register("R", 0,
                Field("A", 0, 1, "<enumeratedValues><name>OnOff</name><enumeratedValue><name>Off</name><value>0</value></enumeratedValue><enumeratedValue><name>On</name><value>1</value></enumeratedValue></enumeratedValues>") +
                Field("B", 1, 1, "<enumeratedValues derivedFrom=\"OnOff\"/>") +
                Field("C", 2, 1, "<enumeratedValues derivedFrom=\"R.A\"/>"))));

            Assert.Contains("\tstatic const b_f off, on;\n", header);
            Assert.Contains("\tstatic const c_f off, on;\n", header);
        }

        // ── Device-wide headers ────────────────────────────────────────────

        [Fact]
        public void Interrupts_are_in_the_device_namespace()
        {
            var header = Generate(Device(Peripheral("P", 0x1000, SimpleRegister(), extra: Irq)))["interrupts.hpp"];

            Assert.Contains("namespace DEV\n{\n", header);
            Assert.Contains("inline constexpr std::size_t irq_count = 4;\n", header);
            Assert.Contains("enum IRQn_Type : std::uint16_t\n{\n\tI_IRQn", header);
            Assert.EndsWith("};\n\n} // DEV\n", header, StringComparison.Ordinal);
        }

        [Fact]
        public void The_device_header_describes_the_device_and_its_core()
        {
            const string cpu = "<cpu><name>CM0+</name><revision>r0p1</revision><endian>little</endian><mpuPresent>false</mpuPresent><fpuPresent>false</fpuPresent><nvicPrioBits>2</nvicPrioBits><vendorSystickConfig>false</vendorSystickConfig></cpu>";

            var header = Generate(Device(Peripheral("P", 0x1000, SimpleRegister()), extra: cpu))["device.hpp"];

            Assert.Contains("inline constexpr char device_name[] = \"DEV\";\n", header);
            Assert.Contains("inline constexpr char device_version[] = \"1.0\";\n", header);
            Assert.Contains("inline constexpr char cpu_name[] = \"CM0+\";\n", header);
            Assert.Contains("inline constexpr unsigned nvic_priority_bits = 2;\n", header);
            Assert.Contains("inline constexpr bool has_mpu = false;\n", header);
        }

        [Fact]
        public void A_device_without_cpu_has_no_core_constants()
        {
            var header = Generate(Device(Peripheral("P", 0x1000, SimpleRegister())))["device.hpp"];

            Assert.DoesNotContain("cpu_name", header);
        }
    }
}
