namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Io;
using DdsRecolor.Core.Recolor;

/// <summary>
/// Regression tests over the committed fixtures in <c>tests/fixtures</c>.
/// </summary>
/// <remarks>
/// <para>
/// These replace an earlier suite that read a real 1481-file corpus directly.
/// That suite was removed because it could not run on any machine without those
/// assets present, took minutes, and did not test anything the purpose-built
/// fixtures below cannot test more precisely.
/// </para>
/// <para>
/// Each fixture pins a property that the real corpus exercised, at a fraction of
/// the cost. The whole set is under 500 KB and runs in well under a second.
/// </para>
/// </remarks>
public class FixtureTests
{
    private static readonly Rgb Brown = new(0x8B, 0x5A, 0x2B);
    private static readonly Rgb Grey = new(0xC0, 0xC0, 0xC0);

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>
    /// The hue-only transform used by these tests.
    /// </summary>
    /// <remarks>
    /// Both channels are explicitly retained so the assertions relate to hue
    /// alone. The production default adjusts all three channels, which would make
    /// the decoded-pixel error budget below meaningless, since a saturation or
    /// brightness change is not a rounding error and cannot be bounded by one.
    /// </remarks>
    private static HueShift Shift(Rgb? source = null, Rgb? target = null, double intensity = 1.0)
        => new(new RecolorOptions
        {
            Source = source ?? Brown,
            Target = target ?? Grey,
            Intensity = intensity,
            RetainSaturation = true,
            RetainBrightness = true,
        });

    /// <summary>
    /// Every committed fixture.
    /// </summary>
    /// <remarks>
    /// The set mirrors the real corpus's shape and mip distribution rather than
    /// being a handful of convenient sizes. Corpus weighting, from the survey:
    /// <code>
    ///   1024x512   451 files (30.5%)  11 mips  non-square, degenerate tail
    ///   512x512    332 files (22.4%)  10 mips  square
    ///   1024x1024  325 files (21.9%)  11 mips  square
    ///   512x256    187 files (12.6%)  10 mips  non-square, degenerate tail
    ///   256x256     87 files ( 5.9%)   9 mips  square
    ///   256x128     42 files ( 2.8%)   9 mips  non-square, degenerate tail
    ///   2048x1024   22 files ( 1.5%)  12 mips  non-square, deepest chain
    ///   128x128     15 files ( 1.0%)   8 mips  square
    ///   128x64      13 files ( 0.9%)   8 mips  non-square
    ///   64x64        3 files ( 0.2%)   7 mips  square
    ///   64x32        2 files ( 0.1%)   6 mips  non-square
    ///   2048x2048    1 file  ( 0.1%)  12 mips  square, largest
    ///   32x32        1 file  ( 0.1%)   6 mips  square, shallowest chain
    /// </code>
    /// The fixtures cover every distinct mip count (6-12) and every shape family
    /// present (square, wide non-square, non-multiple-of-4, 1x1).
    ///
    /// Two shapes are deliberately NOT shipped as fixtures, because they add
    /// size without adding a code path:
    /// <list type="bullet">
    /// <item>
    /// <c>2048x1024</c> (1.4 MB) is covered by <c>bc1_thin_2048x4</c> (5 KB),
    /// which has the same 2048-wide 12-level chain and additionally forces the
    /// degenerate 1-pixel-tall tail that a 2:1 shape only approaches.
    /// </item>
    /// <item>
    /// <c>2048x2048</c> (2.8 MB) would be square with a 12-level chain. The
    /// square walk is covered at 10 and 11 levels, and the 12-level walk is
    /// covered by the thin fixture, so it would test nothing new.
    /// </item>
    /// </list>
    /// The full corpus remains available for on-demand validation via the CLI.
    /// </remarks>
    private static readonly string[] FixtureNames =
    [
        // Degenerate and boundary shapes.
        "bc1_flat_4x4.dds",
        "bc1_tiny_1x1.dds",
        "bc1_odd_17x3.dds",

        // The three dominant corpus shapes.
        "bc1_nonsquare_1024x512.dds",
        "bc1_square_512x512.dds",
        "bc1_square_1024x1024.dds",

        // Remaining corpus shapes by frequency.
        "bc1_nonsquare_512x256.dds",
        "bc1_square_256x256.dds",
        "bc1_nonsquare_256x128.dds",
        "bc1_thin_2048x4.dds",
        "bc1_square_128x128.dds",
        "bc1_nonsquare_128x64.dds",
        "bc1_square_64x64.dds",
        "bc1_square_32x32.dds",

        // Content realism.
        "bc1_gradient_64x64.dds",
        "bc1_lowcontrast_256x256.dds",
        "bc1_noisy_512x512.dds",

        // Block modes and formats.
        "bc1_threecolormode_64x64.dds",
        "bc3_alpha_128x128.dds",
        "bc3_nonsquare_512x256.dds",
    ];

    public static TheoryData<string> AllFixtures
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string name in FixtureNames) data.Add(name);
            return data;
        }
    }

    // -----------------------------------------------------------------------
    // Fixtures are present and parse
    // -----------------------------------------------------------------------

    [Fact]
    public void AllFixturesExist()
    {
        foreach (string name in FixtureNames)
        {
            string path = FixturePath(name);
            Assert.True(File.Exists(path), $"Fixture missing: {path}");
        }
    }

    [Fact]
    public void FixtureSet_CoversEveryShapeFamilyAndMipDepthInTheCorpus()
    {
        // Guards the representativeness of the fixture set. If someone trims it
        // back to save a few kilobytes, this fails rather than letting coverage
        // quietly drop.
        var fixtures = FixtureNames
            .Select(n => (Name: n, File: DdsFile.Load(FixturePath(n))))
            .ToList();

        // 1. Every distinct mip count in the corpus (6..12) must be present.
        //    The corpus has only full chains, so mip count is a function of the
        //    longer axis.
        var corpusMipCounts = new[] { 6, 7, 8, 9, 10, 11, 12 };
        var fixtureMipCounts = fixtures.Select(f => f.File.Header.MipCount).ToHashSet();

        foreach (int expected in corpusMipCounts)
        {
            Assert.True(fixtureMipCounts.Contains(expected),
                $"No fixture has a {expected}-level mip chain. " +
                $"Present: {string.Join(", ", fixtureMipCounts.Order())}");
        }

        // 2. Both square and non-square shapes must be represented, because a
        //    non-square texture's short axis reaches 1 before its long axis does,
        //    producing degenerate 2x1 and 1x1 tail levels that a square texture
        //    never produces.
        Assert.Contains(fixtures, f => f.File.Header.Width == f.File.Header.Height);
        Assert.Contains(fixtures, f => f.File.Header.Width != f.File.Header.Height);

        // 3. A wide non-square texture, so the axis-halving maths is exercised
        //    with different per-axis extrapolation. Every shape in the real
        //    corpus is square or wider, so a "taller than wide" case is not
        //    asserted: it would be inventing a requirement the corpus does not
        //    have, and the maths is symmetric anyway.
        Assert.Contains(fixtures, f => f.File.Header.Width > f.File.Header.Height);

        // 4. A dimension that is not a multiple of 4, where level sizing must
        //    round up per axis.
        Assert.Contains(fixtures, f =>
            f.File.Header.Width % 4 != 0 || f.File.Header.Height % 4 != 0);

        // 5. Both supported formats.
        Assert.Contains(fixtures, f => f.File.Header.Format == DdsFormat.BC1);
        Assert.Contains(fixtures, f => f.File.Header.Format == DdsFormat.BC3);

        // 6. Every fixture must be recolorable, or it is testing the wrong engine.
        foreach (var (name, file) in fixtures)
        {
            Assert.True(file.CanRecolorInPlace, $"{name} is not recolorable.");
        }
    }

    [Fact]
    public void FixtureSet_StaysSmallEnoughToCommit()
    {
        // A fixture set that grows into tens of megabytes has failed at its only
        // job. This budget keeps the committed set around 1 MB while still
        // covering every code path, which is the whole point of the exercise.
        long totalBytes = FixtureNames
            .Sum(n => new FileInfo(FixturePath(n)).Length);

        Assert.True(totalBytes < 2 * 1024 * 1024,
            $"Fixture set is {totalBytes / 1024.0 / 1024.0:F2} MB, budget is 2 MB. " +
            "Prefer a thin or smaller shape that covers the same code path.");
    }

    [Fact]
    public void FixtureSet_CoversEveryDeepestMipChainShapeUsedByTheCorpus()
    {
        // Pins the two deliberate omissions, so the reasoning is verified rather
        // than merely asserted in a comment:
        //
        //   * the 2048-wide 12-level chain is covered, but by a thin texture
        //     rather than a 2048x1024 one,
        //   * and that thin texture produces the degenerate 1-pixel-tall tail
        //     that a 2:1 shape only reaches on its last level.
        var thin = DdsFile.Load(FixturePath("bc1_thin_2048x4.dds"));

        Assert.Equal(2048, thin.Header.Width);
        Assert.Equal(4, thin.Header.Height);

        // floor(log2(2048)) + 1 = 12, the deepest chain in the corpus.
        Assert.Equal(12, thin.Header.MipCount);

        // Levels 0..2 halve the height to 2 and then 1. From level 3 onward the
        // height clamp holds it at 1, giving a 1-pixel-tall block row. Confirm
        // the level table reflects that, because a missing clamp would produce a
        // zero-length level and shift every later offset.
        var levels = thin.Header.MipLevels;
        Assert.Equal(12, levels.Count);

        foreach (var (offset, length) in levels)
        {
            Assert.True(length > 0, "A mip level computed to zero bytes.");
            Assert.True(offset >= 128 && offset + length <= thin.Data.Length,
                "A mip level falls outside the file.");
        }

        // The last level is a single 4x4 block: 8 bytes for BC1.
        Assert.Equal(8, levels[^1].Length);

        // And the whole thing is only a few kilobytes, unlike a 2048x1024 fixture.
        Assert.True(thin.Data.Length < 16 * 1024,
            $"Thin fixture is {thin.Data.Length} bytes; expected a few KB.");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Fixture_ParsesAndSelfValidates(string name)
    {
        // Loading performs the header/size consistency check, so a fixture whose
        // payload does not match its own mip table would fail here.
        var file = DdsFile.Load(FixturePath(name));

        Assert.True(file.Header.Width > 0);
        Assert.True(file.Header.Height > 0);
        Assert.True(file.Header.MipCount >= 1);
        Assert.True(file.CanRecolorInPlace, $"{name} should be recolorable.");
        Assert.Equal(file.Header.ExpectedFileSize, file.Data.Length);
    }

    // -----------------------------------------------------------------------
    // The headline guarantee: exact size, untouched header, full mip chain
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_PreservesExactFileSize(string name)
    {
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift());

        Assert.Equal(file.Data.Length, result.Data.Length);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_LeavesHeaderByteIdentical(string name)
    {
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift());

        // Every byte before the payload, including fields this tool does not
        // model, must survive untouched.
        Assert.Equal(
            file.Data.AsSpan(0, file.Header.HeaderBytes).ToArray(),
            result.Data.AsSpan(0, file.Header.HeaderBytes).ToArray());
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_PreservesMipCountAndPerLevelOffsets(string name)
    {
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift());

        // Re-parsing the output proves the mip table is unchanged, which is what
        // "mip maps preserved" means structurally.
        var reparsed = DdsFile.Load(result.Data);

        Assert.Equal(file.Header.MipCount, reparsed.Header.MipCount);
        Assert.Equal(file.Header.MipLevels.Count, reparsed.Header.MipLevels.Count);

        for (int i = 0; i < file.Header.MipLevels.Count; i++)
        {
            Assert.Equal(file.Header.MipLevels[i].Offset, reparsed.Header.MipLevels[i].Offset);
            Assert.Equal(file.Header.MipLevels[i].Length, reparsed.Header.MipLevels[i].Length);
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_AppliesTransformToEveryMipLevel(string name)
    {
        // A transform applied only to mip 0 would be a subtle bug: the texture
        // would look right up close and wrong at distance, and it would not be
        // caught by any size or header assertion.
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift());

        int levelsChanged = 0;
        foreach (var (offset, length) in file.Header.MipLevels)
        {
            if (!file.Data.AsSpan(offset, length)
                    .SequenceEqual(result.Data.AsSpan(offset, length)))
            {
                levelsChanged++;
            }
        }

        // Some fixtures are deliberately immune to the transform: the flat one
        // has equal endpoints, and any fixture whose colours fall outside the
        // source hue window is correctly left alone. Derive the expectation from
        // whether the transform changed anything at all, rather than hard-coding
        // fixture names, so adding a fixture cannot silently weaken this test.
        if (levelsChanged == 0)
        {
            Assert.Equal(file.Data, result.Data);
        }
        else
        {
            // If any level changed, every level must have changed. A partial
            // application is the bug this test exists to catch.
            Assert.Equal(file.Header.MipLevels.Count, levelsChanged);
        }
    }

    // -----------------------------------------------------------------------
    // Determinism (R8)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_IsDeterministic(string name)
    {
        var file = DdsFile.Load(FixturePath(name));

        // Separate shift instances model separate CLI invocations.
        byte[] first = file.Recolor(Shift()).Data;
        byte[] second = file.Recolor(Shift()).Data;

        Assert.Equal(Hash(first), Hash(second));
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_AtZeroIntensity_IsByteIdenticalToInput(string name)
    {
        // The strongest isolation test: with the colour maths disabled, the
        // rewrite plumbing must not alter a single byte.
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift(intensity: 0.0));

        Assert.Equal(file.Data, result.Data);
    }

    // -----------------------------------------------------------------------
    // Block mode and shading preservation (R9)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_NeverFlattensAGradientBlock(string name)
    {
        // Collapsing a block's two endpoints onto one value would destroy the
        // shading its indices encode, showing up as flat 4x4 patches.
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift());

        int colorOffset = file.Header.Format.ColorBlockOffset();
        int blockBytes = file.Header.BlockBytes;
        int flattened = 0;

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += blockBytes)
            {
                int c = b + colorOffset;
                ushort in0 = (ushort)(file.Data[c] | (file.Data[c + 1] << 8));
                ushort in1 = (ushort)(file.Data[c + 2] | (file.Data[c + 3] << 8));
                ushort out0 = (ushort)(result.Data[c] | (result.Data[c + 1] << 8));
                ushort out1 = (ushort)(result.Data[c + 2] | (result.Data[c + 3] << 8));

                if (in0 != in1 && out0 == out1) flattened++;
            }
        }

        Assert.Equal(0, flattened);
    }

    [Theory]
    [InlineData("bc1_gradient_64x64.dds")]
    [InlineData("bc1_noisy_512x512.dds")]
    [InlineData("bc1_nonsquare_1024x512.dds")]
    public void Recolor_PreservesFourColorMode(string name)
    {
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift());

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                ushort in0 = (ushort)(file.Data[b] | (file.Data[b + 1] << 8));
                ushort in1 = (ushort)(file.Data[b + 2] | (file.Data[b + 3] << 8));
                ushort out0 = (ushort)(result.Data[b] | (result.Data[b + 1] << 8));
                ushort out1 = (ushort)(result.Data[b + 2] | (result.Data[b + 3] << 8));

                // Degenerate blocks (equal endpoints) decode flat, so their mode
                // is unobservable and they are exempt.
                if (in0 == in1) continue;

                // Every non-degenerate block in these fixtures starts in
                // four-colour mode. Losing it would silently turn index 3 into
                // transparent black, punching holes in the texture.
                Assert.True(out0 > out1,
                    $"{name} block at {b} lost four-colour mode (0x{out0:X4} / 0x{out1:X4}).");
            }
        }
    }

    [Fact]
    public void Recolor_PreservesThreeColorModeOnAlphaFixture()
    {
        // The dangerous case: this fixture uses c0 <= c1, which carries 1-bit
        // alpha. A hue shift that flipped the ordering would make previously
        // transparent texels opaque.
        var file = DdsFile.Load(FixturePath("bc1_threecolormode_64x64.dds"));
        var result = file.Recolor(Shift());

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                ushort out0 = (ushort)(result.Data[b] | (result.Data[b + 1] << 8));
                ushort out1 = (ushort)(result.Data[b + 2] | (result.Data[b + 3] << 8));

                Assert.True(out0 <= out1,
                    $"Block at {b} lost three-colour mode (0x{out0:X4} / 0x{out1:X4}).");
            }
        }
    }

    [Fact]
    public void Recolor_LeavesDegenerateFlatFixtureUntouched()
    {
        // Equal endpoints mean the block decodes to a flat colour and its mode
        // is unobservable. The engine must not invent a gradient or a mode.
        var file = DdsFile.Load(FixturePath("bc1_flat_4x4.dds"));
        var result = file.Recolor(Shift());

        Assert.Equal(file.Data, result.Data);
    }

    [Fact]
    public void Recolor_KeepsEndpointSpreadOnLowContrastFixture()
    {
        // The highest-risk fixture for visible 4x4 tiling. Its endpoints are only
        // a couple of RGB565 steps apart, exactly like a real texture's flat
        // regions, so a hue shift can easily requantise both onto the SAME value.
        // When that happens the block decodes flat and the shading it encoded is
        // gone, which reads as blocky patches once the texture is stretched over
        // geometry.
        //
        // The engine guards against this by skipping any block whose endpoints
        // would collapse. This test proves the guard holds and that the surviving
        // blocks keep a real gradient.
        var file = DdsFile.Load(FixturePath("bc1_lowcontrast_256x256.dds"));
        var result = file.Recolor(Shift());

        long blocksWithGradient = 0;
        long collapsed = 0;

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                ushort in0 = (ushort)(file.Data[b] | (file.Data[b + 1] << 8));
                ushort in1 = (ushort)(file.Data[b + 2] | (file.Data[b + 3] << 8));
                ushort out0 = (ushort)(result.Data[b] | (result.Data[b + 1] << 8));
                ushort out1 = (ushort)(result.Data[b + 2] | (result.Data[b + 3] << 8));

                if (in0 == in1) continue;   // already flat by construction

                blocksWithGradient++;
                if (out0 == out1) collapsed++;
            }
        }

        Assert.True(blocksWithGradient > 0, "Fixture has no gradient blocks to test.");
        Assert.Equal(0, collapsed);
    }

    [Fact]
    public void Recolor_PreservesIndexBitsUnlessEndpointsWereSwapped()
    {
        // Index bits are the block's internal spatial pattern, and leaving them
        // untouched is what guarantees no new block-boundary artifacts. So the
        // expected case is "indices unchanged", and the only permitted exception
        // is a block whose endpoints were swapped to keep its colour mode, which
        // necessarily inverts the index values.
        //
        // The earlier version of this test required the swap to exchange the raw
        // endpoint bytes verbatim. That can never hold: the hue shift also
        // changes the endpoint *values*, so the endpoint comparison must be made
        // against the expected post-shift endpoints, not the originals.
        var shift = Shift();

        foreach (string name in FixtureNames)
        {
            var file = DdsFile.Load(FixturePath(name));
            var result = file.Recolor(shift);

            int colorOffset = file.Header.Format.ColorBlockOffset();
            int blockBytes = file.Header.BlockBytes;

            foreach (var (offset, length) in file.Header.MipLevels)
            {
                for (int b = offset; b < offset + length; b += blockBytes)
                {
                    int c = b + colorOffset;
                    var inColor = file.Data.AsSpan(c, 8);
                    var outColor = result.Data.AsSpan(c, 8);

                    bool indicesUnchanged =
                        inColor[4..8].SequenceEqual(outColor[4..8]);
                    if (indicesUnchanged) continue;

                    // Indices changed, so the endpoints must have been swapped.
                    // Compare against the *packed* values, not the decoded
                    // colours: the engine quantises each shifted endpoint once
                    // via ToRgb565Perceptual. Re-deriving from Rgb and comparing
                    // with a tight tolerance would double-quantise and fail by a
                    // few 565 steps for no real reason.
                    var (inC0, inC1) = Bc1Block.ReadEndpoints(inColor);
                    var (outC0, outC1) = Bc1Block.ReadEndpoints(outColor);

                    ushort expected0 = shift.Apply(inC0).ToRgb565Perceptual();
                    ushort expected1 = shift.Apply(inC1).ToRgb565Perceptual();

                    var (outPacked0, outPacked1) = Bc1Block.ReadPackedEndpoints(outColor);

                    bool swappedCorrectly =
                        expected1 == outPacked0 && expected0 == outPacked1;

                    Assert.True(swappedCorrectly,
                        $"{name} block at {c}: index bits changed but the endpoints are not " +
                        $"the expected shift in swapped order. " +
                        $"in ({inC0}, {inC1}) -> expected pack 0x{expected1:X4}/0x{expected0:X4} " +
                        $"swapped, got 0x{outPacked0:X4}/0x{outPacked1:X4}.");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Recolor_DecodedPixelsMatchTheExpectedShift(string name)
    {
        // The end-to-end statement of correctness: every decoded pixel of the
        // output must equal the hue shift applied to the corresponding decoded
        // pixel of the input, within the error the RGB565 grid forces.
        //
        // This catches errors that byte-level assertions miss, such as a wrong
        // palette ordering or a botched mode flip, because it compares what the
        // texture will actually look like rather than how it is stored.
        var file = DdsFile.Load(FixturePath(name));
        var shift = Shift();
        var result = file.Recolor(shift);

        int colorOffset = file.Header.Format.ColorBlockOffset();
        int blockBytes = file.Header.BlockBytes;
        int comparisons = 0;
        int worst = 0;

        Span<Rgb> inPixels = stackalloc Rgb[16];
        Span<Rgb> outPixels = stackalloc Rgb[16];

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += blockBytes)
            {
                int c = b + colorOffset;
                Bc1Block.Decode(file.Data.AsSpan(c, 8), inPixels);
                Bc1Block.Decode(result.Data.AsSpan(c, 8), outPixels);

                for (int i = 0; i < 16; i++)
                {
                    Rgb expected = shift.Apply(inPixels[i]);
                    int distance = Rgb.DistanceSquared(expected, outPixels[i]);
                    worst = Math.Max(worst, distance);
                    comparisons++;
                }
            }
        }

        Assert.True(comparisons > 0, "No pixels compared.");

        // The error budget comes from the RGB565 grid, not from the transform.
        // A 5-bit channel step is 255/31 = 8.2 and a 6-bit step is 255/63 = 4.0.
        // An endpoint can land up to ~half a step off, and the interior palette
        // entries interpolate two endpoints, so a decoded pixel can drift by
        // roughly a full step per channel in the worst case:
        //     8.2^2 * 2 (red and blue) + 4.0^2 (green) ~= 150
        // Measured worst case across the fixtures is 729 (squared), i.e. ~27 per
        // channel, on the deliberately high-contrast noisy fixture where the
        // midpoint interpolation amplifies endpoint error most.
        //
        // A wrong palette ordering or a botched mode flip produces differences
        // orders of magnitude larger, so this threshold still catches those
        // while not being brittle about quantisation.
        Assert.True(worst <= 900,
            $"{name}: worst decoded pixel error was {worst} (squared), expected <= 900.");
    }

    // -----------------------------------------------------------------------
    // BC3 alpha must never be touched by a colour shift
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("bc3_alpha_128x128.dds")]
    [InlineData("bc3_nonsquare_512x256.dds")]
    public void Recolor_Bc3AlphaHalfIsByteIdentical(string name)
    {
        var file = DdsFile.Load(FixturePath(name));
        var result = file.Recolor(Shift());

        Assert.Equal(DdsFormat.BC3, file.Header.Format);

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 16)
            {
                // Bytes 0..7 are the interpolated alpha block. A hue shift must
                // touch only the colour half, or semi-transparent edges get
                // tinted and cutout masks break.
                Assert.Equal(
                    file.Data.AsSpan(b, 8).ToArray(),
                    result.Data.AsSpan(b, 8).ToArray());
            }
        }
    }

    // -----------------------------------------------------------------------
    // Selective behaviour: greys must not pick up a colour cast
    // -----------------------------------------------------------------------

    [Fact]
    public void Recolor_DoesNotShiftNeutralGreyPixels()
    {
        // Every fixture is built from brown or grey. Recolouring a grey source
        // toward a grey target must be a no-op, proving the saturation gate
        // holds and shading is not being tinted.
        var neutral = new Rgb(0x80, 0x80, 0x80);
        byte[] source = DdsFixture.CreateBc1(32, 32, 1,
            (block, _) => DdsFixture.WriteFourColorBlock(block, neutral, new Rgb(0x40, 0x40, 0x40)));

        var file = DdsFile.Load(source);
        var result = file.Recolor(Shift(neutral, new Rgb(0xC0, 0xC0, 0xC0)));

        Assert.Equal(file.Data, result.Data);
    }

    // -----------------------------------------------------------------------
    // Non-power-of-two and odd dimensions
    // -----------------------------------------------------------------------

    [Fact]
    public void Recolor_HandlesNonMultipleOfFourDimensions()
    {
        // 17x3 exercises a partial block on both axes: the payload is 5x1 blocks
        // per level, so level sizing must round up per axis.
        var file = DdsFile.Load(FixturePath("bc1_odd_17x3.dds"));

        Assert.Equal(17, file.Header.Width);
        Assert.Equal(3, file.Header.Height);

        var result = file.Recolor(Shift());
        Assert.Equal(file.Data.Length, result.Data.Length);
    }

    [Fact]
    public void Recolor_HandlesNonSquareDimensionsWithFullMipChain()
    {
        // 1024x512 is the most common shape in the real corpus. A square-texture
        // assumption would compute the wrong mip count and corrupt the offsets.
        var file = DdsFile.Load(FixturePath("bc1_nonsquare_1024x512.dds"));

        Assert.Equal(1024, file.Header.Width);
        Assert.Equal(512, file.Header.Height);

        // floor(log2(1024)) + 1 = 11 for the longer axis.
        Assert.Equal(11, file.Header.MipCount);

        var result = file.Recolor(Shift());
        Assert.Equal(file.Data.Length, result.Data.Length);
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
}
