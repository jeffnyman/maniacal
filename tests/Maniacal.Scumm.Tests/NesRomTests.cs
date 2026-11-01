using System.Buffers.Binary;
using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The NES cartridge, read into the loose files of the DOS releases as ScummVM's ScummNESFile reads
/// it. The tests with the cartridge pass without checking when its folder is absent.
/// </summary>
public class NesRomTests
{
    private const int ProgramLength = 262144, InesHeaderLength = 16, IndexLength = 2082;

    // the cartridge's program data, its .prg, read from whichever file the folder has
    private static byte[]? Program()
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path)
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(path))
        {
            var data = File.ReadAllBytes(file);
            if (file.EndsWith(".prg", StringComparison.OrdinalIgnoreCase) && data.Length == ProgramLength)
            {
                return data;
            }

            if (file.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) && data.Length == InesHeaderLength + ProgramLength)
            {
                return data[InesHeaderLength..];
            }
        }

        return null;
    }

    // a .nes file: the iNES header, 16 banks of program data and none of tiles, then the program
    private static byte[] Ines(byte[] program) => [.. "NES\x1A"u8, 16, 0, 0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. program];

    /// <summary>
    /// The USA cartridge, as a .prg or as a .nes file, is the release ScummVM's table names by the
    /// MD5 of the index built from it: the index is the same, byte for byte.
    /// </summary>
    [Theory]
    [InlineData("Maniac Mansion (U).prg", false)]
    [InlineData("Maniac Mansion (USA).nes", true)]
    public void Identifies_the_usa_cartridge(string name, bool ines)
    {
        if (Program() is not { } program)
        {
            return;
        }

        var folder = new MemoryGameContainer("nes").Add(name, ines ? Ines(program) : program);
        var game = ScummDetector.Identify(folder);

        Assert.NotNull(game);
        Assert.Equal("NES", game.Variant);
        Assert.Equal(1, game.Version);
        Assert.Equal("en", game.Language);
        Assert.Equal("nes", game.Platform);
        Assert.Equal("91d5db93187fab54d823f73bd6441cb6", game.Entry.Md5);
        Assert.Equal(IndexLength, game.Entry.Size);
        Assert.False(game.IsDemo);

        var rom = NesRom.Open(folder)!;
        Assert.Equal("USA", rom.RomSetName);
        Assert.Equal(IndexLength, rom.GetLength("00.LFL"));
    }

    /// <summary>The folder as it is, holding the cartridge in either form, is the NES release.</summary>
    [Fact]
    public void Identifies_the_cartridge_folder()
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path)
        {
            return;
        }

        var game = ScummDetector.Identify(new DirectoryGameContainer(path));

        Assert.Equal("Maniac Mansion (NES, English, NES)", game?.Description);
    }

    /// <summary>
    /// Every room file comes out of the cartridge, each resource as long as the tables say; the
    /// index finds them all; and every room, script, sound and costume it names reads whole.
    /// </summary>
    [Fact]
    public void Every_resource_comes_out_of_the_cartridge()
    {
        if (Program() is not { } program)
        {
            return;
        }

        var rom = NesRom.FromBytes("nes", program)!;
        var index = ResourceIndex.Parse(LflFile.Read(rom, 0));
        var loader = new ResourceLoader(rom, index);

        Assert.True(index.IsNes);
        Assert.Equal(1, index.Version);
        Assert.Equal(775, index.GlobalObjectCount);
        Assert.Equal(55, index.Count(ResourceType.Room));
        Assert.Equal(80, index.Count(ResourceType.Costume));
        Assert.Equal(200, index.Count(ResourceType.Script));
        Assert.Equal(100, index.Count(ResourceType.Sound));
        Assert.Equal(55, rom.EnumerateFiles().Count());

        // each room file is its resources end to end, then its two closing bytes
        for (int room = 1; room < 55; room++)
        {
            var file = LflFile.Read(rom, room);
            int at = 0;
            while (at < file.Length - 2)
            {
                at += BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at));
            }

            Assert.Equal(file.Length - 2, at);
            Assert.Equal([0xD1, 0xF5], file[^2..]);
        }

        // the rooms 1 to 54, the scripts and sounds the cartridge has, the 25 costumes and the
        // costume parts, room graphics, font translation and prepositions after them
        Assert.Equal(54, Enumerable.Range(0, 55).Count(i => loader.IsReadable(ResourceType.Room, i)));
        Assert.Equal(171, Enumerable.Range(0, 200).Count(i => loader.IsReadable(ResourceType.Script, i)));
        Assert.Equal(82, Enumerable.Range(0, 100).Count(i => loader.IsReadable(ResourceType.Sound, i)));
        Assert.Equal(79, Enumerable.Range(0, 80).Count(i => loader.IsReadable(ResourceType.Costume, i)));
    }

    /// <summary>The cartridge, a .prg or a .nes under any name, opens as the NES release, its index read from the cartridge.</summary>
    [Fact]
    public void Opens_the_cartridge()
    {
        if (Program() is not { } program)
        {
            return;
        }

        foreach (var container in new[] { new MemoryGameContainer("nes").Add("mm.prg", program), new MemoryGameContainer("nes").Add("Maniac Mansion (U).nes", Ines(program)) })
        {
            var loader = ResourceLoader.Open(container);

            Assert.True(loader.Index.IsNes);
            Assert.Equal(1, loader.Index.Version);
            Assert.IsType<NesRom>(loader.Game);
        }
    }

    /// <summary>A .prg or .nes that is no cartridge ScummVM knows is not taken for one.</summary>
    [Fact]
    public void Ignores_other_roms()
    {
        var other = new byte[ProgramLength];
        other[100] = 1;

        Assert.Null(NesRom.Open(new MemoryGameContainer("other").Add("other.prg", other)));
        Assert.Null(NesRom.Open(new MemoryGameContainer("other").Add("other.nes", Ines(other))));
        Assert.Null(NesRom.Open(new MemoryGameContainer("dos").Add("00.LFL", [0xCE, 0xF5])));
    }
}
