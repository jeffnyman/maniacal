using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

public class CostumeTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    private const int Dave = 3;

    private static readonly int[] Standing = [CostumeRenderer.InitFrame, CostumeRenderer.StandFrame];

    /// <summary>
    /// Every costume the index really locates parses, and every cel its animations use decodes.
    /// Runs that reach past the command bytes are counted: there is one, in V2 costume 23.
    /// </summary>
    [Theory]
    [InlineData(V1Folder, 23, 0x57, 0)]
    [InlineData(V2Folder, 24, 0x58, 1)]
    public void Every_costume_and_its_cels_decode(string folder, int costumes, int format, int overrunningRuns)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        int parsed = 0;
        int overruns = 0;
        for (int number = 1; number <= costumes; number++)
        {
            var costume = loader.LoadCostume(number)!;
            Assert.Equal(format, costume.Format);

            for (int animation = 0; animation < costume.AnimationCount; animation++)
            {
                foreach (var limb in costume.Animation(animation))
                {
                    Assert.InRange(limb.Limb, 0, costume.LimbCount - 1);
                    if (limb.Start is not { } start)
                    {
                        continue;
                    }

                    if (start + limb.Length >= costume.CommandCount)
                    {
                        overruns++;
                    }

                    for (int index = start; index <= start + limb.Length && index < costume.CommandCount; index++)
                    {
                        int code = costume.Command(index) & 0x7F;
                        if (code is Costume.Hide or Costume.Stop or Costume.Start or Costume.Sound or Costume.Counter)
                        {
                            continue;
                        }

                        var cel = costume.Cel(limb.Limb, code);
                        var image = CostumeRenderer.DrawCel(costume, cel, number);
                        Assert.Equal(Math.Max(cel.Width, 1), image.Width);
                    }
                }
            }

            parsed++;
        }

        Assert.Equal(costumes, parsed);
        Assert.Equal(overrunningRuns, overruns);
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Facing_left_mirrors_facing_right(string folder)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var costume = loader.LoadCostume(Dave)!;
        Assert.False(costume.HasOwnLeftPictures);

        var left = CostumeRenderer.DrawFrame(costume, CostumeRenderer.Left, Standing, Dave).Pixels;
        var right = CostumeRenderer.DrawFrame(costume, CostumeRenderer.Right, Standing, Dave).Pixels;

        Assert.Equal(right.Width, left.Width);
        Assert.Equal(right.Height, left.Height);
        for (int y = 0; y < left.Height; y++)
        {
            for (int x = 0; x < left.Width; x++)
            {
                Assert.Equal(right[right.Width - 1 - x, y], left[x, y]);
            }
        }
    }

    /// <summary>Dave's torso facing front (limb 3, command 1), checked against an independent decoder.</summary>
    [Fact]
    public void V2_cel_matches_the_reference_decoder()
    {
        if (Open(V2Folder) is not { } loader)
        {
            return;
        }

        var costume = loader.LoadCostume(Dave)!;
        var cel = costume.Cel(3, 1);

        Assert.Equal((22, 28, 64, 26), (cel.Width, cel.Height, cel.RelX, cel.RelY));
        Assert.Equal(TorsoMd5, Md5.ComputeHex(CostumeRenderer.DrawCel(costume, cel, Dave).Pixels));
    }

    /// <summary>Pinned after checking the figures by eye: Dave standing, facing front.</summary>
    [Theory]
    [InlineData(V1Folder, V1StandingMd5)]
    [InlineData(V2Folder, V2StandingMd5)]
    public void Dave_standing_is_stable(string folder, string md5)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var frame = CostumeRenderer.DrawFrame(loader.LoadCostume(Dave)!, CostumeRenderer.Front, Standing, Dave);

        Assert.Equal(md5, Md5.ComputeHex(frame.Pixels.Pixels));
    }

    private const string TorsoMd5 = "b96f9e12932040712d3a1d1d90fc7b99";
    private const string V1StandingMd5 = "bffff7e25a9d0818814c398be52c2e2f";
    private const string V2StandingMd5 = "f0c6f8eb6abcf771eda97f71c712b2c8";

    private static ResourceLoader? Open(string folder)
        => GameFolders.Find(folder) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
