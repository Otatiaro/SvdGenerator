using System;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using static StringExtensions;


public partial class fieldType
{
    public override string ToString()
    {
        var sb = new StringBuilder();

        var (width, offset) = WidthOffset;


        sb.AppendLine($"/**");
        sb.AppendLine($" * {description.ToOneLine()}");
        sb.AppendLine($" */");
        sb.AppendLine($"class {this.ClassName()} {{");
        sb.AppendLine($"public:");

        sb.AppendLine($"\tstatic constexpr {OffsetType} Offset = {offset};");
        sb.AppendLine($"\tstatic constexpr {WidthType} Width = {width};");

        if (width != 1)
        {
            sb.AppendLine($"\tstatic constexpr {width.ToInt()} Range = static_cast<{width.ToInt()}>({width.Range()});");
            sb.AppendLine($"\tstatic constexpr {(width + offset).ToInt()} Mask = static_cast<{(width + offset).ToInt()}>(static_cast<{(width + offset).ToType()}>(Range) << Offset);");
        }
        else
        {
            sb.AppendLine($"\tstatic constexpr {(width + offset).ToInt()} Mask = static_cast<{(width + offset).ToInt()}>(1ULL << Offset);");
        }

        sb.AppendLine();
        var rangeCheck = width.ExactType() ? "" : " & Range";
        var defaultValue = width == 1 ? " = true" : "";
        sb.AppendLine($"\tconstexpr {this.ClassName()}({width.ToType()} value{defaultValue}) : m_value(value{rangeCheck}) {{}}");
        sb.AppendLine($"\tconstexpr operator {width.ToType()}() const {{return m_value;}}");

        if (reg != null)
        {
            sb.AppendLine(width == 1
                ? $"\tconstexpr operator {reg.ClassName()}() const {{return m_value ? Mask : 0;}}"
                : $"\tconstexpr operator {reg.ClassName()}() const {{return static_cast<{(width + offset).ToType()}>(static_cast<{(width + offset).ToType()}>(m_value) << Offset);}}");

            sb.AppendLine($"\tconstexpr operator ClearSet<{reg.ClassName()}>() const {{return ClearSet<{reg.ClassName()}>(Mask, *this);}}");
            sb.AppendLine($"\tconstexpr auto operator|({reg.ClassName()} other) const -> {reg.ClassName()} {{ return static_cast<{reg.ClassName()}>(*this) | other.m_value;}}");
            sb.AppendLine($"\tconstexpr auto operator||(ClearSet<{reg.ClassName()}> other) const -> ClearSet<{reg.ClassName()}> {{return ClearSet<{reg.ClassName()}>({reg.ClassName()}(Mask) | other.clear(), *this | other.set()); }}");
        }

        sb.AppendLine();
        sb.AppendLine("private:");
        sb.AppendLine($"\t {width.ToType()} m_value;");

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
                        var split = value.Split(':');
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

    [XmlIgnore]
    public registerType reg { get; set; } = null;

}

public partial class registerType
{
    public override string ToString()
    {
        var sb = new StringBuilder();

        if(!BitSize.ExactType())
            throw new Exception();

        sb.AppendLine($"/**");
        sb.AppendLine($" * {description.ToOneLine()}");
        sb.AppendLine($" */");
        sb.AppendLine($"class {this.ClassName()} {{");
        sb.AppendLine($"public:");

        if (fields != null)
        {
            foreach (var field in fields)
            {
                field.reg = this;
                sb.AppendLine();
                sb.Append(field);
            }

            sb.AppendLine();

            foreach (var field in fields)
            {
                var (width, offset) = field.WidthOffset;
                if(width == 1)
                    sb.AppendLine($"\t[[nodiscard]] constexpr auto {field.FieldName()}() const -> {field.ClassName()} {{return {field.ClassName()}((m_value & {field.ClassName()}::Mask) != 0);}}");
                else
                    sb.AppendLine($"\t[[nodiscard]] constexpr auto {field.FieldName()}() const -> {field.ClassName()} {{return {field.ClassName()}(static_cast<{width.ToType()}>(m_value >> {field.ClassName()}::Offset));}}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"\tconstexpr {this.ClassName()}({BitSize.ToType()} value) : m_value(value) {{}}");
        sb.AppendLine($"\tconstexpr auto operator |({this.ClassName()} other) const -> {this.ClassName()} {{ return m_value | other.m_value; }}");
        sb.AppendLine($"\tconstexpr auto operator ~() const -> {this.ClassName()} {{ return ~m_value; }}");
        sb.AppendLine($"\t[[nodiscard]] constexpr auto value() const {{ return m_value; }}");

        sb.AppendLine();
        sb.AppendLine($"\tstatic constexpr std::size_t Offset = {addressOffset.ToValue()};");

        if (!string.IsNullOrWhiteSpace(resetValue))
            sb.AppendLine($"\tstatic constexpr {BitSize.ToType()} ResetValue = {resetValue.ToValue().ToBinary()}; // {resetValue.ToValue()} {resetValue.ToValue().ToHex()}");

        sb.AppendLine();
        sb.AppendLine("private:");
        sb.AppendLine($"\t{BitSize.ToType()} m_value;");

        sb.AppendLine("};");
        return sb.ToString();
    }

    [XmlIgnore]
    public int BitSize => (int)size.ToValue();
}

public partial class peripheralType
{
    public string ClassDefinition(string className = null)
    {
        if (className == null)
            className = this.ClassName();

        var sb = new StringBuilder();

        sb.AppendLine($"/**");
        sb.AppendLine($" * {description.ToOneLine()}");
        sb.AppendLine($" */");
        sb.AppendLine($"class {className} {{");
        sb.AppendLine($"public:");

        if (registers != null)
        {
            foreach (var register in registers)
                sb.Append(register);

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

                if(padding < 0)
                    throw new Exception();

                if (padding != 0)
                    sb.AppendLine($"\tPadding<{padding}> _p{currentOffset};");

                var size = 0;

                foreach (var register in group)
                {
                    switch (register.accessSpecified ? register.access : accessType.readwrite)
                    {
                        case accessType.@readonly:
                            sb.AppendLine($"\tReadOnlyMemory<{register.BitSize.ToType()},{register.ClassName()}> {register.FieldName()};");
                            break;
                        case accessType.writeonly:
                            sb.AppendLine($"\tWriteOnlyMemory<{register.BitSize.ToType()},{register.ClassName()}> {register.FieldName()};");
                            break;
                        default:
                        case accessType.readwrite:
                            sb.AppendLine($"\tMemory<{register.BitSize.ToType()},{register.ClassName()}> {register.FieldName()};");
                            break;
                        case accessType.writeOnce:
                            sb.AppendLine($"\tWriteOnlyMemory<{register.BitSize.ToType()},{register.ClassName()}> {register.FieldName()};");
                            break;
                        case accessType.readwriteOnce:
                            sb.AppendLine($"\tMemory<{register.BitSize.ToType()},{register.ClassName()}> {register.FieldName()};");
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
                sb.AppendLine($"static_assert(offsetof({className}, {register.FieldName()}) == {className}::{register.ClassName()}::Offset);");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
