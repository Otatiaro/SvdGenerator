using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace SvdGenerator
{
    /// <summary>
    /// Reads a CMSIS-SVD file into the generator's model, with everything the SVD
    /// leaves implicit made explicit: derivedFrom on peripherals and registers,
    /// properties inherited from the device, peripheral or cluster, dim arrays and
    /// clusters. The emitter never looks at the raw XML classes.
    /// </summary>
    public static class SvdReader
    {
        public static Device Read(string path)
        {
            if (!File.Exists(path))
                throw new SvdException($"Can't find file {path}");

            var bytes = File.ReadAllBytes(path);
            string text;
            using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                text = reader.ReadToEnd();

            return Parse(text, Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        public static Device Parse(string text, string sourceFile, string sha256)
        {
            device? raw;
            try
            {
                var serializer = new XmlSerializer(typeof(device));
                using var reader = new StringReader(Sanitize(text));
                raw = serializer.Deserialize(reader) as device;
            }
            catch (InvalidOperationException e)
            {
                throw new SvdException($"Can't read {sourceFile}: {e.InnerException?.Message ?? e.Message}");
            }

            if (raw == null)
                throw new SvdException($"Can't deserialize file {sourceFile}");
            if (string.IsNullOrWhiteSpace(raw.name))
                throw new SvdException($"{sourceFile}: the device has no name");

            return new Device
            {
                Name = raw.name,
                Version = raw.version ?? string.Empty,
                Peripherals = new PeripheralBuilder(raw).Build(),
                SourceFile = sourceFile,
                SourceSha256 = sha256,
                Notice = ExtractNotice(text, raw.licenseText),
            };
        }

        // Vendor SVDs occasionally drift from the CMSIS-SVD spec on <access>
        // casing. Nordic ships <access>read-writeonce</access> (lowercase 'o')
        // across the whole nrf52/53/54 family, which the strict
        // XmlSerializer-generated reader rejects. Patch known misspellings
        // in-place; leave everything else untouched so a truly broken file
        // still fails loud.
        static string Sanitize(string content) => content
            .Replace("<access>read-writeonce</access>", "<access>read-writeOnce</access>");

        // The notice a vendor puts on its SVD: the XML comments that precede the
        // root element (where ST and others write their copyright and license),
        // then the <licenseText> element.
        static List<string> ExtractNotice(string text, string? licenseText)
        {
            var lines = new List<string>();

            var root = text.IndexOf("<device", StringComparison.Ordinal);
            if (root > 0)
            {
                foreach (Match comment in Regex.Matches(text[..root], "<!--(.*?)-->", RegexOptions.Singleline))
                    AddBlock(lines, comment.Groups[1].Value);
            }

            if (!string.IsNullOrWhiteSpace(licenseText))
                AddBlock(lines, licenseText.Replace("\\n", "\n"));

            return lines;
        }

        static void AddBlock(List<string> lines, string block)
        {
            var blockLines = block.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).ToList();
            while (blockLines.Count > 0 && blockLines[0].Length == 0) blockLines.RemoveAt(0);
            while (blockLines.Count > 0 && blockLines[^1].Length == 0) blockLines.RemoveAt(blockLines.Count - 1);
            if (blockLines.Count == 0)
                return;
            if (lines.Count > 0)
                lines.Add(string.Empty);
            lines.AddRange(blockLines);
        }

        static Access ToAccess(accessType access) => access switch
        {
            accessType.@readonly => Access.ReadOnly,
            accessType.writeonly => Access.WriteOnly,
            accessType.writeOnce => Access.WriteOnly,
            _ => Access.ReadWrite,
        };

        /// <summary>Properties a register inherits from its cluster, peripheral or device.</summary>
        readonly record struct Defaults(int Size, Access Access);

        /// <summary>A register in its context: where it sits and what it inherits.</summary>
        sealed record Placed(registerType Raw, string Prefix, long BaseOffset, Defaults Defaults);

        sealed class PeripheralBuilder
        {
            readonly device _device;
            readonly Dictionary<string, peripheralType> _rawByName = new(StringComparer.Ordinal);
            readonly Dictionary<peripheralType, Dictionary<string, registerType>> _registersByName = new();

            public PeripheralBuilder(device device)
            {
                _device = device;
            }

            public List<Peripheral> Build()
            {
                var raws = (_device.peripherals ?? Array.Empty<peripheralType>()).SelectMany(ExpandPeripheral).ToList();
                foreach (var raw in raws)
                {
                    if (string.IsNullOrWhiteSpace(raw.name))
                        throw new SvdException("a peripheral has no name");
                    _rawByName.TryAdd(raw.name, raw);
                }

                var built = new Dictionary<peripheralType, Peripheral>();
                var result = new List<Peripheral>();
                foreach (var raw in raws)
                {
                    var peripheral = new Peripheral
                    {
                        Name = raw.name,
                        Description = Inherited(raw, p => p.description) ?? string.Empty,
                        GroupName = Inherited(raw, p => p.groupName),
                        BaseAddress = Naming.Number(raw.baseAddress ?? "0", $"peripheral {raw.name}: baseAddress"),
                        Interrupts = (raw.interrupt ?? Array.Empty<interruptType>())
                            .Select(i => new Interrupt
                            {
                                Name = i.name,
                                Description = i.description ?? string.Empty,
                                Value = Naming.Number(i.value, $"interrupt {i.name}: value"),
                            })
                            .ToList(),
                    };
                    built[raw] = peripheral;
                    result.Add(peripheral);
                }

                foreach (var raw in raws)
                {
                    var owner = RegisterOwner(raw);
                    if (ReferenceEquals(owner, raw))
                        built[raw].Registers = BuildRegisters(raw);
                    else
                        built[raw].Base = built[owner];
                }

                return result;
            }

            // <dim> on a peripheral: one instance per index, all sharing the
            // registers of the first.
            static IEnumerable<peripheralType> ExpandPeripheral(peripheralType raw)
            {
                if (string.IsNullOrEmpty(raw.dim) || raw.name == null || !raw.name.Contains("%s"))
                {
                    yield return raw;
                    yield break;
                }

                var count = (int)Naming.Number(raw.dim, $"peripheral {raw.name}: dim");
                var increment = string.IsNullOrEmpty(raw.dimIncrement) ? 0L : Naming.Number(raw.dimIncrement, $"peripheral {raw.name}: dimIncrement");
                var baseAddress = Naming.Number(raw.baseAddress ?? "0", $"peripheral {raw.name}: baseAddress");
                var indices = ResolveDimIndices(raw.dimIndex, count);
                var template = raw.name.Replace("[%s]", "%s");
                var first = template.Replace("%s", indices[0]);

                for (var i = 0; i < count; i++)
                {
                    var clone = Clone(raw);
                    clone.name = template.Replace("%s", indices[i]);
                    clone.baseAddress = Naming.Hex(baseAddress + i * increment);
                    clone.dim = null;
                    clone.dimIncrement = null;
                    clone.dimIndex = null;
                    if (i > 0)
                    {
                        clone.derivedFrom = first;
                        clone.registers = null;
                    }
                    yield return clone;
                }
            }

            static peripheralType Clone(peripheralType raw)
            {
                // The XML classes carry no clone method; a serializer round trip
                // would be heavier than copying the handful of members we read.
                return new peripheralType
                {
                    name = raw.name,
                    description = raw.description,
                    groupName = raw.groupName,
                    baseAddress = raw.baseAddress,
                    size = raw.size,
                    access = raw.access,
                    accessSpecified = raw.accessSpecified,
                    interrupt = raw.interrupt,
                    registers = raw.registers,
                    derivedFrom = raw.derivedFrom,
                    dim = raw.dim,
                    dimIncrement = raw.dimIncrement,
                    dimIndex = raw.dimIndex,
                };
            }

            peripheralType? Parent(peripheralType raw)
            {
                if (string.IsNullOrEmpty(raw.derivedFrom))
                    return null;
                if (!_rawByName.TryGetValue(raw.derivedFrom, out var parent))
                    throw new SvdException($"peripheral {raw.name}: derivedFrom '{raw.derivedFrom}' names no peripheral");
                return parent;
            }

            IEnumerable<peripheralType> Chain(peripheralType raw)
            {
                var seen = new HashSet<peripheralType>();
                for (var p = raw; p != null; p = Parent(p))
                {
                    if (!seen.Add(p))
                        throw new SvdException($"peripheral {raw.name}: derivedFrom forms a cycle");
                    yield return p;
                }
            }

            string? Inherited(peripheralType raw, Func<peripheralType, string?> get) =>
                Chain(raw).Select(get).FirstOrDefault(v => !string.IsNullOrEmpty(v));

            // The peripheral whose <registers> this one uses: itself when it has
            // some, otherwise the nearest one up its derivedFrom chain.
            peripheralType RegisterOwner(peripheralType raw) =>
                Chain(raw).FirstOrDefault(p => p.registers is { Length: > 0 }) ?? Chain(raw).Last();

            Defaults PeripheralDefaults(peripheralType raw)
            {
                var size = Inherited(raw, p => p.size) ?? _device.size;
                var accessOwner = Chain(raw).FirstOrDefault(p => p.accessSpecified);
                var access = accessOwner != null ? ToAccess(accessOwner.access)
                    : _device.accessSpecified ? ToAccess(_device.access)
                    : Access.ReadWrite;
                // No <size> anywhere: 32 bits matches every Cortex-M peripheral
                // seen in practice.
                return new Defaults(
                    string.IsNullOrEmpty(size) ? 32 : (int)Naming.Number(size, $"peripheral {raw.name}: size"),
                    access);
            }

            List<Register> BuildRegisters(peripheralType raw)
            {
                if (raw.registers == null)
                    return new List<Register>();

                var placed = new List<Placed>();
                Place(raw, raw.registers, string.Empty, 0, PeripheralDefaults(raw), placed);
                ResolveDerivedRegisters(raw, placed.Select(p => p.Raw).Distinct().ToList());

                var registers = new List<Register>();
                foreach (var p in placed)
                    registers.AddRange(ExpandRegister(raw, p));
                return registers;
            }

            // Walk registers and clusters. A cluster is flattened: its registers
            // become registers of the peripheral, named <cluster>_<register> and
            // offset by the cluster's own address; a <dim> cluster contributes
            // one such set per index.
            void Place(peripheralType peripheral, object[] items, string prefix, long baseOffset, Defaults defaults, List<Placed> placed)
            {
                foreach (var item in items)
                {
                    switch (item)
                    {
                        case registerType register:
                            placed.Add(new Placed(register, prefix, baseOffset, defaults));
                            break;

                        case clusterType cluster:
                            {
                                var what = $"cluster {peripheral.name}.{cluster.name}";
                                var inner = new Defaults(
                                    string.IsNullOrEmpty(cluster.size) ? defaults.Size : (int)Naming.Number(cluster.size, $"{what}: size"),
                                    cluster.accessSpecified ? ToAccess(cluster.access) : defaults.Access);
                                var offset = Naming.Number(cluster.addressOffset ?? "0", $"{what}: addressOffset");
                                var name = (cluster.name ?? string.Empty).Replace("[%s]", "%s");
                                if (cluster.Items == null)
                                    break;

                                if (string.IsNullOrEmpty(cluster.dim))
                                {
                                    Place(peripheral, cluster.Items, $"{prefix}{name.Replace("%s", string.Empty)}_", baseOffset + offset, inner, placed);
                                    break;
                                }

                                var count = (int)Naming.Number(cluster.dim, $"{what}: dim");
                                var increment = Naming.Number(cluster.dimIncrement ?? "0", $"{what}: dimIncrement");
                                var indices = ResolveDimIndices(cluster.dimIndex, count);
                                for (var i = 0; i < count; i++)
                                {
                                    var indexed = name.Contains("%s") ? name.Replace("%s", indices[i]) : name + indices[i];
                                    Place(peripheral, cluster.Items, $"{prefix}{indexed}_", baseOffset + offset + i * increment, inner, placed);
                                }
                                break;
                            }
                    }
                }
            }

            Dictionary<string, registerType> RegistersByName(peripheralType peripheral)
            {
                if (_registersByName.TryGetValue(peripheral, out var known))
                    return known;

                var byName = new Dictionary<string, registerType>(StringComparer.Ordinal);
                void Collect(object[]? items)
                {
                    foreach (var item in items ?? Array.Empty<object>())
                    {
                        if (item is registerType r && !string.IsNullOrEmpty(r.name))
                            byName.TryAdd(r.name, r);
                        else if (item is clusterType c)
                            Collect(c.Items);
                    }
                }
                Collect(RegisterOwner(peripheral).registers);
                _registersByName[peripheral] = byName;
                return byName;
            }

            registerType FindRegister(peripheralType peripheral, registerType derived)
            {
                var path = derived.derivedFrom;
                var local = RegistersByName(peripheral);
                if (local.TryGetValue(path, out var found))
                    return found;

                // Qualified forms: Peripheral.Register, or Cluster.Register
                // inside the same peripheral.
                var dot = path.LastIndexOf('.');
                if (dot > 0)
                {
                    var last = path[(dot + 1)..];
                    var first = path[..path.IndexOf('.')];
                    if (_rawByName.TryGetValue(first, out var other) && RegistersByName(other).TryGetValue(last, out found))
                        return found;
                    if (local.TryGetValue(last, out found))
                        return found;
                }

                throw new SvdException($"register {peripheral.name}.{derived.name}: derivedFrom '{path}' names no register");
            }

            // Apply CMSIS-SVD register-level derivedFrom inheritance. Some vendor
            // SVDs (Atmel SAMD21 PMUX1_%s derived from PMUX0_%s) leave size,
            // fields, access, etc. implicit on the derived register.
            void ResolveDerivedRegisters(peripheralType peripheral, List<registerType> registers)
            {
                bool changed;
                var rounds = 0;
                do
                {
                    changed = false;
                    foreach (var r in registers)
                    {
                        if (string.IsNullOrEmpty(r.derivedFrom)) continue;
                        var parent = FindRegister(peripheral, r);

                        if (string.IsNullOrEmpty(r.size) && !string.IsNullOrEmpty(parent.size))
                        { r.size = parent.size; changed = true; }
                        if ((r.fields == null || r.fields.Length == 0) && parent.fields != null && parent.fields.Length > 0)
                        { r.fields = parent.fields; changed = true; }
                        if (!r.accessSpecified && parent.accessSpecified)
                        { r.access = parent.access; r.accessSpecified = true; changed = true; }
                        if (string.IsNullOrEmpty(r.resetValue) && !string.IsNullOrEmpty(parent.resetValue))
                        { r.resetValue = parent.resetValue; changed = true; }
                        if (string.IsNullOrEmpty(r.resetMask) && !string.IsNullOrEmpty(parent.resetMask))
                        { r.resetMask = parent.resetMask; changed = true; }
                        if (string.IsNullOrEmpty(r.description) && !string.IsNullOrEmpty(parent.description))
                        { r.description = parent.description; changed = true; }
                    }
                } while (changed && ++rounds < 64);
            }

            // Expand a CMSIS-SVD register array (<dim>/<dimIncrement>/<dimIndex>
            // with `%s` in the name) into one register per index. The spec
            // accepts both `name%s` (Atmel/Microchip) and the `name[%s]`
            // array-notation form (Nordic); the brackets are not part of the
            // C++ identifier.
            static IEnumerable<Register> ExpandRegister(peripheralType peripheral, Placed placed)
            {
                var raw = placed.Raw;
                if (string.IsNullOrWhiteSpace(raw.name))
                    throw new SvdException($"peripheral {peripheral.name}: a register has no name");

                var what = $"register {peripheral.name}.{placed.Prefix}{raw.name}";
                var size = string.IsNullOrEmpty(raw.size) ? placed.Defaults.Size : (int)Naming.Number(raw.size, $"{what}: size");
                if (size != 8 && size != 16 && size != 32 && size != 64)
                    throw new SvdException($"{what}: a size of {size} bits is not supported (8, 16, 32 or 64)");

                var declared = raw.accessSpecified ? ToAccess(raw.access) : placed.Defaults.Access;
                var offset = placed.BaseOffset + Naming.Number(raw.addressOffset ?? "0", $"{what}: addressOffset");
                var fields = (raw.fields ?? Array.Empty<fieldType>()).SelectMany(f => ExpandField(f, what, size, declared)).ToList();

                // Vendors such as ST state the access on the fields and leave
                // the register read-write: a status register made of read-only
                // flags is read-only all the same.
                var access = fields.Count == 0 ? declared
                    : fields.All(f => f.Access == Access.ReadOnly) ? Access.ReadOnly
                    : fields.All(f => f.Access == Access.WriteOnly) ? Access.WriteOnly
                    : Access.ReadWrite;

                Register Make(string name, long at) => new()
                {
                    Name = placed.Prefix + name,
                    Description = raw.description ?? string.Empty,
                    Offset = at,
                    Size = size,
                    Access = access,
                    ResetText = string.IsNullOrWhiteSpace(raw.resetValue) ? null : raw.resetValue,
                    Fields = fields,
                };

                if (string.IsNullOrEmpty(raw.dim) || !raw.name.Contains("%s"))
                {
                    yield return Make(raw.name, offset);
                    yield break;
                }

                var template = raw.name.Replace("[%s]", "%s");
                var count = (int)Naming.Number(raw.dim, $"{what}: dim");
                var increment = string.IsNullOrEmpty(raw.dimIncrement) ? 4L : Naming.Number(raw.dimIncrement, $"{what}: dimIncrement");
                var indices = ResolveDimIndices(raw.dimIndex, count);

                for (var i = 0; i < count; i++)
                    yield return Make(template.Replace("%s", indices[i]), offset + i * increment);
            }

            static IEnumerable<Field> ExpandField(fieldType raw, string register, int registerSize, Access registerAccess)
            {
                if (string.IsNullOrWhiteSpace(raw.name))
                    throw new SvdException($"{register}: a field has no name");

                var what = $"field {raw.name} of {register}";
                var (width, offset) = WidthOffset(raw, what);

                Field Make(string name, int at)
                {
                    if (width < 1 || at < 0 || at + width > registerSize)
                        throw new SvdException($"{what}: bits [{at + width - 1}:{at}] do not fit a {registerSize}-bit register");
                    return new Field
                    {
                        Name = name,
                        Description = raw.description ?? string.Empty,
                        Offset = at,
                        Width = width,
                        Access = raw.accessSpecified ? ToAccess(raw.access) : registerAccess,
                    };
                }

                if (string.IsNullOrEmpty(raw.dim) || !raw.name.Contains("%s"))
                {
                    yield return Make(raw.name, offset);
                    yield break;
                }

                var template = raw.name.Replace("[%s]", "%s");
                var count = (int)Naming.Number(raw.dim, $"{what}: dim");
                var increment = string.IsNullOrEmpty(raw.dimIncrement) ? width : (int)Naming.Number(raw.dimIncrement, $"{what}: dimIncrement");
                var indices = ResolveDimIndices(raw.dimIndex, count);

                for (var i = 0; i < count; i++)
                    yield return Make(template.Replace("%s", indices[i]), offset + i * increment);
            }

            static (int Width, int Offset) WidthOffset(fieldType raw, string what)
            {
                if (raw.ItemsElementName == null || raw.Items == null)
                    throw new SvdException($"{what}: no bit position (bitOffset/bitWidth, lsb/msb or bitRange)");

                int? bitOffset = null, bitWidth = null, lsb = null, msb = null;

                for (var i = 0; i < raw.ItemsElementName.Length; ++i)
                {
                    var value = raw.Items[i];
                    switch (raw.ItemsElementName[i])
                    {
                        case ItemsChoiceType.bitOffset:
                            bitOffset = (int)Naming.Number(value, $"{what}: bitOffset");
                            break;
                        case ItemsChoiceType.bitWidth:
                            bitWidth = (int)Naming.Number(value, $"{what}: bitWidth");
                            break;
                        case ItemsChoiceType.lsb:
                            lsb = (int)Naming.Number(value, $"{what}: lsb");
                            break;
                        case ItemsChoiceType.msb:
                            msb = (int)Naming.Number(value, $"{what}: msb");
                            break;
                        case ItemsChoiceType.bitRange:
                            {
                                // CMSIS-SVD writes <bitRange>[msb:lsb]</bitRange>
                                // with surrounding square brackets (RP2040 uses
                                // this form).
                                var split = value.Trim().Trim('[', ']').Split(':');
                                if (split.Length != 2)
                                    throw new SvdException($"{what}: bitRange '{value}' is not [msb:lsb]");
                                msb = (int)Naming.Number(split[0], $"{what}: bitRange");
                                lsb = (int)Naming.Number(split[1], $"{what}: bitRange");
                                break;
                            }
                    }
                }

                // <lsb>/<msb> and <bitRange> are inclusive bit positions: the
                // width is msb - lsb + 1.
                if (lsb != null && msb != null)
                    return (msb.Value - lsb.Value + 1, lsb.Value);
                if (bitOffset != null)
                    return (bitWidth ?? 1, bitOffset.Value);

                throw new SvdException($"{what}: incomplete bit position");
            }

            static string[] ResolveDimIndices(string? raw, int count)
            {
                if (count < 1)
                    throw new SvdException($"a dim of {count} is not valid");

                if (string.IsNullOrWhiteSpace(raw))
                    return Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();

                if (raw.Contains(','))
                {
                    var listed = raw.Split(',').Select(s => s.Trim()).ToArray();
                    if (listed.Length != count)
                        throw new SvdException($"dimIndex '{raw}' lists {listed.Length} names for a dim of {count}");
                    return listed;
                }

                // Range form: "0-3", or "A-D" with single letters.
                var dash = raw.IndexOf('-');
                if (dash > 0)
                {
                    var from = raw[..dash].Trim();
                    var to = raw[(dash + 1)..].Trim();
                    if (int.TryParse(from, out var lo) && int.TryParse(to, out var hi) && hi - lo + 1 == count)
                        return Enumerable.Range(lo, count).Select(i => i.ToString()).ToArray();
                    if (from.Length == 1 && to.Length == 1 && char.IsAsciiLetter(from[0]) && to[0] - from[0] + 1 == count)
                        return Enumerable.Range(from[0], count).Select(c => ((char)c).ToString()).ToArray();
                }

                return Enumerable.Range(0, count).Select(i => $"{raw}{i}").ToArray();
            }
        }
    }
}
