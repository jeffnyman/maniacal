using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// The room files of the DOS releases, <c>00.LFL</c> to <c>53.LFL</c>. Every byte is stored XOR-ed
/// with <see cref="EncodingKey"/>. Mirrors ScummVM's <c>ScummFile</c> opened with the encoding byte
/// that <c>getEncByte</c> gives versions 1 and 2.
/// </summary>
internal static class LflFile
{
    public const byte EncodingKey = 0xFF;

    /// <summary>The extension of the room files, but for the 1987 demo's (<see cref="DemoExtension"/>).</summary>
    public const string Extension = "LFL";

    /// <summary>The 1987 demo's room files are <c>00.MAN</c> on (<c>detection_tables.h</c>'s <c>"%02d.MAN"</c>).</summary>
    public const string DemoExtension = "MAN";

    /// <summary>The file holding a room: its number in two digits (<c>generateFilename</c>, <c>kGenRoomNum</c>).</summary>
    public static string NameOf(int room, string extension = Extension) => $"{room:D2}.{extension}";

    public static bool Exists(IGameContainer game, int room, string extension = Extension) => game.Exists(NameOf(room, extension));

    /// <summary>Reads a room file and undoes its encoding.</summary>
    public static byte[] Read(IGameContainer game, int room, string extension = Extension)
    {
        var data = game.ReadAllBytes(NameOf(room, extension));
        Decode(data);
        return data;
    }

    public static void Decode(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= EncodingKey;
        }
    }
}
