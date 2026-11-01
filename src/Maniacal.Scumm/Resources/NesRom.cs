using System.Buffers.Binary;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// The NES release's cartridge, seen as the loose files of the DOS releases: the index as
/// <c>00.LFL</c> and each room with its graphics, costumes, scripts and sounds as <c>NN.LFL</c>,
/// encoded as those are, with the cartridge's costume and font data gathered in <c>54.LFL</c>.
/// Mirrors <c>ScummNESFile</c>, which builds the same files from the ROM for the classic index
/// reader and the resource loader.
/// </summary>
/// <remarks>
/// ScummVM reads the cartridge's program data alone, saved as a <c>.prg</c> file. A <c>.nes</c>
/// file, the program data behind a 16-byte iNES header, is read too: the header is set aside.
/// The ROM is known by the MD5 of its program data, which names its ROM set.
/// </remarks>
internal sealed class NesRom : IGameContainer
{
    /// <summary>The bytes an iNES file starts with, and its header's length.</summary>
    private static ReadOnlySpan<byte> InesSignature => "NES\x1A"u8;

    private const int InesHeaderLength = 16, InesTrainerLength = 512, InesTrainerFlag = 0x04;

    // the index's tables (ScummNESFile's _lfl_index): rooms, costumes, scripts and sounds
    private const int Rooms = 55, Costumes = 80, Scripts = 200, Sounds = 100;

    // where the costume parts sit in the costume table (generateIndex)
    private const int SpriteDescriptionSlot = 25, SpriteLengthSlot = 27, SpriteOffsetSlot = 29, SpriteDataSlot = 31,
        CostumeGraphicsSlot = 33, SpritePaletteSlot = 35, RoomGraphicsSlot = 37, CharsetSlot = 77, PrepositionSlot = 78;

    /// <summary>The first word of the index, as <c>readIndexFile</c> knows the NES's (0x4643, "CF").</summary>
    public const ushort IndexSignature = 0x4643;

    private readonly byte[] _rom;
    private readonly Dictionary<int, byte[]> _files = [];

    private NesRom(string name, byte[] rom, int romSet)
    {
        Name = name;
        _rom = rom;
        RomSet = romSet;
    }

    public string Name { get; }

    /// <summary>The ROM set, in <c>ScummNESFile::ROMset</c>'s order: 0 for the USA, 1 for Europe and so on.</summary>
    public int RomSet { get; }

    /// <summary>The ROM set's name: "USA", "Europe", "Sweden", "France", "Germany", "Spain" or "Italy".</summary>
    public string RomSetName => NesTables.RomSets[RomSet];

    /// <summary>
    /// The cartridge in a folder, or null when it has none: the one <c>.prg</c> or <c>.nes</c>
    /// file whose program data is a ROM ScummVM knows.
    /// </summary>
    public static NesRom? Open(IGameContainer folder)
    {
        if (folder is NesRom rom)
        {
            return rom;
        }

        foreach (var name in folder.EnumerateFiles())
        {
            if ((name.EndsWith(".prg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".nes", StringComparison.OrdinalIgnoreCase))
                && FromBytes(folder.Name, folder.ReadAllBytes(name)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The cartridge in a file's bytes, a <c>.prg</c>'s or a <c>.nes</c>'s, or null when they are no ROM ScummVM knows.</summary>
    internal static NesRom? FromBytes(string name, byte[] data)
    {
        var program = ProgramData(data);
        int romSet = Array.IndexOf(NesTables.RomMd5, Md5.ComputeHex(program));
        return romSet < 0 ? null : new NesRom(name, program, romSet);
    }

    // a .nes file's program data follows its header, and its trainer when it has one
    private static byte[] ProgramData(byte[] data)
    {
        if (data.Length < InesHeaderLength || !data.AsSpan(0, 4).SequenceEqual(InesSignature))
        {
            return data;
        }

        int start = InesHeaderLength + ((data[6] & InesTrainerFlag) != 0 ? InesTrainerLength : 0);
        int length = Math.Min(data[4] * 16384, data.Length - start);
        return length > 0 ? data.AsSpan(start, length).ToArray() : data;
    }

    public IEnumerable<string> EnumerateFiles()
    {
        for (int room = 0; room < NesTables.Files.Length; room++)
        {
            yield return LflFile.NameOf(room);
        }
    }

    public bool Exists(string name) => Number(name) is not null;

    public long GetLength(string name) => File(name).Length;

    public Stream Open(string name) => new MemoryStream(File(name), writable: false);

    private byte[] File(string name)
    {
        if (Number(name) is not { } room)
        {
            throw new FileNotFoundException($"{name} is not on the cartridge.", name);
        }

        if (!_files.TryGetValue(room, out var data))
        {
            data = room == 0 ? GenerateIndex() : GenerateRoom(room);
            _files[room] = data;
        }

        return data;
    }

    // "NN.LFL", as openSubFile reads the number from the two characters before the dot
    private static int? Number(string name)
        => name.Length == 6 && name.EndsWith(".LFL", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(0, 2), out int room) && room < NesTables.Files.Length
            ? room
            : null;

    /// <summary>
    /// A room file: its resources one after another, then two bytes, 0xD1 and 0xF5, as the DOS
    /// files end. Mirrors <c>generateResource</c>.
    /// </summary>
    private byte[] GenerateRoom(int room)
    {
        var output = new FileWriter();
        foreach (var (kind, index) in NesTables.Files[room])
        {
            ExtractResource(output, kind, index);
        }

        output.Byte(0xD1);
        output.Byte(0xF5);
        return output.ToArray();
    }

    /// <summary>
    /// The index: "CF", the global objects' owners and states from the cartridge, then where each
    /// room, costume, script and sound is, as room files and offsets. The costume table also holds
    /// the costume parts, the rooms' graphics, the font's translation and the prepositions, at the
    /// slots the engine looks for them. Mirrors <c>generateIndex</c>.
    /// </summary>
    private byte[] GenerateIndex()
    {
        var roomFiles = new byte[Rooms];
        var roomOffsets = new ushort[Rooms];
        var costumeFiles = new byte[Costumes];
        var costumeOffsets = new ushort[Costumes];
        var scriptFiles = new byte[Scripts];
        var scriptOffsets = new ushort[Scripts];
        var soundFiles = new byte[Sounds];
        var soundOffsets = new ushort[Sounds];

        for (int file = 1; file < NesTables.Files.Length; file++)
        {
            int position = 0;
            foreach (var (kind, index) in NesTables.Files[file])
            {
                void Place(byte[] files, ushort[] offsets, int slot)
                {
                    files[slot] = (byte)file;
                    offsets[slot] = (ushort)position;
                }

                switch (kind)
                {
                    case NesResourceKind.Room: Place(roomFiles, roomOffsets, index); break;
                    case NesResourceKind.Costume: Place(costumeFiles, costumeOffsets, index); break;
                    case NesResourceKind.SpriteDescriptions: Place(costumeFiles, costumeOffsets, index + SpriteDescriptionSlot); break;
                    case NesResourceKind.SpriteLengths: Place(costumeFiles, costumeOffsets, index + SpriteLengthSlot); break;
                    case NesResourceKind.SpriteOffsets: Place(costumeFiles, costumeOffsets, index + SpriteOffsetSlot); break;
                    case NesResourceKind.SpriteData: Place(costumeFiles, costumeOffsets, index + SpriteDataSlot); break;
                    case NesResourceKind.CostumeGraphics: Place(costumeFiles, costumeOffsets, index + CostumeGraphicsSlot); break;
                    case NesResourceKind.SpritePalettes: Place(costumeFiles, costumeOffsets, index + SpritePaletteSlot); break;
                    case NesResourceKind.RoomGraphics: Place(costumeFiles, costumeOffsets, index + RoomGraphicsSlot); break;
                    case NesResourceKind.Script: Place(scriptFiles, scriptOffsets, index); break;
                    case NesResourceKind.Sound: Place(soundFiles, soundOffsets, index); break;
                    case NesResourceKind.Charset: Place(costumeFiles, costumeOffsets, CharsetSlot); break;
                    case NesResourceKind.PrepositionList: Place(costumeFiles, costumeOffsets, PrepositionSlot); break;
                    default: throw new InvalidDataException($"Room file {file} holds a {kind}, which the index has no place for.");
                }

                position += ExtractResource(null, kind, index);
            }
        }

        var output = new FileWriter();
        output.Word(IndexSignature);
        ExtractResource(output, NesResourceKind.GlobalData, 0);
        foreach (var (files, offsets) in new[] { (roomFiles, roomOffsets), (costumeFiles, costumeOffsets), (scriptFiles, scriptOffsets), (soundFiles, soundOffsets) })
        {
            foreach (byte file in files)
            {
                output.Byte(file);
            }

            foreach (ushort offset in offsets)
            {
                output.Word(offset);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// A resource's bytes as the cartridge holds them, from its place on: to the program data's
    /// end, for the title screens, which are read until they are whole (<c>decodeTitleScreen</c>),
    /// or its length alone (<c>readTitle2SparkleChr</c>, <c>readTitle2SparklePalette</c>).
    /// </summary>
    internal ReadOnlyMemory<byte> Raw(NesResourceKind kind, int index, bool toEnd = false)
    {
        var (offset, length) = NesTables.Group(kind)[RomSet][index];
        return toEnd ? _rom.AsMemory(offset) : _rom.AsMemory(offset, length);
    }

    /// <summary>
    /// Writes one resource as the DOS files hold it, led by its length word, and returns how many
    /// bytes that is (with no output, only counts them). A resource's place and length come from
    /// the ROM set's tables; the cartridge's own lengths are checked against them. Mirrors
    /// <c>extractResource</c>.
    /// </summary>
    private int ExtractResource(FileWriter? output, NesResourceKind kind, int index)
    {
        var (offset, length) = NesTables.Group(kind)[RomSet][index];

        // there are 8 scripts that are zero bytes long, which are skipped
        if (offset == 0 && length == 0)
        {
            return 0;
        }

        var rom = new RomReader(_rom, offset);
        int written = 0;
        void Byte(byte value)
        {
            output?.Byte(value);
            written++;
        }

        void Word(int value)
        {
            output?.Word((ushort)value);
            written += 2;
        }

        void CheckLength(string what)
        {
            if (rom.Position - offset != length)
            {
                throw new InvalidDataException($"The cartridge's {what} {kind} {index} is 0x{rom.Position - offset:X4} bytes long, not 0x{length:X4}.");
            }
        }

        switch (kind)
        {
            case NesResourceKind.GlobalData:
                for (int i = 0; i < length; i++)
                {
                    Byte(rom.Byte());
                }

                break;

            case NesResourceKind.RoomGraphics:
            case NesResourceKind.CostumeGraphics:
            {
                // tiles, 16 bytes each, run-length coded: a count byte, its high bit meaning that
                // many bytes follow, else one byte to repeat
                Word(length + 2);
                int tiles = rom.Byte();
                Byte((byte)tiles);
                int bytes = (tiles == 0 ? 256 : tiles) << 4;
                for (int i = 0; i < bytes;)
                {
                    byte count = rom.Byte();
                    Byte(count);
                    for (int j = 0; j < (count & 0x7F); j++, i++)
                    {
                        if ((count & 0x80) != 0 || j == 0)
                        {
                            Byte(rom.Byte());
                        }
                    }
                }

                CheckLength("graphics");
                break;
            }

            case NesResourceKind.Room:
            case NesResourceKind.Script:
            {
                // these keep their own length word, the length included
                int own = rom.Word();
                if (own != length)
                {
                    throw new InvalidDataException($"The cartridge's {kind} {index} says it is 0x{own:X4} bytes long, not 0x{length:X4}.");
                }

                rom.Position -= 2;
                for (int i = 0; i < length; i++)
                {
                    Byte(rom.Byte());
                }

                break;
            }

            case NesResourceKind.Sound:
            {
                Word(length + 2);
                byte type = rom.Byte(), tempo = rom.Byte();
                Byte(type);
                Byte(tempo);
                if (type == 2 && tempo == 100)
                {
                    // music: a channel count, the channels' starts and tracks, then events to 0xFE or 0xFF
                    byte channels = rom.Byte();
                    Byte(channels);
                    for (int i = 0; i < channels * 2; i++)
                    {
                        Byte(rom.Byte());
                    }

                    byte value;
                    do
                    {
                        value = rom.Byte();
                        Byte(value);
                    }
                    while (value < 0xFE);
                }
                else if (type is 0 or 1 or 4 && tempo == 10)
                {
                    // effects: commands to 0xFE or 0xFF, a command under 0x10 with four bytes, else one
                    while (true)
                    {
                        byte value = rom.Byte();
                        Byte(value);
                        if (value >= 0xFE)
                        {
                            break;
                        }

                        for (int i = value >= 0x10 ? 1 : 4; i > 0; i--)
                        {
                            Byte(rom.Byte());
                        }
                    }
                }
                else
                {
                    throw new InvalidDataException($"The cartridge's sound {index} is of type {type}/{tempo}, which no sound is.");
                }

                CheckLength("sound");
                break;
            }

            case NesResourceKind.Costume:
            case NesResourceKind.SpritePalettes:
            case NesResourceKind.SpriteDescriptions:
            case NesResourceKind.SpriteLengths:
            case NesResourceKind.SpriteOffsets:
            case NesResourceKind.SpriteData:
            case NesResourceKind.Charset:
                Word(length + 2);
                for (int i = 0; i < length; i++)
                {
                    Byte(rom.Byte());
                }

                break;

            case NesResourceKind.PrepositionList:
            {
                // five words of 8 bytes, each a space then its letters, padded with zeros: the
                // first empty, the other four from the cartridge's zero-ended words
                Word(0x002A);
                Byte((byte)' ');
                for (int i = 1; i < 8; i++)
                {
                    Byte(0);
                }

                for (int word = 0; word < 4; word++)
                {
                    Byte((byte)' ');
                    int i = 1;
                    for (byte value; (value = rom.Byte()) != 0; i++)
                    {
                        Byte(value);
                    }

                    for (; i < 8; i++)
                    {
                        Byte(0);
                    }
                }

                break;
            }

            default:
                throw new InvalidDataException($"A {kind} is not kept in a room file.");
        }

        return written;
    }

    /// <summary>The files' bytes, encoded as the DOS files are (<c>write_byte</c>, <c>write_word</c>).</summary>
    private sealed class FileWriter
    {
        private readonly List<byte> _bytes = [];

        public void Byte(byte value) => _bytes.Add((byte)(value ^ LflFile.EncodingKey));

        public void Word(ushort value)
        {
            Byte((byte)value);
            Byte((byte)(value >> 8));
        }

        public byte[] ToArray() => [.. _bytes];
    }

    private struct RomReader(byte[] rom, int position)
    {
        public int Position { get; set; } = position;

        public byte Byte() => Position < rom.Length
            ? rom[Position++]
            : throw new InvalidDataException($"The cartridge ends at {rom.Length} bytes, before the resource does.");

        public ushort Word() => (ushort)(Byte() | Byte() << 8);
    }
}
