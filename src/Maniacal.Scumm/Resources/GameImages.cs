using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// The releases that come as images rather than loose files: the C64's disks and the NES's
/// cartridge, each seen as the DOS releases' room files, and the Atari ST's disk and the Amiga's
/// two, whose files are those room files. Mirrors <c>openDiskImage</c>, which opens a <c>.d64</c> with
/// <c>ScummDiskImage</c> and a <c>.prg</c> with <c>ScummNESFile</c>; ScummVM reads the Atari ST's
/// and the Amiga's files once copied off their disks.
/// </summary>
internal static class GameImages
{
    /// <summary>The room files of the disks or cartridge in a folder, or the folder itself when it has none.</summary>
    public static IGameContainer Unpack(IGameContainer folder)
        => (IGameContainer?)C64DiskImage.Open(folder) ?? NesRom.Open(folder) ?? (IGameContainer?)FatDiskImage.Open(folder) ?? AdfDiskImage.Open(folder) ?? folder;
}
