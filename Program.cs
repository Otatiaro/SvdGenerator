using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace SvdGenerator
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
            {
                Console.WriteLine($"SvdGenerator {InformationalVersion}");
                return 0;
            }

            if (args.Any(a => a == "--help" || a == "-h"))
            {
                PrintUsage(Console.Out);
                return 0;
            }

            if (args.Length != 2)
            {
                PrintUsage(Console.Error);
                return 1;
            }

            var input = args[0];
            var output = args[1];

            if (!File.Exists(input))
            {
                Console.Error.WriteLine($"Can't find file {input}");
                return 1;
            }

            var serializer = new XmlSerializer(typeof(device));
            device? device;

            // Vendor SVDs occasionally drift from the CMSIS-SVD spec on
            // <access> casing. Nordic ships <access>read-writeonce</access>
            // (lowercase 'o') across the whole nrf52/53/54 family, which the
            // strict XmlSerializer-generated reader rejects. Patch known
            // misspellings in-place; leave everything else untouched so a
            // truly broken file still fails loud.
            var content = SanitizeSvd(File.ReadAllText(input));
            using (var reader = new StringReader(content))
                device = serializer.Deserialize(reader) as device;

            if (device == null)
            {
                Console.Error.WriteLine($"Can't deserialize file {input}");
                return 1;
            }

            // Apply CMSIS-SVD register-level derivedFrom inheritance before
            // generation. Some vendor SVDs (Atmel SAMD21 PMUX1_%s derived
            // from PMUX0_%s) leave size, fields, access, etc. implicit on
            // the derived register and our fallbacks would compute the
            // wrong layout otherwise.
            foreach (var p in device.peripherals)
                p.ResolveDerivedFrom();

            if (Directory.Exists(output))
            {
                // Avoid clobbering anything we did not generate ourselves —
                // a typo like `SvdGenerator foo.svd .` should not silently
                // wipe a working directory.
                var stranger = Directory.EnumerateFiles(output)
                    .FirstOrDefault(f => !f.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase));
                if (stranger != null)
                {
                    Console.Error.WriteLine($"Refusing to clean '{output}': it contains a non-.hpp file ('{Path.GetFileName(stranger)}'). Pass an empty directory or one that only holds previously generated headers.");
                    return 2;
                }
                foreach (var file in Directory.EnumerateFiles(output))
                    File.Delete(file);
            }
            else
            {
                Directory.CreateDirectory(output);
            }


            // groupName is optional in CMSIS-SVD. Some vendors (NXP Kinetis,
            // RaspberryPi) leave it empty; fall back to the peripheral's own
            // name so each ungrouped peripheral lives in its own .hpp.
            var grouping = device.peripherals
                .Where(p => string.IsNullOrEmpty(p.derivedFrom))
                .Select(p => (p, device.peripherals.Where(p2 => p2.derivedFrom == p.name).Append(p).OrderBy(p2 => p2.name)))
                .GroupBy(p => string.IsNullOrEmpty(p.p.groupName) ? p.p.name : p.p.groupName).ToArray();


            foreach (var group in grouping.Where(g => g.Count() == 1))
            {
                // there is only one "base class" in the group, make it the name of the group
                var file = Path.Combine(output, group.Key.FileName());
                File.WriteAllLines(file, new[] { "#pragma once", "", "#if __has_include(\"memory.hpp\")", "#include \"memory.hpp\"", "#else", "#include <utility/memory.hpp>", "#endif", "", $"namespace {device.ToNamespace()}", "{", "" });
                File.AppendAllText(file, group.First().p.ClassDefinition(group.Key.PeripheralName()));
                foreach (var periph in group.First().Item2)
                    File.AppendAllLines(file, new[] { $"inline {group.Key.PeripheralName()}& {periph.PeriphName()} = *reinterpret_cast<{group.Key.PeripheralName()}*>({periph.baseAddress.ToValue().ToHex()});" });
                File.AppendAllLines(file, new[] { "", $"}} // {device.ToNamespace()}", "" });

            }

            foreach (var group in grouping.Where(g => g.Count() > 1))
            {
                // there are multiple "base classes" in the same group (like TIM where some timers have slightly different configurations)
                var file = Path.Combine(output, group.Key.FileName());
                File.WriteAllLines(file, new[] { "#pragma once", "", "#if __has_include(\"memory.hpp\")", "#include \"memory.hpp\"", "#else", "#include <utility/memory.hpp>", "#endif", "", $"namespace {device.ToNamespace()}", "{", "" });

                foreach (var tuple in group)
                    File.AppendAllText(file, tuple.p.ClassDefinition());

                foreach (var tuple in group)
                {
                    foreach (var periph in tuple.Item2)
                    {
                        File.AppendAllLines(file, new[] { $"inline {tuple.p.ClassName()}& {periph.PeriphName()} = *reinterpret_cast<{tuple.p.ClassName()}*>({periph.baseAddress.ToValue().ToHex()});" });
                    }
                }

                File.AppendAllLines(file, new[] { "", $"}} // {device.ToNamespace()}", "" });


            }

            foreach (var file in grouping.Select(g => g.Key.FileName()))
            {
                var all = Path.Combine(output, "all.hpp");
                //File.AppendAllLines(all, new []{$"#include \"{file}\""});
            }


            // Dedupe: an SVD can list the same IRQ on every peripheral that
            // shares it (e.g. TIM1_UP_TIM10_IRQn appears on TIM1 and TIM10),
            // and some SVDs even repeat an entry on a single peripheral with
            // slightly different descriptions (FPU). One enumerator per
            // (name, value) is what the C++ enum needs.
            var interrupts = device.peripherals
                .Where(p => p.interrupt != null)
                .SelectMany(p => p.interrupt)
                .GroupBy(i => (i.name, i.value))
                .Select(g => g.First())
                .OrderBy(i => i.value.ToValue())
                .ThenBy(i => i.name)
                .ToArray();

            var irqMaxSize = interrupts.Select(i => i.name.Length).Max();
            var commentMaxSize = interrupts.Select(i => i.description.ToOneLine().Length).Max();
            var irqMax = interrupts.Select(i => i.value.ToValue()).Max();

            var sb = new StringBuilder();

            sb.AppendLine("#pragma once");
            sb.AppendLine();
            sb.AppendLine("#include <cstddef>");

            sb.AppendLine();
            sb.AppendLine($"constexpr std::size_t irq_count = {irqMax + 1};");
            sb.AppendLine();

            sb.AppendLine("enum IRQn_Type");
            sb.AppendLine("{");

            foreach (var interrupt in interrupts)
                sb.AppendLine($"\t{interrupt.name.ToIrq().PadRight(irqMaxSize + 10)} = {interrupt.value.ToValue(),-5}, /** {interrupt.description.ToOneLine().PadRight(commentMaxSize)} */");

            sb.AppendLine("};");


            var intFile = Path.Combine(output, "interrupts.hpp");
            File.WriteAllText(intFile, sb.ToString());

            return 0;
        }

        static string InformationalVersion =>
            typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";

        // Patch known vendor deviations from the CMSIS-SVD spec before the
        // strict XmlSerializer-generated reader sees them.
        static string SanitizeSvd(string content) => content
            .Replace("<access>read-writeonce</access>", "<access>read-writeOnce</access>");

        static void PrintUsage(TextWriter w)
        {
            w.WriteLine("Usage: SvdGenerator <input.svd> <output_dir>");
            w.WriteLine();
            w.WriteLine("Generates typed C++ peripheral headers for ARM Cortex-M targets");
            w.WriteLine("from a CMSIS-SVD file.");
            w.WriteLine();
            w.WriteLine("Options:");
            w.WriteLine("  -h, --help     Show this help and exit.");
            w.WriteLine("  -v, --version  Show version and exit.");
        }
    }
}
