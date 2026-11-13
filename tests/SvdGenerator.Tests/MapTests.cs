using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static SvdGenerator.Tests.Svd;

namespace SvdGenerator.Tests
{
    /// <summary>
    /// The map at the top of each peripheral header and the README of the
    /// directory: what lets a reader go to one register without reading a header.
    /// </summary>
    public class MapTests
    {
        static readonly Regex RowPattern = new(@"^//\s+(\+?0x[0-9A-F]+)\s+(rw|r|w|-)\s+(\S+)\s+(\S+)\s+(\d+)-(\d+)(?:\s+(.*))?$");

        sealed record Row(string Offset, string Access, string Member, string Class, int First, int Last, string Fields);

        static List<Row> Rows(string header) =>
            Lines(header)
                .TakeWhile(l => l != "#pragma once")
                .Select(l => RowPattern.Match(l))
                .Where(m => m.Success)
                .Select(m => new Row(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value,
                    int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), m.Groups[7].Value))
                .ToList();

        /// <summary>The fixture of the C++ tests: every construct the generator knows.</summary>
        static SortedDictionary<string, string> Fixture()
        {
            var directory = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(directory, "SvdGenerator.sln")))
                directory = Path.GetDirectoryName(directory) ?? throw new InvalidOperationException("repository root not found");
            return Generate(File.ReadAllText(Path.Combine(directory, "tests", "cpp", "fixture.svd")));
        }

        // What the map promises: the lines it gives run from the comment of the
        // class named on the row to the brace that closes it.
        static void AssertRangesAreExact(string header)
        {
            var lines = Lines(header);
            var rows = Rows(header);
            Assert.NotEmpty(rows);

            foreach (var row in rows)
            {
                Assert.Equal("/**", lines[row.First - 1]);
                var declaration = Enumerable.Range(row.First, row.Last - row.First).First(i => lines[i].StartsWith("class ", StringComparison.Ordinal));
                Assert.Equal($"class {row.Class} {{", lines[declaration]);
                Assert.Equal("};", lines[row.Last - 1]);
                // The class is whole: as many classes open as close between the two lines.
                var inside = lines[(row.First - 1)..row.Last];
                Assert.Equal(inside.Count(l => l.StartsWith("class ", StringComparison.Ordinal) && l.EndsWith(" {", StringComparison.Ordinal)), inside.Count(l => l == "};"));
            }
        }

        [Fact]
        public void The_lines_of_the_map_are_those_of_the_classes()
        {
            foreach (var (name, content) in Fixture().Where(f => f.Key is "ctl.hpp" or "port.hpp" or "dma.hpp" or "bank.hpp"))
            {
                Assert.True(Rows(content).Count > 0, name);
                AssertRangesAreExact(content);
            }
        }

        [Fact]
        public void The_lines_stay_right_whatever_the_length_of_the_banner()
        {
            const string notice = "<!--\nline 1\nline 2\nline 3\nline 4\nline 5\n-->\n";
            var registers = Register("A", 0, Field("X", 0, 3)) + Register("B", 4, Field("Y", 4, 1));

            AssertRangesAreExact(Generate(Device(Peripheral("P", 0x1000, registers)))["p.hpp"]);
            AssertRangesAreExact(Generate(Device(Peripheral("P", 0x1000, registers), prolog: notice))["p.hpp"]);
        }

        [Fact]
        public void The_map_is_between_the_banner_and_the_code()
        {
            var lines = Lines(GenerateOne(Peripheral("P", 0x1000, SimpleRegister())));

            var map = Array.IndexOf(lines, "// Map of this file: where each class is, to read one register without the rest.");
            var pragma = Array.IndexOf(lines, "#pragma once");
            Assert.StartsWith(Emitter.BannerStart, lines[0], StringComparison.Ordinal);
            Assert.True(map > 0 && map < pragma);
            Assert.Equal("", lines[map - 1]);
            Assert.Equal("", lines[pragma - 1]);
        }

        [Fact]
        public void A_register_is_one_line_with_its_offset_access_class_and_fields()
        {
            var header = GenerateOne(Peripheral("P", 0x1000,
                Register("CR", 0x8, Field("EN", 0, 1) + Field("MODE", 4, 3) + Field("RDY", 31, 1, "<access>read-only</access>")) +
                Register("SR", 0xC, Field("BUSY", 0, 1, "<access>read-only</access>")) +
                Register("ICR", 0x10, Field("CLR", 2, 1, "<access>write-only</access>"))));

            var rows = Rows(header);
            Assert.Equal(new[] { "cr", "sr", "icr" }, rows.Select(r => r.Member));
            Assert.Equal(new[] { "0x008", "0x00C", "0x010" }, rows.Select(r => r.Offset));
            Assert.Equal(new[] { "rw", "r", "w" }, rows.Select(r => r.Access));
            Assert.Equal(new[] { "cr_r", "sr_r", "icr_r" }, rows.Select(r => r.Class));
            // A field is marked only when its access is not the register's.
            Assert.Equal(new[] { "en[0] mode[6:4] rdy[31](r)", "busy[0]", "clr[2]" }, rows.Select(r => r.Fields));
        }

        [Fact]
        public void The_title_of_a_class_gives_its_lines_and_its_instances()
        {
            var header = Generate(Device(
                Peripheral("UART4", 0x1000, SimpleRegister()) +
                Peripheral("UART5", 0x2000, derivedFrom: "UART4")))["uart.hpp"];

            var title = Lines(header).Single(l => l.StartsWith("// class uart_p, lines ", StringComparison.Ordinal));
            var match = Regex.Match(title, @"^// class uart_p, lines (\d+)-(\d+): uart4 0x1000, uart5 0x2000$");
            Assert.True(match.Success, title);
            Assert.Equal("class uart_p {", Lines(header)[int.Parse(match.Groups[1].Value) + 2]);
            Assert.Equal("};", Lines(header)[int.Parse(match.Groups[2].Value) - 1]);
        }

        [Fact]
        public void A_header_with_several_classes_maps_each_of_them()
        {
            string Timer(string name, long address, int width) => Peripheral(name, address, Register($"{name}_CNT", 0, Field("CNT", 0, width)));

            var header = Generate(Device(Timer("TIM1", 0x1000, 16) + Timer("TIM2", 0x2000, 32)))["tim.hpp"];

            Assert.Equal(2, Lines(header).Count(l => l.StartsWith("// class tim", StringComparison.Ordinal)));
            Assert.Equal(new[] { "cnt[15:0]", "cnt[31:0]" }, Rows(header).Select(r => r.Fields));
            AssertRangesAreExact(header);
        }

        [Fact]
        public void A_cluster_is_a_line_followed_by_its_registers_with_their_own_offsets()
        {
            var rows = Rows(Fixture()["dma.hpp"]);

            Assert.Equal(new[] { "isr", "c[3]", "cr", "sar", "tr3" }, rows.Select(r => r.Member));
            Assert.Equal(new[] { "0x000", "0x010", "+0x000", "+0x004", "+0x008" }, rows.Select(r => r.Offset));
            Assert.Equal("-", rows[1].Access);
            Assert.Equal("c_c", rows[1].Class);
            Assert.StartsWith("3 x 32 bytes", rows[1].Fields, StringComparison.Ordinal);
            // The registers of the cluster are inside its lines.
            Assert.All(rows.Skip(2), r => Assert.True(r.First > rows[1].First && r.Last < rows[1].Last));
        }

        [Fact]
        public void Arrays_shared_classes_and_lanes_show_as_they_are_written_in_code()
        {
            var rows = Rows(Fixture()["bank.hpp"]);

            Assert.Contains(rows, r => r.Member == "key[4]" && r.Class == "key_r");
            Assert.Contains(rows, r => r.Member == "slot[3]" && r.Class == "slot_c");
            // Two registers that share a class point at the same lines.
            var bytes = rows.Where(r => r.Class == "bytex_r").ToList();
            Assert.Equal(new[] { "_lane_0.byte0", "_lane_0.byte1" }, bytes.Select(r => r.Member));
            Assert.Equal(bytes[0].First, bytes[1].First);
        }

        // ── README of the directory ────────────────────────────────────────

        [Fact]
        public void The_readme_starts_with_the_banner_and_says_where_the_headers_come_from()
        {
            const string notice = "<!--\nCopyright (c) 2025 Vendor.\n-->\n";
            var readme = Generate(Device(Peripheral("P", 0x1000, SimpleRegister()), prolog: notice))["README.md"];

            Assert.StartsWith("<!-- Generated by SvdGenerator 1.2.3; do not edit. -->\n\n# DEV register headers\n", readme, StringComparison.Ordinal);
            Assert.Contains("| Source | `test.svd` (device DEV, version 1.0) |\n", readme);
            Assert.Contains("| SHA-256 of the source | `0123abcd` |\n", readme);
            Assert.Contains("| Namespace | `DEV` |\n", readme);
            Assert.DoesNotContain('\r', readme);
        }

        [Fact]
        public void The_readme_says_whose_is_what()
        {
            const string notice = "<!--\nCopyright (c) 2025 Vendor.\n-->\n";
            var readme = Generate(Device(Peripheral("P", 0x1000, SimpleRegister()), prolog: notice))["README.md"];

            // The notice of the source is an attribution of what comes from it,
            // not a license put on the generated code, and mmio.hpp is apart.
            Assert.Contains(
                "## Whose is what\n\n" +
                "- **What the headers take from the source file**, names, addresses and descriptions,\n" +
                "  remains under the notice that file carries:\n\n" +
                "  > Copyright (c) 2025 Vendor.  \n\n" +
                "- **The code generated around it** carries no license of SvdGenerator: the generator puts\n" +
                "  none of its own on what it generates.\n" +
                "- **`mmio.hpp`** is not generated from the source file: it is part of SvdGenerator, under\n" +
                "  the MIT license written in its header.\n", readme);
            Assert.DoesNotContain("derive", readme);
        }

        [Fact]
        public void The_readme_of_a_source_without_notice_invents_none()
        {
            var readme = Generate(Device(Peripheral("P", 0x1000, SimpleRegister())))["README.md"];

            Assert.Contains("remains under the terms of that file, which carries no notice.\n", readme);
            Assert.DoesNotContain("Copyright", readme);
        }

        [Fact]
        public void The_readme_lists_every_peripheral_with_the_line_of_its_class()
        {
            var files = Fixture();
            var readme = files["README.md"];

            var rows = Lines(readme)
                .Select(l => Regex.Match(l, @"^\| `(\w+)` \| `(0x[0-9A-F]+)` \| `(\w+)` \| \[`([\w.]+)`\]\(([\w.]+)\) \| (\d+) \| (.*?) \| (.*) \|$"))
                .Where(m => m.Success)
                .ToList();

            Assert.Equal(new[] { "bank", "ctl", "dma1", "porta", "portb", "portc" }, rows.Select(m => m.Groups[1].Value));
            Assert.Equal(new[] { "bank_p", "ctl_p", "dma_p", "port_p", "port_p", "port_p" }, rows.Select(m => m.Groups[3].Value));
            Assert.Equal("`CTL_IRQn` (2), `CTL_ERR_IRQn` (5)", rows[1].Groups[7].Value);

            foreach (var row in rows)
            {
                var header = Lines(files[row.Groups[4].Value]);
                var line = int.Parse(row.Groups[6].Value);
                Assert.Equal("/**", header[line - 1]);
                Assert.Equal($"class {row.Groups[3].Value} {{", header[line + 2]);
                Assert.Equal(row.Groups[4].Value, row.Groups[5].Value);
            }
        }

        [Fact]
        public void The_readme_describes_the_device_headers_that_exist()
        {
            const string cortexA = "<cpu><name>CA7</name><revision>r0p0</revision><endian>little</endian><mpuPresent>false</mpuPresent><fpuPresent>true</fpuPresent><nvicPrioBits>2</nvicPrioBits><vendorSystickConfig>false</vendorSystickConfig></cpu>";

            var cortexM = Generate(Device(Peripheral("P", 0x1000, SimpleRegister())))["README.md"];
            var other = Generate(Device(Peripheral("P", 0x1000, SimpleRegister()), extra: cortexA))["README.md"];

            Assert.Contains("| [`vectors.hpp`](vectors.hpp) |", cortexM);
            Assert.DoesNotContain("vectors.hpp", other);
            Assert.Contains("| Core | CA7, r0p0, 2 interrupt priority bits, FPU, no MPU |\n", other);
            Assert.Contains("| [`mmio.hpp`](mmio.hpp) |", other);
        }

        [Fact]
        public void A_description_cannot_break_the_table_of_the_readme()
        {
            var readme = Generate(Device("<peripheral><name>P</name><description>a | b\nc</description><baseAddress>0x1000</baseAddress><registers>" + SimpleRegister() + "</registers></peripheral>"))["README.md"];

            Assert.Contains(" | a \\| b c |\n", readme);
        }

        [Fact]
        public void A_readme_written_by_hand_is_not_overwritten_and_a_generated_one_is_replaced()
        {
            using var input = new TempDirectory();
            using var output = new TempDirectory();
            var svd = input.Write("device.svd", Device(Peripheral("P", 0x1000, SimpleRegister())));
            output.Write("README.md", "# Mine\n");

            var error = new StringWriter();
            Assert.Equal(Program.ExitRefused, Program.Run(new[] { svd, output.Path }, new StringWriter(), error));
            Assert.Contains("README.md", error.ToString());
            Assert.Equal("# Mine\n", File.ReadAllText(output.File("README.md")));
            Assert.Equal(new[] { "README.md" }, output.Names());

            Assert.Equal(Program.ExitOk, Program.Run(new[] { svd, output.Path, "--force" }, new StringWriter(), new StringWriter()));
            Assert.StartsWith(Emitter.MarkdownBannerStart, File.ReadAllText(output.File("README.md")), StringComparison.Ordinal);

            // Once generated it is ours: replaced without --force, and checked.
            File.AppendAllText(output.File("README.md"), "edited\n");
            Assert.Equal(Program.ExitOutOfDate, Program.Run(new[] { svd, output.Path, "--check" }, new StringWriter(), new StringWriter()));
            Assert.Equal(Program.ExitOk, Program.Run(new[] { svd, output.Path }, new StringWriter(), new StringWriter()));
            Assert.Equal(Program.ExitOk, Program.Run(new[] { svd, output.Path, "--check" }, new StringWriter(), new StringWriter()));
        }
    }
}
