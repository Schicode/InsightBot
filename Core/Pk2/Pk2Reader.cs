using InsightBot.Core.Network.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InsightBot.Core.Pk2;

// ── Public model ──────────────────────────────────────────────────────────────

public sealed class Pk2Folder
{
    public string Name { get; }
    public Dictionary<string, Pk2Folder> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Pk2File> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Pk2Folder(string name) => Name = name;
}

public sealed class Pk2File
{
    public string Name { get; }
    public long Position { get; }
    public uint Size { get; }
    public Pk2File(string name, long position, uint size)
        => (Name, Position, Size) = (name, position, size);
}

// ── Pk2Reader ─────────────────────────────────────────────────────────────────

/// <summary>
/// Reads Silkroad Online PK2 archives (Media.pk2 etc.).
///
/// ┌─ Binary layout ───────────────────────────────────────────────────────────┐
/// │  File header   : 256 bytes — plain text, NOT encrypted                    │
/// │  Entry blocks  : N × 2560 bytes — Blowfish-ECB encrypted                  │
/// │                  Each block = 20 entries × 128 bytes                      │
/// └───────────────────────────────────────────────────────────────────────────┘
///
/// Entry layout (128 bytes, little-endian):
///   0x00   1   type         0=null  1=folder  2=file
///   0x01  81   name         null-terminated, EUC-KR (CP949)
///   0x52   8   createTime   FILETIME – ignored
///   0x5A   8   modifyTime   FILETIME – ignored
///   0x62   8   accessTime   FILETIME – ignored   (3×8 = 24 bytes)
///   0x6A   8   position     int64 LE  folder→block offset  file→data offset
///   0x72   4   size         uint32 LE  file size; 0 for folders
///   0x76   8   nextChain    int64 LE  next block in chain; 0 = end
///   0x7E   2   padding
///   Total: 1+81+24+8+4+8+2 = 128 ✓
///
/// ┌─ BLOWFISH KEY DERIVATION ─────────────────────────────────────────────────┐
/// │  ROOT CAUSE of every "file not found" bug in naive implementations:       │
/// │                                                                            │
/// │  SRO does NOT feed the raw ASCII key ("169841") directly to Blowfish.     │
/// │  It first XORs each byte with a fixed 56-byte "base key" that is          │
/// │  hardcoded in GFXFileManager.dll / sro_client.exe.                        │
/// │                                                                            │
/// │  BaseKey[0..9] = 0x03,0xF8,0xE4,0x44,0x88,0x99,0x3F,0x64,0xFE,0x35     │
/// │  BaseKey[10..55] = 0x00 (zero-padded to 56 bytes)                        │
/// │                                                                            │
/// │  DerivedKey[i] = BaseKey[i] XOR asciiKey[i % keyLen]                      │
/// │                                                                            │
/// │  For "169841" (ASCII: 31 36 39 38 34 31):                                 │
/// │    [0] 0x03 ^ 0x31 = 0x32                                                 │
/// │    [1] 0xF8 ^ 0x36 = 0xCE                                                 │
/// │    [2] 0xE4 ^ 0x39 = 0xDD                                                 │
/// │    [3] 0x44 ^ 0x38 = 0x7C                                                 │
/// │    [4] 0x88 ^ 0x34 = 0xBC                                                 │
/// │    [5] 0x99 ^ 0x31 = 0xA8                                                 │
/// │    [6] 0x00 ^ 0x31 = 0x31  (BaseKey[6..] = 0)                            │
/// │    ...                                                                     │
/// └───────────────────────────────────────────────────────────────────────────┘
/// </summary>
public sealed class Pk2Reader : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int HeaderSize = 256;
    private const int BlockSize = 2560;   // 20 × 128
    private const int EntriesPerBlock = 20;
    private const int EntrySize = 128;

    private const int OffType = 0x00;
    private const int OffName = 0x01;
    private const int OffPosition = 0x6A;  // 106
    private const int OffSize = 0x72;  // 114
    private const int OffNextChain = 0x76;  // 118

    /// <summary>
    /// Fixed base key embedded in every official SRO client.
    /// Source: GFXFileManager.dll (IDA / Ghidra RE), confirmed by multiple
    /// independent implementations (xBot, PhantomBot, JellyBitz/SRO.PK2API).
    /// </summary>
    private static readonly byte[] BaseKey =
    {
        0x03, 0xF8, 0xE4, 0x44, 0x88, 0x99, 0x3F, 0x64,
        0xFE, 0x35, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00   // 56 bytes
    };

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly FileStream _stream;
    private readonly Blowfish _bf;

    public Pk2Folder Root { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="path">Full filesystem path to the .pk2 file.</param>
    /// <param name="key">Blowfish key string. Default iSRO key = "169841".</param>
    public Pk2Reader(string path, string? key = null)
    {
        byte[] derived = DeriveKey(key ?? "169841");
        _bf = new Blowfish();
        _bf.Initialize(derived);

        _stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                 FileShare.ReadWrite, bufferSize: 1 << 17);

        Root = new Pk2Folder("root");
        TraverseBlock(HeaderSize, Root);
    }

    // ── Key derivation ────────────────────────────────────────────────────────

    /// <summary>
    /// XORs the ASCII key bytes with the 56-byte base key to produce the
    /// actual Blowfish key used to encrypt PK2 entry blocks.
    /// </summary>
    public static byte[] DeriveKey(string keyStr)
    {
        byte[] ascii = Encoding.ASCII.GetBytes(keyStr);
        byte[] result = new byte[56];
        for (int i = 0; i < 56; i++)
            result[i] = (byte)(BaseKey[i] ^ ascii[i % ascii.Length]);
        return result;
    }

    // ── Block traversal ───────────────────────────────────────────────────────

    private void TraverseBlock(long startOffset, Pk2Folder folder)
    {
        // Collect sub-folder jobs; recurse only after the chain is fully consumed
        // to avoid FileStream seek-position conflicts while iterating the chain.
        var subJobs = new List<(Pk2Folder sub, long offset)>();

        long offset = startOffset;
        while (offset > 0)
        {
            byte[]? block = DecryptBlock(offset);
            if (block is null) break;

            long nextChain = 0;

            for (int i = 0; i < EntriesPerBlock; i++)
            {
                int o = i * EntrySize;
                byte type = block[o + OffType];

                switch (type)
                {
                    case 1: // folder
                        {
                            string name = DecodeName(block, o + OffName);
                            if (name is "." or "..") break;
                            long pos = ReadI64(block, o + OffPosition);
                            if (pos > 0 && name.Length > 0)
                            {
                                var sub = new Pk2Folder(name);
                                folder.Folders.TryAdd(name, sub);
                                subJobs.Add((sub, pos));
                            }
                            break;
                        }
                    case 2: // file
                        {
                            string name = DecodeName(block, o + OffName);
                            long pos = ReadI64(block, o + OffPosition);
                            uint size = ReadU32(block, o + OffSize);
                            if (name.Length > 0 && pos > 0)
                                folder.Files.TryAdd(name, new Pk2File(name, pos, size));
                            break;
                        }
                }

                // nextChain can appear in any entry slot; we keep the last non-zero value
                long nc = ReadI64(block, o + OffNextChain);
                if (nc > 0) nextChain = nc;
            }

            offset = nextChain;
        }

        foreach (var (sub, off) in subJobs)
            TraverseBlock(off, sub);
    }

    private byte[]? DecryptBlock(long offset)
    {
        if (offset <= 0) return null;

        _stream.Seek(offset, SeekOrigin.Begin);
        byte[] buf = new byte[BlockSize];
        int read = _stream.Read(buf, 0, BlockSize);
        if (read == 0) return null;

        // At EOF the last block may be smaller; round up to 8-byte Blowfish boundary
        if (read < BlockSize)
        {
            int padded = (read + 7) & ~7;
            Array.Resize(ref buf, padded);
        }

        return _bf.Decode(buf);
    }

    // ── Public read API ───────────────────────────────────────────────────────

    /// <summary>
    /// Read raw bytes of a file by virtual path (backslash-separated, case-insensitive).
    /// Example: <c>@"server_dep\silkroad\textdata\textuisystem.txt"</c>
    /// </summary>
    public byte[]? GetBytes(string virtualPath)
    {
        Pk2File? f = ResolveFile(virtualPath);
        if (f is null || f.Size == 0) return null;
        return ReadFileBytes(f);
    }

    /// <summary>Read text file, auto-detecting UTF-16 LE BOM / UTF-8 BOM / EUC-KR.</summary>
    public string? GetText(string virtualPath)
    {
        byte[]? data = GetBytes(virtualPath);
        return data is null ? null : DecodeText(data);
    }

    /// <summary>
    /// Read bytes by <see cref="Pk2File"/> reference (used by GameDataLoader
    /// when it already has the file object from folder enumeration).
    /// </summary>
    public byte[] ReadFileBytes(Pk2File file)
    {
        if (file.Size == 0) return Array.Empty<byte>();
        byte[] buf = new byte[file.Size];
        _stream.Seek(file.Position, SeekOrigin.Begin);
        _stream.ReadExactly(buf);
        return buf;
    }

    /// <summary>Read text by <see cref="Pk2File"/> reference.</summary>
    public string? ReadFileText(Pk2File file)
    {
        if (file.Size == 0) return null;
        return DecodeText(ReadFileBytes(file));
    }

    /// <summary>
    /// Search the entire tree breadth-first for a file by name only.
    /// Use when the exact path is unknown.
    /// </summary>
    public Pk2File? FindFile(string fileName)
    {
        var q = new Queue<Pk2Folder>();
        q.Enqueue(Root);
        while (q.Count > 0)
        {
            var dir = q.Dequeue();
            if (dir.Files.TryGetValue(fileName, out var f)) return f;
            foreach (var sub in dir.Folders.Values) q.Enqueue(sub);
        }
        return null;
    }

    /// <summary>Find and read a file anywhere in the tree by name only.</summary>
    public string? FindText(string fileName)
    {
        var f = FindFile(fileName);
        return f is null ? null : ReadFileText(f);
    }

    // ── Path navigation ───────────────────────────────────────────────────────

    public Pk2File? ResolveFile(string virtualPath)
    {
        var folder = Root;
        var span = virtualPath.AsSpan();

        while (true)
        {
            int sep = span.IndexOfAny('\\', '/');
            if (sep < 0)
            {
                folder.Files.TryGetValue(span.ToString(), out var f);
                return f;
            }
            if (!folder.Folders.TryGetValue(span[..sep].ToString(), out var next))
                return null;
            folder = next;
            span = span[(sep + 1)..];
        }
    }

    public Pk2Folder? ResolveFolder(string virtualPath)
    {
        var folder = Root;
        var span = virtualPath.AsSpan();

        while (span.Length > 0)
        {
            int sep = span.IndexOfAny('\\', '/');
            string seg = sep < 0 ? span.ToString() : span[..sep].ToString();

            if (!folder.Folders.TryGetValue(seg, out var next))
                return null;
            folder = next;

            if (sep < 0) return folder;
            span = span[(sep + 1)..];
        }
        return folder;
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    /// <summary>Enumerate every virtual path in the archive (for debugging).</summary>
    public IEnumerable<string> EnumerateAllPaths() => WalkFolder(Root, "");

    private static IEnumerable<string> WalkFolder(Pk2Folder dir, string prefix)
    {
        foreach (var name in dir.Files.Keys)
            yield return prefix.Length == 0 ? name : $"{prefix}\\{name}";
        foreach (var (name, sub) in dir.Folders)
            foreach (var path in WalkFolder(sub, prefix.Length == 0 ? name : $"{prefix}\\{name}"))
                yield return path;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DecodeName(byte[] buf, int offset)
    {
        int end = offset, max = offset + 81;
        while (end < max && buf[end] != 0) end++;
        int len = end - offset;
        if (len == 0) return string.Empty;
        try { return Encoding.GetEncoding(949).GetString(buf, offset, len); }
        catch { return Encoding.ASCII.GetString(buf, offset, len); }
    }

    private static string DecodeText(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        try { return Encoding.GetEncoding(949).GetString(data); }
        catch { return Encoding.Latin1.GetString(data); }
    }

    private static long ReadI64(byte[] b, int o) => BitConverter.ToInt64(b, o);
    private static uint ReadU32(byte[] b, int o) => BitConverter.ToUInt32(b, o);

    public void Dispose() => _stream.Dispose();
}