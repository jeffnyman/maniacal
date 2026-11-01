using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// The releases that come as images rather than loose files: the C64's disks and the NES's
/// cartridge, each seen as the DOS releases' room files, and the Atari ST's disk, whose files are
/// those room files. Mirrors <c>openDiskImage</c>, which opens a <c>.d64</c> with
/// <c>ScummDiskImage</c> and a <c>.prg</c> with <c>ScummNESFile</c>; ScummVM reads the Atari ST's
/// files once copied off its disk.
/// </summary>
internal static class GameImages
{
    /// <summary>The room files of the disks or cartridge in a folder, or the folder itself when it has none.</summary>
    public static IGameContainer Unpack(IGameContainer folder)
        => (IGameContainer?)C64DiskImage.Open(folder) ?? NesRom.Open(folder) ?? (IGameContainer?)FatDiskImage.Open(folder) ?? folder;
}
