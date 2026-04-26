using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace SvdGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Call with SVD file and output directory to generate the C++ code");
                return;
            }

            var input = args[0];
            var output = args[1];


            if (!File.Exists(input))
            {
                Console.WriteLine($"Can't find file {input}");
                return;
            }

            var serializer = new XmlSerializer(typeof(device));
            device? device;

            using (var stream = new FileStream(input, FileMode.Open))
                device = serializer.Deserialize(stream) as device;

            if (device == null)
            {
                Console.WriteLine($"Can't deserialize file {input}");
                return;
            }

            if (Directory.Exists(output))
                foreach (var file in Directory.EnumerateFiles(output))
                    File.Delete(file);
            else
                Directory.CreateDirectory(output);


            var grouping = device.peripherals
                .Where(p => string.IsNullOrEmpty(p.derivedFrom))
                .Select(p => (p, device.peripherals.Where(p2 => p2.derivedFrom == p.name).Append(p).OrderBy(p2 => p2.name)))
                .GroupBy(p => p.p.groupName).ToArray();


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


            var interrupts = device.peripherals
                .Where(p => p.interrupt != null)
                .SelectMany(p => p.interrupt)
                .OrderBy(i => i.value.ToValue())
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

        }
    }
}
