using System.Text;

namespace SteamImport.Infrastructure;

internal sealed class BinaryVdfNode
{
    internal BinaryVdfNode(byte type, string name, object? value, List<BinaryVdfNode>? children)
    {
        Type = type;
        Name = name;
        Value = value;
        Children = children;
    }

    public byte Type { get; }

    public string Name { get; }

    public object? Value { get; }

    public List<BinaryVdfNode>? Children { get; }

    public static BinaryVdfNode Object(string name, IEnumerable<BinaryVdfNode>? children = null) =>
        new(BinaryVdf.Object, name, null, children?.ToList() ?? []);

    public static BinaryVdfNode String(string name, string value) =>
        new(BinaryVdf.String, name, value, null);

    public static BinaryVdfNode Int32(string name, int value) =>
        new(BinaryVdf.Int32, name, value, null);

    public T GetValue<T>() => Value is T value
        ? value
        : throw new InvalidDataException($"VDF value '{Name}' is not a {typeof(T).Name}.");
}

internal static class BinaryVdf
{
    internal const byte Object = 0;
    internal const byte String = 1;
    internal const byte Int32 = 2;
    private const byte Float32 = 3;
    private const byte Color = 4;
    private const byte WideString = 5;
    private const byte Pointer = 6;
    private const byte UInt64 = 7;
    private const byte End = 8;
    private const byte Int64 = 10;
    private const byte AlternativeEnd = 11;

    public static BinaryVdfNode Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            var type = reader.ReadByte();
            if (type is End or AlternativeEnd)
            {
                throw new InvalidDataException("The VDF file does not contain a root object.");
            }

            var root = ReadNode(reader, type, ReadNullTerminatedUtf8(reader));
            if (reader.ReadByte() != End)
            {
                throw new InvalidDataException("The binary VDF document is missing its final marker.");
            }

            if (stream.CanSeek && stream.Position != stream.Length)
            {
                throw new InvalidDataException("The binary VDF document contains trailing data.");
            }

            return root;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The binary VDF document is truncated.", exception);
        }
    }

    public static void Write(Stream stream, BinaryVdfNode root)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(root);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteNode(writer, root);
        writer.Write(End);
    }

    private static BinaryVdfNode ReadNode(BinaryReader reader, byte type, string name)
    {
        if (type == Object)
        {
            var children = new List<BinaryVdfNode>();
            while (true)
            {
                var childType = reader.ReadByte();
                if (childType is End or AlternativeEnd)
                {
                    break;
                }

                children.Add(ReadNode(reader, childType, ReadNullTerminatedUtf8(reader)));
            }

            return BinaryVdfNode.Object(name, children);
        }

        object value = type switch
        {
            String => ReadNullTerminatedUtf8(reader),
            Int32 => reader.ReadInt32(),
            Float32 => reader.ReadSingle(),
            Pointer => reader.ReadUInt32(),
            WideString => ReadNullTerminatedUtf16(reader),
            Color => reader.ReadBytes(4),
            UInt64 => reader.ReadUInt64(),
            Int64 => reader.ReadInt64(),
            _ => throw new InvalidDataException($"Unsupported binary VDF type: {type}."),
        };

        return new BinaryVdfNode(type, name, value, null);
    }

    private static void WriteNode(BinaryWriter writer, BinaryVdfNode node)
    {
        writer.Write(node.Type);
        WriteNullTerminatedUtf8(writer, node.Name);

        if (node.Type == Object)
        {
            foreach (var child in node.Children ?? [])
            {
                WriteNode(writer, child);
            }

            writer.Write(End);
            return;
        }

        switch (node.Type)
        {
            case String:
                WriteNullTerminatedUtf8(writer, node.GetValue<string>());
                break;
            case Int32:
                writer.Write(node.GetValue<int>());
                break;
            case Float32:
                writer.Write(node.GetValue<float>());
                break;
            case Pointer:
                writer.Write(node.GetValue<uint>());
                break;
            case WideString:
                WriteNullTerminatedUtf16(writer, node.GetValue<string>());
                break;
            case Color:
                writer.Write(node.GetValue<byte[]>());
                break;
            case UInt64:
                writer.Write(node.GetValue<ulong>());
                break;
            case Int64:
                writer.Write(node.GetValue<long>());
                break;
            default:
                throw new InvalidDataException($"Unsupported binary VDF type: {node.Type}.");
        }
    }

    private static string ReadNullTerminatedUtf8(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        while (true)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            bytes.WriteByte(value);
        }
    }

    private static string ReadNullTerminatedUtf16(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        while (true)
        {
            var first = reader.ReadByte();
            var second = reader.ReadByte();
            if (first == 0 && second == 0)
            {
                return Encoding.Unicode.GetString(bytes.ToArray());
            }

            bytes.WriteByte(first);
            bytes.WriteByte(second);
        }
    }

    private static void WriteNullTerminatedUtf8(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private static void WriteNullTerminatedUtf16(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }
}
