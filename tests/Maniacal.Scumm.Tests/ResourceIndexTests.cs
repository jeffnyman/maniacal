using System.Buffers.Binary;
using Daventry.Core.Storage;
using Maniacal.Scumm.Resources;

namespace Maniacal.Scumm.Tests;

public class ResourceIndexTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";
    private const string C64Folder = "maniac_mansion_c64";

    [Fact]
    public void Lfl_decoding_is_xor_ff()
    {
        byte[] data = [0x00, 0xFF, 0x5A, 0xCE];

        LflFile.Decode(data);

        Assert.Equal([0xFF, 0x00, 0xA5, 0x31], data);
        Assert.Equal("07.LFL", LflFile.NameOf(7));
    }

    [Fact]
    public void Unknown_magic_is_rejected()
    {
        // the NES index, which needs its own reader
        var ex = Assert.Throws<InvalidDataException>(() => ResourceIndex.Parse([0x43, 0x46, 0, 0]));
        Assert.Contains("0x4643", ex.Message);
    }

    [Fact]
    public void Truncated_index_is_rejected()
    {
        byte[] data = new byte[100];
        BinaryPrimitives.WriteUInt16LittleEndian(data, ResourceIndex.ClassicMagic);

        Assert.Throws<InvalidDataException>(() => ResourceIndex.Parse(data));
    }

    [Theory]
    [InlineData(V1Folder, 1, 800, 55, 35, 200, 100)]
    [InlineData(V2Folder, 2, 780, 61, 40, 179, 120)]
    [InlineData(C64Folder, 0, 256, 55, 25, 160, 70)]
    public void Index_has_the_tables_of_its_version(string folder, int version, int objects, int rooms, int costumes, int scripts, int sounds)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var index = loader.Index;
        Assert.Equal(version, index.Version);
        Assert.Equal(objects, index.GlobalObjectCount);
        Assert.Equal(objects, index.ObjectStates.Count);
        Assert.Equal(rooms, index.Count(ResourceType.Room));
        Assert.Equal(costumes, index.Count(ResourceType.Costume));
        Assert.Equal(scripts, index.Count(ResourceType.Script));
        Assert.Equal(sounds, index.Count(ResourceType.Sound));
        Assert.All(index.ObjectOwners, owner => Assert.InRange(owner, 0, 0x0F));
        Assert.All(index.ObjectStates, state => Assert.InRange(state, 0, 0x0F));
    }

    [Theory]
    [InlineData(V1Folder, 52)]
    [InlineData(V2Folder, 53)]
    [InlineData(C64Folder, 52)]
    public void Every_room_file_is_in_the_index_and_loads(string folder, int lastRoom)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var game = loader.Game;
        for (int room = 1; room <= lastRoom; room++)
        {
            Assert.True(LflFile.Exists(game, room), $"{LflFile.NameOf(room)} missing");
            Assert.Equal(new ResourceLocation(room, 0), loader.Index.Locate(ResourceType.Room, room));

            var data = loader.Load(ResourceType.Room, room);
            Assert.NotNull(data);
            Assert.True(data.Value.Length > 2);
        }

        Assert.False(LflFile.Exists(game, lastRoom + 1));
    }

    /// <summary>
    /// A room file is a chain of resources, each starting with its own size. Version 1 files end
    /// with one or two bytes of padding after the last resource; version 2 files end exactly, as do
    /// the C64's, which are made of just their resources.
    /// </summary>
    [Theory]
    [InlineData(V1Folder, 2)]
    [InlineData(V2Folder, 0)]
    [InlineData(C64Folder, 0)]
    public void Room_files_are_chains_of_sized_resources(string folder, int maxTrailer)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        foreach (int room in RoomsWithFiles(loader, folder))
        {
            var file = loader.RoomFile(room).Span;
            int end = ResourceStarts(file).Last();
            int last = BinaryPrimitives.ReadUInt16LittleEndian(file[end..]);
            Assert.InRange(file.Length - (end + last), 0, maxTrailer);
        }
    }

    /// <summary>
    /// How many index entries of each type point at the start of a real resource. The rest are
    /// entry 0, version 1's unused table slots, and a few stale entries in every release. Pinned
    /// so a change in how the index is read shows up.
    /// </summary>
    [Theory]
    [InlineData(V1Folder, 23, 172, 64)]
    [InlineData(V2Folder, 24, 169, 59)]
    [InlineData(C64Folder, 24, 154, 66)]
    public void Index_entries_point_at_resource_starts(string folder, int costumes, int scripts, int sounds)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var starts = RoomsWithFiles(loader, folder)
            .ToDictionary(room => room, room => ResourceStarts(loader.RoomFile(room).Span).ToHashSet());

        int Valid(ResourceType type) => loader.Index[type].Count(location =>
            location.IsPresent && location.Offset > 0
            && starts.TryGetValue(location.Room, out var set) && set.Contains(location.Offset));

        Assert.Equal(costumes, Valid(ResourceType.Costume));
        Assert.Equal(scripts, Valid(ResourceType.Script));
        Assert.Equal(sounds, Valid(ResourceType.Sound));

        // and those entries load as whole resources
        foreach (var type in new[] { ResourceType.Costume, ResourceType.Script, ResourceType.Sound })
        {
            for (int i = 0; i < loader.Index.Count(type); i++)
            {
                var location = loader.Index.Locate(type, i);
                if (location.Offset > 0 && starts.TryGetValue(location.Room, out var set) && set.Contains(location.Offset))
                {
                    Assert.NotNull(loader.Load(type, i));
                }
            }
        }
    }

    [Fact]
    public void Absent_entries_load_as_null()
    {
        if (Open(V2Folder) is not { } loader)
        {
            return;
        }

        Assert.Null(loader.Load(ResourceType.Room, 60));
        Assert.Null(loader.Load(ResourceType.Script, 1000));
        Assert.Equal(ResourceLocation.None, loader.Index.Locate(ResourceType.Sound, -1));
    }

    private static ResourceLoader? Open(string folder)
        => GameFolders.Find(folder) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;

    private static IEnumerable<int> RoomsWithFiles(ResourceLoader loader, string folder)
        => Enumerable.Range(1, loader.Index.Count(ResourceType.Room) - 1).Where(room => LflFile.Exists(loader.Game, room));

    /// <summary>Offsets of the resources in a room file, following the size words from offset 0.</summary>
    private static List<int> ResourceStarts(ReadOnlySpan<byte> file)
    {
        var starts = new List<int>();
        int position = 0;
        while (position + 2 <= file.Length)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(file[position..]);
            if (size < 2 || position + size > file.Length)
            {
                break;
            }

            starts.Add(position);
            position += size;
        }

        return starts;
    }
}
