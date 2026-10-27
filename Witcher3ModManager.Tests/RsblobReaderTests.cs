using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Witcher3ModManager.Core.Analysis.Rsblob;
using Witcher3ModManager.Core.Analysis.Script;
using Xunit;

namespace Witcher3ModManager.Tests;

public class RsblobReaderTests
{
    [Fact]
    public void Reads_class_function_symbols()
    {
        var blob = new Blob();
        blob.Compressed(0);                                   // enums
        blob.Compressed(0);                                   // structs
        blob.Compressed(1);                                   // classes
        blob.CName("W3DamageManagerProcessor");
        blob.CName("");                                       // parent
        blob.CName("");                                       // state machine name
        blob.U8(0);                                           // is state
        blob.Compressed(0);                                   // class flags
        blob.Compressed(0);                                   // properties
        blob.Compressed(1);                                   // functions
        blob.CName("ProcessActionReaction");
        blob.CName("");                                       // override class
        blob.Compressed(0x4000);                              // flags
        blob.U8(0);                                           // no return type
        blob.Compressed(0);                                   // parameters
        blob.Compressed(0);                                   // locals
        blob.Compressed(0);                                   // bytecode
        blob.Compressed(0);                                   // default values
        blob.Compressed(0);                                   // global functions
        blob.Compressed(0);                                   // ext replace (global)
        blob.Compressed(0);                                   // ext replace (class)
        blob.Compressed(0);                                   // ext add
        blob.Compressed(0);                                   // ext wrap
        blob.Compressed(0);                                   // ext add properties
        blob.Compressed(0);                                   // import types
        blob.Compressed(0);                                   // import properties
        blob.Compressed(0);                                   // import functions

        var document = RsblobReader.Read(blob.ToArray());

        Assert.Equal(1, document.FormatVersion);
        Assert.Equal("x64", document.BuildPlatform);
        Assert.Equal("1048625", document.BuildVersion);
        Assert.Equal("Release", document.BuildConfig);

        var symbol = Assert.Single(document.Symbols);
        Assert.Equal("W3DamageManagerProcessor", symbol.ClassName);
        Assert.Equal("ProcessActionReaction", symbol.MethodName);
        Assert.Equal(ScriptSymbolKind.ClassFunction, symbol.Kind);
        Assert.Equal(ScriptSymbolOrigin.Rsblob, symbol.Origin);
        Assert.Equal(0x4000u, symbol.Flags);
    }

    [Fact]
    public void Reads_wrap_annotation_with_target_class()
    {
        var blob = new Blob();
        blob.Compressed(0);                                   // enums
        blob.Compressed(0);                                   // structs
        blob.Compressed(0);                                   // classes
        blob.Compressed(0);                                   // global functions
        blob.Compressed(0);                                   // ext replace (global)
        blob.Compressed(0);                                   // ext replace (class)
        blob.Compressed(0);                                   // ext add
        blob.Compressed(1);                                   // ext wrap
        blob.CName("ProcessDismemberment");
        blob.CName("W3DamageManagerProcessor");
        blob.Compressed(0x100000);
        blob.U8(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);                                   // ext add properties
        blob.Compressed(0);                                   // import types
        blob.Compressed(0);                                   // import properties
        blob.Compressed(0);                                   // import functions

        var symbol = Assert.Single(RsblobReader.Read(blob.ToArray()).Symbols);

        Assert.Equal(ScriptSymbolKind.Wrap, symbol.Kind);
        Assert.Equal(ScriptSymbolOrigin.Rsblob, symbol.Origin);
        Assert.Equal("W3DamageManagerProcessor", symbol.ClassName);
        Assert.Equal("ProcessDismemberment", symbol.MethodName);
    }

    [Fact]
    public void Reads_utf16_names_and_skips_function_bodies()
    {
        var blob = new Blob();
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(1);
        blob.CNameUtf16("modKlass");
        blob.CName("");
        blob.CName("");
        blob.U8(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(1);
        blob.CNameUtf16("ProcessAction");
        blob.CName("");
        blob.Compressed(0x10000);
        blob.U8(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(3);                                   // three bytecode bytes
        blob.Raw(new byte[] { 0xAA, 0xBB, 0xCC });
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);

        var symbol = Assert.Single(RsblobReader.Read(blob.ToArray()).Symbols);

        Assert.Equal("modKlass", symbol.ClassName);
        Assert.Equal("ProcessAction", symbol.MethodName);
    }

    [Fact]
    public void Rejects_an_unsupported_format_version()
    {
        var blob = new Blob { Version = 2 };

        Assert.Throws<InvalidDataException>(() => RsblobReader.Read(blob.ToArray()));
    }

    [Fact]
    public void Rejects_a_truncated_blob()
    {
        var blob = new Blob();
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);
        blob.Compressed(0);

        var data = blob.ToArray();
        var truncated = data.Take(data.Length / 2).ToArray();

        Assert.Throws<InvalidDataException>(() => RsblobReader.Read(truncated));
    }

    private sealed class Blob
    {
        private readonly MemoryStream _body = new();
        private readonly BinaryWriter _writer;
        private readonly List<string> _strings = new();
        private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
        private readonly HashSet<int> _utf16 = new();

        public Blob()
        {
            _writer = new BinaryWriter(_body, Encoding.UTF8, leaveOpen: true);
        }

        public int Version { get; init; } = 1;

        public void U8(byte value) => _writer.Write(value);

        public void Raw(byte[] bytes) => _writer.Write(bytes);

        public void CName(string value) => Compressed(Index(value));

        public void CNameUtf16(string value)
        {
            var index = Index(value);
            _utf16.Add(index);
            Compressed(index);
        }

        public void Compressed(int value)
        {
            var negative = value < 0;
            var remaining = (uint)Math.Abs((long)value);

            var first = (byte)(remaining & 0x3F);
            remaining >>= 6;

            if (remaining == 0 && !negative)
            {
                _writer.Write(first);
                return;
            }

            first |= 0x40;
            if (negative) first |= 0x80;
            _writer.Write(first);

            var next = (byte)(remaining & 0x7F);
            remaining >>= 7;
            while (remaining != 0)
            {
                _writer.Write((byte)(next | 0x80));
                next = (byte)(remaining & 0x7F);
                remaining >>= 7;
            }

            _writer.Write(next);
        }

        public byte[] ToArray()
        {
            _writer.Flush();

            var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

            writer.Write((uint)Version);
            WriteString(writer, "x64");
            WriteString(writer, "1048625");
            writer.Write(new byte[8]); // timestamp
            WriteString(writer, "Release");

            writer.Write(_body.ToArray());

            var tableOffset = (uint)output.Position;
            writer.Write((uint)_strings.Count);
            for (var index = 0; index < _strings.Count; index++)
            {
                if (_utf16.Contains(index)) WriteString16(writer, _strings[index]);
                else WriteString(writer, _strings[index]);
            }
            writer.Write(tableOffset);

            return output.ToArray();
        }

        private int Index(string value)
        {
            if (_indices.TryGetValue(value, out var index)) return index;
            index = _strings.Count;
            _strings.Add(value);
            _indices[value] = index;
            return index;
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var negative = bytes.Length == 0 ? 0 : -bytes.Length;

            // Negative length = UTF-8, zero = empty. Mirrors what the game writes for short names.
            WriteCompressed(writer, negative);
            writer.Write(bytes);
        }

        private static void WriteString16(BinaryWriter writer, string value)
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            WriteCompressed(writer, value.Length);
            writer.Write(bytes);
        }

        private static void WriteCompressed(BinaryWriter writer, int value)
        {
            var negative = value < 0;
            var remaining = (uint)Math.Abs((long)value);

            var first = (byte)(remaining & 0x3F);
            remaining >>= 6;

            if (remaining == 0 && !negative)
            {
                writer.Write(first);
                return;
            }

            first |= 0x40;
            if (negative) first |= 0x80;
            writer.Write(first);

            var next = (byte)(remaining & 0x7F);
            remaining >>= 7;
            while (remaining != 0)
            {
                writer.Write((byte)(next | 0x80));
                next = (byte)(remaining & 0x7F);
                remaining >>= 7;
            }

            writer.Write(next);
        }
    }
}
