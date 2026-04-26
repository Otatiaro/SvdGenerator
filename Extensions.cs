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
                        offset = int.Parse(value);
                        width -= offset;
                        break;
                    case ItemsChoiceType.msb:
                        width += int.Parse(value);
                        break;
                    case ItemsChoiceType.bitRange:
                        {
                            // CMSIS-SVD writes <bitRange>[msb:lsb]</bitRange>
                            // with surrounding square brackets; strip them
                            // before splitting (RP2040 uses this form).
                            var split = value.Trim('[', ']').Split(':');
                            if (split.Length != 2)
                                throw new Exception();
                            offset = int.Parse(split[1]);
                            width = int.Parse(split[0]) - offset;
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
}

public partial class peripheralType
{
    public string ClassDefinition(string? className = null)
    {
        if (className == null)
            className = this.ClassName();

        var sb = new StringBuilder();
        var classNames = new Dictionary<registerType, string>();

        sb.AppendLine($"/**");
        sb.AppendLine($" * {description.ToOneLine()}");
        sb.AppendLine($" */");
        sb.AppendLine($"class {className} {{");
        sb.AppendLine($"public:");

        if (registers != null)
        {
            var dict = new Dictionary<registerType, List<registerType>>();
            foreach (var type in registers.OfType<registerType>())
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

            long currentOffset = 0;

            var request = registers
                .OfType<registerType>()
                .GroupBy(r => r.addressOffset.ToValue())
                .OrderBy(r => r.Key);


            foreach (var group in request)
            {
                if (group.Count() > 1)
                    sb.AppendLine("\tunion {");

                var padding = group.Key - currentOffset;

                if (padding < 0)
                    throw new Exception();

                if (padding != 0)
                    sb.AppendLine($"\topsy::utility::padding<{padding}> _p{currentOffset};");

                var size = 0;

                foreach (var register in group)
                {
                    switch (register.accessSpecified ? register.access : accessType.readwrite)
                    {
                        case accessType.@readonly:
                            sb.AppendLine($"\topsy::utility::read_only_memory<{register.BitSize.ToType()},{classNames[register]}> {register.FieldName()};");
                            break;
                        case accessType.writeonly:
                            sb.AppendLine($"\topsy::utility::write_only_memory<{register.BitSize.ToType()},{classNames[register]}> {register.FieldName()};");
                            break;
                        default:
                        case accessType.readwrite:
                            sb.AppendLine($"\topsy::utility::memory<{register.BitSize.ToType()},{classNames[register]}> {register.FieldName()};");
                            break;
                        case accessType.writeOnce:
                            sb.AppendLine($"\topsy::utility::write_only_memory<{register.BitSize.ToType()},{classNames[register]}> {register.FieldName()};");
                            break;
                        case accessType.readwriteOnce:
                            sb.AppendLine($"\topsy::utility::memory<{register.BitSize.ToType()},{classNames[register]}> {register.FieldName()};");
                            break;
                    }

                    if (register.BitSize > size)
                        size = register.BitSize;
                }

                if (group.Count() > 1)
                    sb.AppendLine("\t};");


                currentOffset += (size / 8) + padding;
            }
        }

        sb.AppendLine("};");


        if (registers != null)
        {
            sb.AppendLine();
            sb.AppendLine($"static_assert(std::is_standard_layout_v<{className}>);");
            foreach (var register in registers.OfType<registerType>())
            {
                var offset = register.addressOffset.ToValue();
                sb.AppendLine($"static_assert(offsetof({className}, {register.FieldName()}) == {register.Offset});");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
