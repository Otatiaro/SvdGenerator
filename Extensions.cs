using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using static StringExtensions;


public partial class fieldType
{
    public override string ToString() => name;

    public string ClassDefinition(string? regClassName)
    {
        var sb = new StringBuilder();

        var (width, offset) = WidthOffset;


        sb.AppendLine($"/**");
        sb.AppendLine($" * {description.ToOneLine()}");
        sb.AppendLine($" */");
        sb.AppendLine($"class {this.ClassName()} {{");
        sb.AppendLine($"public:");

        sb.AppendLine($"\tstatic constexpr {OffsetType} offset = {offset};");
        sb.AppendLine($"\tstatic constexpr {WidthType} width = {width};");

        if (width != 1)
        {
            sb.AppendLine($"\tstatic constexpr {width.ToInt()} range = static_cast<{width.ToInt()}>({width.Range()});");
            sb.AppendLine($"\tstatic constexpr {(width + offset).ToInt()} mask = static_cast<{(width + offset).ToInt()}>(static_cast<{(width + offset).ToType()}>(range) << offset);");
        }
        else
        {
            sb.AppendLine($"\tstatic constexpr {(width + offset).ToInt()} mask = static_cast<{(width + offset).ToInt()}>(1ULL << offset);");
        }

        sb.AppendLine();
        var rangeCheck = width.ExactType() ? "" : " & range";
        var defaultValue = width == 1 ? " = true" : "";
        sb.AppendLine($"\tconstexpr {this.ClassName()}({width.ToType()} value{defaultValue}) : value_(value{rangeCheck}) {{}}");
        sb.AppendLine($"\tconstexpr operator {width.ToType()}() const {{return value_;}}");

        if (regClassName != null)
        {
            sb.AppendLine(width == 1
                ? $"\tconstexpr operator {regClassName}() const {{return value_ ? mask : 0;}}"
                : $"\tconstexpr operator {regClassName}() const {{return static_cast<{(width + offset).ToType()}>(static_cast<{(width + offset).ToType()}>(value_) << offset);}}");

            sb.AppendLine($"\tconstexpr operator opsy::utility::clear_set<{regClassName}>() const {{return opsy::utility::clear_set<{regClassName}>(mask, *this);}}");
            sb.AppendLine($"\tconstexpr auto operator|({regClassName} other) const -> {regClassName} {{ return static_cast<{regClassName}>(*this) | other.value_;}}");
            sb.AppendLine($"\tconstexpr auto operator||(opsy::utility::clear_set<{regClassName}> other) const -> opsy::utility::clear_set<{regClassName}> {{return opsy::utility::clear_set<{regClassName}>({regClassName}(mask) | other.clear(), *this | other.set()); }}");
        }

        sb.AppendLine();
        sb.AppendLine("private:");
        sb.AppendLine($"\t {width.ToType()} value_;");

        sb.AppendLine("};");
        return sb.ToString();
    }

    [XmlIgnore]
    public (int, int) WidthOffset
    {
        get
        {
            var width = 0;
            var offset = 0;

            if (ItemsElementName == null)
                throw new Exception();

            for (var i = 0; i < ItemsElementName.Length; ++i)
            {
                var type = ItemsElementName[i];
                var value = Items[i];

                switch (type)
                {
                    case ItemsChoiceType.bitOffset:
                        offset = int.Parse(value);
                        break;
                    case ItemsChoiceType.bitWidth:
                        width = int.Parse(value);
                        break;
                    case ItemsChoiceType.lsb:
                        // CMSIS-SVD <lsb>/<msb> are inclusive bit positions,
                        // so the width is msb - lsb + 1. The +1 used to be
                        // missing, which made 1-bit fields like [5:5] come
                        // out as width=0 -> "0b" literal -> garbage.
                        offset = int.Parse(value);
                        width = width - offset + 1;
                        break;
                    case ItemsChoiceType.msb:
                        width += int.Parse(value);
                        break;
                    case ItemsChoiceType.bitRange:
                        {
                            // CMSIS-SVD writes <bitRange>[msb:lsb]</bitRange>
                            // with surrounding square brackets; strip them
                            // before splitting (RP2040 uses this form).
                            // msb and lsb are inclusive: width = msb-lsb+1.
                            var split = value.Trim('[', ']').Split(':');
                            if (split.Length != 2)
                                throw new Exception();
                            offset = int.Parse(split[1]);
                            width = int.Parse(split[0]) - offset + 1;
                            break;
                        }
                    default:
                        throw new Exception();
                }
            }

            return (width, offset);
        }
    }

    public bool IsEquivalent(fieldType other) =>
        other.name == name &&
        other.WidthOffset == WidthOffset &&
        other.accessSpecified == accessSpecified &&
        (!other.accessSpecified || (other.access == access));


}

public partial class registerType
{
    public override string ToString() => name;

    public string ClassDefinition(string? overrideName = null, string? overrideDescription = null)
    {
        overrideName ??= this.ClassName();
        overrideDescription ??= description;

        var sb = new StringBuilder();

        if (!BitSize.ExactType())
            throw new Exception();

        sb.AppendLine($"/**");
        sb.AppendLine($" * {overrideDescription.ToOneLine()}");
        sb.AppendLine($" */");
        sb.AppendLine($"class {overrideName} {{");
        sb.AppendLine($"public:");

        if (fields != null)
        {
            foreach (var field in fields)
            {
                sb.AppendLine();
                sb.Append(field.ClassDefinition(overrideName));
            }

            sb.AppendLine();

            foreach (var field in fields)
            {
                var (width, offset) = field.WidthOffset;
                if (width == 1)
                    sb.AppendLine($"\t[[nodiscard]] constexpr auto {field.FieldName()}() const -> {field.ClassName()} {{return {field.ClassName()}((value_ & {field.ClassName()}::mask) != 0);}}");
                else
                    sb.AppendLine($"\t[[nodiscard]] constexpr auto {field.FieldName()}() const -> {field.ClassName()} {{return {field.ClassName()}(static_cast<{width.ToType()}>(value_ >> {field.ClassName()}::offset));}}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"\tconstexpr {overrideName}({BitSize.ToType()} value) : value_(value) {{}}");
        sb.AppendLine($"\tconstexpr auto operator |({overrideName} other) const -> {overrideName} {{ return value_ | other.value_; }}");
        sb.AppendLine($"\tconstexpr auto operator ~() const -> {overrideName} {{ return ~value_; }}");
        sb.AppendLine($"\t[[nodiscard]] constexpr auto value() const {{ return value_; }}");

        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(resetValue))
            sb.AppendLine($"\tstatic constexpr {BitSize.ToType()} reset_value = {resetValue.ToValue().ToBinary()}; // {resetValue.ToValue()} {resetValue.ToValue().ToHex()}");

        sb.AppendLine();
        sb.AppendLine("private:");
        sb.AppendLine($"\t{BitSize.ToType()} value_;");

        sb.AppendLine("};");
        return sb.ToString();
    }

    public bool IsEquivalent(registerType other) => other.fields != null &&
                                                    fields != null &&
                                                    other.fields.Length == fields.Length &&
                                                    other.fields.Length != 0 &&
                                                    other.fields.All(f => fields.Any(f2 => f2.IsEquivalent(f))) &&
                                                    other.BitSize == BitSize &&
                                                    other.resetValue == resetValue;

    [XmlIgnore]
    public long Offset => addressOffset.ToValue();

    [XmlIgnore]
    // CMSIS-SVD spec lets a register inherit <size> from its peripheral or
    // device — the deserializer here does not propagate that, so when the
    // tag is absent we fall back to 32, which matches every ARM Cortex-M
    // peripheral we have seen in practice.
    public int BitSize => string.IsNullOrEmpty(size) ? 32 : (int)size.ToValue();

    // Expand a CMSIS-SVD register array (<dim>/<dimIncrement>/<dimIndex>
    // with `%s` in the name) into one registerType per index. SVDs from
    // Atmel/Microchip and Nordic use this heavily; STM32 SVDs do not, so
    // the path was untested before the multi-vendor smoke matrix.
    //
    // CMSIS-SVD spec accepts both `name%s` (Atmel/Microchip) and the
    // `name[%s]` array-notation form (Nordic). The brackets are not
    // part of the C++ identifier; strip them so the cloned names are
    // valid identifiers in either case.
    public IEnumerable<registerType> Expand()
    {
        if (string.IsNullOrEmpty(dim) || !name.Contains("%s"))
        {
            yield return this;
            yield break;
        }

        var template = name.Replace("[%s]", "%s");
        var count = (int)dim.ToValue();
        var increment = string.IsNullOrEmpty(dimIncrement) ? 4L : dimIncrement.ToValue();
        var baseOffset = addressOffset.ToValue();
        var indices = ResolveDimIndices(dimIndex, count);

        for (var i = 0; i < count; i++)
        {
            var clone = (registerType)MemberwiseClone();
            clone.name = template.Replace("%s", indices[i]);
            clone.addressOffset = (baseOffset + i * increment).ToHex();
            clone.dim = null;
            clone.dimIncrement = null;
            clone.dimIndex = null;
            yield return clone;
        }
    }

    static string[] ResolveDimIndices(string? raw, int count)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();

        if (raw.Contains(','))
            return raw.Split(',').Select(s => s.Trim()).ToArray();

        // Range form like "0-3" or "A-D". Numeric only is what we have seen
        // in practice; if both ends parse as int, expand the range.
        var dash = raw.IndexOf('-');
        if (dash > 0 && int.TryParse(raw[..dash], out var lo) && int.TryParse(raw[(dash + 1)..], out var hi))
            return Enumerable.Range(lo, hi - lo + 1).Select(i => i.ToString()).ToArray();

        return Enumerable.Range(0, count).Select(i => $"{raw}{i}").ToArray();
    }
}

public partial class peripheralType
{
    // Resolve register-level <register derivedFrom="..."> inheritance
    // before generation. CMSIS-SVD lets a register declare itself as
    // derived from another in the same peripheral and inherit unset
    // attributes (size, fields, access, resetValue, ...) from that
    // parent. The auto-generated XmlSerializer reader stores
    // derivedFrom as a plain string; we apply the inheritance here so
    // the rest of the generator can treat every register uniformly.
    public void ResolveDerivedFrom()
    {
        if (registers == null) return;
        var byName = registers.OfType<registerType>()
            .Where(r => !string.IsNullOrEmpty(r.name))
            .GroupBy(r => r.name)
            .ToDictionary(g => g.Key, g => g.First());

        bool changed;
        do
        {
            changed = false;
            foreach (var r in registers.OfType<registerType>())
            {
                if (string.IsNullOrEmpty(r.derivedFrom)) continue;
                if (!byName.TryGetValue(r.derivedFrom, out var parent)) continue;

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
            }
        } while (changed);
    }

    public string ClassDefinition(string? className = null)
    {
        if (className == null)
            className = this.ClassName();

        var sb = new StringBuilder();
        var classNames = new Dictionary<registerType, string>();
        // Each register's offsetof access path on the peripheral class:
        // either a bare field name, or "_lane_N.field" when the register
        // sits inside a named lane struct.
        var accessPath = new Dictionary<registerType, string>();

        sb.AppendLine($"/**");
        sb.AppendLine($" * {description.ToOneLine()}");
        sb.AppendLine($" */");
        sb.AppendLine($"class {className} {{");
        sb.AppendLine($"public:");

        // Pre-expand <dim>-arrayed registers (CMSIS-SVD `name%s` form) and
        // sort them. The same expanded list feeds register-class
        // equivalence detection, layout walking and the offsetof
        // static_asserts so all three see identical names.
        var expanded = registers?
            .OfType<registerType>()
            .SelectMany(r => r.Expand())
            .OrderBy(r => r.addressOffset.ToValue())
            .ThenByDescending(r => r.BitSize)
            .ToList();

        if (expanded != null)
        {
            var dict = new Dictionary<registerType, List<registerType>>();
            foreach (var type in expanded)
            {
                var equ = dict.Keys.FirstOrDefault(k => k.IsEquivalent(type));
                if (equ != null) dict[equ].Add(type);
                else dict.Add(type, new List<registerType> { type });
            }

            foreach (var values in dict.Values)
            {
                var name = values.Select(r => r.ClassName()).CommonWithReplace("x");

                if (name is null)
                    name = values.First().ClassName();
                else
                {
                    var i = 2;
                    while (classNames.ContainsValue(name))
                    {
                        var replacement = string.Empty;
                        for (var j = 0; j < i; j++)
                            replacement += "x";

                        name = values.Select(r => r.ClassName()).CommonWithReplace(replacement)!;
                    }
                }

                var description = values.Select(r => r.description).CommonWithReplace("X") ?? values.First().description;


                sb.AppendLine();
                sb.Append(values.First().ClassDefinition(name, description));

                foreach (var value in values)
                    classNames.Add(value, name);
            }


            sb.AppendLine();

            // Walk the registers as a sequence of "footprint groups": each
            // group is a maximal run of registers whose byte spans
            // transitively overlap. Single-register groups lay out flat.
            // Multi-register groups become an anonymous union of lanes
            // (interval coloring). A lane that is a single register at
            // the group's offset rides directly inside the anonymous
            // union (non-POD member is fine in an anonymous union by
            // standard C++); any other lane has to live in a NAMED
            // struct field (`struct { ... } _lane_N;`) because GCC's
            // anonymous-struct extension forbids non-POD members and
            // opsy::utility::memory holds a std::atomic, so it is not
            // POD. Trade-off: byte/half aliases get accessed as
            // `peripheral._lane_N.fooN` instead of `peripheral.fooN`.
            long currentOffset = 0;
            var laneId = 0;

            foreach (var group in BuildFootprintGroups(expanded))
            {
                var groupStart = group[0].addressOffset.ToValue();
                var groupEnd = group.Max(r => r.addressOffset.ToValue() + r.BitSize / 8);

                var leadingPadding = groupStart - currentOffset;
                if (leadingPadding > 0)
                    sb.AppendLine($"\topsy::utility::padding<{leadingPadding}> _p{currentOffset};");

                var lanes = PartitionLanes(group);

                if (lanes.Count == 1)
                {
                    foreach (var r in lanes[0]) accessPath[r] = r.FieldName();
                    EmitLaneMembers(sb, lanes[0], groupStart, classNames, "\t");
                }
                else
                {
                    sb.AppendLine("\tunion {");
                    foreach (var lane in lanes)
                    {
                        var bare = lane.Count == 1 && lane[0].addressOffset.ToValue() == groupStart;
                        if (bare)
                        {
                            accessPath[lane[0]] = lane[0].FieldName();
                            EmitLaneMembers(sb, lane, groupStart, classNames, "\t\t");
                        }
                        else
                        {
                            var laneName = $"_lane_{laneId++}";
                            foreach (var r in lane) accessPath[r] = $"{laneName}.{r.FieldName()}";
                            sb.AppendLine("\t\tstruct {");
                            EmitLaneMembers(sb, lane, groupStart, classNames, "\t\t\t");
                            sb.AppendLine($"\t\t}} {laneName};");
                        }
                    }
                    sb.AppendLine("\t};");
                }

                currentOffset = groupEnd;
            }
        }

        sb.AppendLine("};");


        if (expanded != null)
        {
            sb.AppendLine();
            sb.AppendLine($"static_assert(std::is_standard_layout_v<{className}>);");
            foreach (var register in expanded)
                sb.AppendLine($"static_assert(offsetof({className}, {accessPath[register]}) == {register.Offset});");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    // Build maximal runs of registers whose byte spans transitively overlap.
    // Input: registers sorted by offset asc, then size desc.
    // Output: groups in offset-asc order; within a group, the same sort.
    static List<List<registerType>> BuildFootprintGroups(List<registerType> sorted)
    {
        var groups = new List<List<registerType>>();
        long currentEnd = -1;
        foreach (var r in sorted)
        {
            var start = r.addressOffset.ToValue();
            var end = start + r.BitSize / 8;
            if (groups.Count == 0 || start >= currentEnd)
            {
                groups.Add(new List<registerType> { r });
                currentEnd = end;
            }
            else
            {
                groups[^1].Add(r);
                if (end > currentEnd) currentEnd = end;
            }
        }
        return groups;
    }

    // Greedy interval coloring on a single footprint group. With the input
    // sorted (offset asc, size desc) the largest register lands in lane 0,
    // halfword aliases collapse into lane 1, byte aliases into lane 2.
    static List<List<registerType>> PartitionLanes(List<registerType> group)
    {
        var lanes = new List<List<registerType>>();
        var laneEnds = new List<long>();
        foreach (var r in group)
        {
            var start = r.addressOffset.ToValue();
            var end = start + r.BitSize / 8;
            var idx = -1;
            for (var i = 0; i < laneEnds.Count; i++)
            {
                if (laneEnds[i] <= start) { idx = i; break; }
            }
            if (idx == -1)
            {
                idx = lanes.Count;
                lanes.Add(new List<registerType>());
                laneEnds.Add(0);
            }
            lanes[idx].Add(r);
            laneEnds[idx] = end;
        }
        return lanes;
    }

    // Emit a sequence of registers belonging to one lane, inserting padding
    // for any internal gap between consecutive registers. Padding is named
    // by the byte offset where it starts; intra-lane paddings are scoped to
    // the lane's named struct so two lanes with padding at the same offset
    // do not collide at peripheral scope.
    static void EmitLaneMembers(
        StringBuilder sb, List<registerType> lane, long laneStart,
        Dictionary<registerType, string> classNames, string indent)
    {
        var cursor = laneStart;
        foreach (var r in lane)
        {
            var off = r.addressOffset.ToValue();
            if (off > cursor)
                sb.AppendLine($"{indent}opsy::utility::padding<{off - cursor}> _p{cursor};");

            var template = (r.accessSpecified ? r.access : accessType.readwrite) switch
            {
                accessType.@readonly => "opsy::utility::read_only_memory",
                accessType.writeonly => "opsy::utility::write_only_memory",
                accessType.writeOnce => "opsy::utility::write_only_memory",
                _ => "opsy::utility::memory",
            };
            sb.AppendLine($"{indent}{template}<{r.BitSize.ToType()},{classNames[r]}> {r.FieldName()};");

            cursor = off + r.BitSize / 8;
        }
    }
}
