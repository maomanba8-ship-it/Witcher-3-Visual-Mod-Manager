using System.IO;
using System.Text;
using Witcher3ModManager.Core.Analysis.Script;

namespace Witcher3ModManager.Core.Analysis.Rsblob;

/// <summary>
/// The parts of a blob the conflict analysis needs. Only metadata is read; bytecode is skipped.
/// </summary>
public sealed record RsblobDocument(
    int FormatVersion,
    string BuildPlatform,
    string BuildVersion,
    string BuildConfig,
    IReadOnlyList<ScriptSymbol> Symbols);

/// <summary>
/// Reads the symbol table of a Witcher 3 <c>precompiled.rsblob</c>.
///
/// The format is the one documented by the community project tw3-cahirb (this reader is an
/// independent implementation of the observed format, not a copy of its code): a header, then
/// the definition tables, with the string table stored near the end of the file and its offset
/// in the last four bytes.
///
/// Only the metadata is consumed; bytecode bodies are skipped. Anything unexpected throws
/// <see cref="InvalidDataException"/> so the caller can fall back to "cannot tell" instead of
/// reporting a conflict from a half-understood file.
/// </summary>
public static class RsblobReader
{
    public const int SupportedFormatVersion = 1;

    private const int MaxElements = 1_000_000;
    private const int MaxStringBytes = 64 * 1024 * 1024;

    public static RsblobDocument Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllBytes(path));
    }

    public static RsblobDocument Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var cursor = new Cursor(data);

        var formatVersion = (int)cursor.U32();
        if (formatVersion != SupportedFormatVersion)
            throw new InvalidDataException($"Unsupported rsblob format version {formatVersion}.");

        var buildPlatform = cursor.ReadString();
        var buildVersion = cursor.ReadString();
        cursor.Skip(8); // CDateTime (date + time), not needed for symbols
        var buildConfig = cursor.ReadString();

        var definitionsStart = cursor.Position;
        var stringTable = ReadStringTable(data);
        cursor.Position = definitionsStart;

        var strings = new StringTable(stringTable);
        var symbols = new List<ScriptSymbol>();

        // Enums
        var enumCount = ReadArray(cursor);
        for (var i = 0; i < enumCount; i++)
        {
            strings.Name(cursor);
            var enumeratorCount = ReadArray(cursor);
            for (var j = 0; j < enumeratorCount; j++)
            {
                strings.Name(cursor);
                cursor.CompressedI32();
            }
        }

        // Structs
        var structCount = ReadArray(cursor);
        for (var i = 0; i < structCount; i++)
        {
            strings.Name(cursor);
            cursor.CompressedU32();
            SkipProperties(cursor, strings);
            SkipDefaultValues(cursor, strings);
        }

        // Classes
        var classCount = ReadArray(cursor);
        for (var i = 0; i < classCount; i++)
        {
            var className = strings.Name(cursor);
            strings.Name(cursor); // parent
            strings.Name(cursor); // state machine name
            cursor.U8();          // is state
            cursor.CompressedU32();
            SkipProperties(cursor, strings);

            var functionCount = ReadArray(cursor);
            for (var j = 0; j < functionCount; j++)
            {
                var function = ReadFunction(cursor, strings);
                symbols.Add(new ScriptSymbol(
                    className, function.Name, ScriptSymbolKind.ClassFunction, ScriptSymbolOrigin.Rsblob, function.Flags));
            }

            SkipDefaultValues(cursor, strings);
        }

        // Global functions
        var globalFunctionCount = ReadArray(cursor);
        for (var i = 0; i < globalFunctionCount; i++)
        {
            var function = ReadFunction(cursor, strings);
            symbols.Add(new ScriptSymbol(
                "", function.Name, ScriptSymbolKind.GlobalFunction, ScriptSymbolOrigin.Rsblob, function.Flags));
        }

        // Annotations: @replace (global), @replace (class), @addMethod, @wrapMethod, @addField
        ReadAnnotationArray(cursor, strings, symbols, ScriptSymbolKind.Replace);
        ReadAnnotationArray(cursor, strings, symbols, ScriptSymbolKind.Replace);
        ReadAnnotationArray(cursor, strings, symbols, ScriptSymbolKind.Add);
        ReadAnnotationArray(cursor, strings, symbols, ScriptSymbolKind.Wrap);

        var addedPropertyCount = ReadArray(cursor);
        for (var i = 0; i < addedPropertyCount; i++)
        {
            var property = ReadProperty(cursor, strings);
            symbols.Add(new ScriptSymbol(
                property.ClassName, property.Name, ScriptSymbolKind.AddProperty, ScriptSymbolOrigin.Rsblob, property.Flags));
        }

        // The remaining tables describe imports used by bytecode; nothing to extract there.
        return new RsblobDocument(formatVersion, buildPlatform, buildVersion, buildConfig, symbols);
    }

    private static string[] ReadStringTable(byte[] data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("File is too small to be an rsblob.");

        var cursor = new Cursor(data) { Position = data.Length - 4 };
        var offset = cursor.U32();
        if (offset >= (uint)data.Length)
            throw new InvalidDataException("String table offset is outside the file.");

        cursor.Position = (int)offset;
        // The table length is a plain u32 (unlike the definition arrays, which are compressed).
        var count = cursor.U32();
        if (count > MaxElements)
            throw new InvalidDataException($"String table length {count} is unreasonable.");

        var strings = new string[(int)count];
        for (var i = 0; i < strings.Length; i++)
            strings[i] = cursor.ReadString();
        return strings;
    }

    private static void ReadAnnotationArray(
        Cursor cursor,
        StringTable strings,
        List<ScriptSymbol> symbols,
        ScriptSymbolKind kind)
    {
        var count = ReadArray(cursor);
        for (var i = 0; i < count; i++)
        {
            var function = ReadFunction(cursor, strings);
            symbols.Add(new ScriptSymbol(
                function.OverrideClass, function.Name, kind, ScriptSymbolOrigin.Rsblob, function.Flags));
        }
    }

    private static (string Name, string OverrideClass, uint Flags) ReadFunction(Cursor cursor, StringTable strings)
    {
        var name = strings.Name(cursor);
        var overrideClass = strings.Name(cursor);
        var flags = cursor.CompressedU32();

        if (cursor.U8() != 0)
            ReadProperty(cursor, strings); // return type

        SkipProperties(cursor, strings); // parameters
        SkipProperties(cursor, strings); // locals

        var bytecodeLength = cursor.CompressedU32();
        if (bytecodeLength > (uint)cursor.Remaining)
            throw new InvalidDataException("Function bytecode length is outside the file.");
        cursor.Skip((int)bytecodeLength);

        return (name, overrideClass, flags);
    }

    private static void SkipProperties(Cursor cursor, StringTable strings)
    {
        var count = ReadArray(cursor);
        for (var i = 0; i < count; i++)
            ReadProperty(cursor, strings);
    }

    private static (string Name, string ClassName, uint Flags) ReadProperty(Cursor cursor, StringTable strings)
    {
        var name = strings.Name(cursor);
        cursor.ReadString();          // hint (not indexed)
        var flags = cursor.CompressedU32();
        strings.Name(cursor);         // type name
        var className = strings.Name(cursor);
        strings.Name(cursor);         // binding
        return (name, className, flags);
    }

    private static void SkipDefaultValues(Cursor cursor, StringTable strings)
    {
        var count = ReadArray(cursor);
        for (var i = 0; i < count; i++)
        {
            strings.Name(cursor);
            SkipDefaultValue(cursor, strings);
        }
    }

    private static void SkipDefaultValue(Cursor cursor, StringTable strings)
    {
        strings.Name(cursor);
        cursor.ReadString(); // value is not indexed
        var count = ReadArray(cursor);
        for (var i = 0; i < count; i++)
            SkipDefaultValue(cursor, strings);
    }

    private static int ReadArray(Cursor cursor)
    {
        var count = cursor.CompressedI32();
        if (count < 0 || count > MaxElements)
            throw new InvalidDataException($"Invalid array length {count}.");
        return count;
    }

    private readonly struct StringTable
    {
        private readonly string[] _strings;

        public StringTable(string[] strings) => _strings = strings;

        public string Name(Cursor cursor)
        {
            var index = cursor.CompressedU32();
            if (index >= (uint)_strings.Length)
                throw new InvalidDataException($"String index {index} is outside the table.");
            return _strings[index];
        }
    }

    private sealed class Cursor
    {
        private readonly byte[] _data;

        public Cursor(byte[] data) => _data = data;

        public int Position { get; set; }

        public int Remaining => _data.Length - Position;

        public byte U8()
        {
            Ensure(1);
            return _data[Position++];
        }

        public uint U32()
        {
            Ensure(4);
            var value = BitConverter.ToUInt32(_data, Position);
            Position += 4;
            return value;
        }

        public void Skip(int count)
        {
            Ensure(count);
            Position += count;
        }

        public int CompressedI32()
        {
            var first = U8();
            var negative = (first & 0x80) != 0;
            var result = first & 0x3F;

            if ((first & 0x40) != 0)
            {
                var second = U8();
                result |= (second & 0x7F) << 6;
                if ((second & 0x80) != 0)
                {
                    var third = U8();
                    result |= (third & 0x7F) << 13;
                    if ((third & 0x80) != 0)
                    {
                        var fourth = U8();
                        result |= (fourth & 0x7F) << 20;
                        if ((fourth & 0x80) != 0)
                        {
                            var fifth = U8();
                            result |= (fifth & 0x7F) << 27;
                        }
                    }
                }
            }

            return negative ? -result : result;
        }

        public uint CompressedU32() => unchecked((uint)CompressedI32());

        public string ReadString()
        {
            var length = CompressedI32();
            if (length == 0)
                return "";

            if (length > 0)
            {
                var bytes = length * 2;
                if (bytes > MaxStringBytes)
                    throw new InvalidDataException($"String length {length} is unreasonable.");
                Ensure(bytes);
                var text = Encoding.Unicode.GetString(_data, Position, bytes);
                Position += bytes;
                return text;
            }

            var utf8Bytes = -length;
            if (utf8Bytes > MaxStringBytes)
                throw new InvalidDataException($"String length {utf8Bytes} is unreasonable.");
            Ensure(utf8Bytes);
            var utf8 = Encoding.UTF8.GetString(_data, Position, utf8Bytes);
            Position += utf8Bytes;
            return utf8;
        }

        private void Ensure(int count)
        {
            if (count < 0 || Position + count > _data.Length)
                throw new InvalidDataException("Unexpected end of rsblob.");
        }
    }
}
