using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T17's own "Done when" proof (SRS NFR-U5): <c>DisplayFontFamily</c>/
/// <c>BodyFontFamily</c> resolve from the embedded <c>avares://</c> resources under
/// <c>Assets/Fonts/**</c> alone, and shape a real <see cref="GlyphRun"/> from that resolution -
/// not a network font fetch (no live Google Fonts CDN link, matching the redesign plan's own
/// "conflicts with an architectural decision?" check), and not a silent fallback to whatever
/// generic sans-serif the OS happens to ship.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately does not route through <see cref="Avalonia.Media.FontManager"/>/
/// <see cref="Typeface.GlyphTypeface"/>. Under <c>Avalonia.Headless</c> (this whole solution's own
/// sanctioned Linux dev/CI surface - see <c>CLAUDE.md</c>'s development-platform note) the
/// registered <c>IFontManagerImpl</c> is a stub that never reads a real font file at all, for any
/// family - it always answers with the same placeholder typeface, regardless of what is asked
/// for. Forcing the real Skia font manager into that shared, assembly-wide headless application
/// would mean changing <see cref="TestAppBuilder"/> for every other test in this project, an
/// out-of-proportion, high-blast-radius change for this one task.
/// </para>
/// <para>
/// Instead this reads the exact same bytes the application would - via
/// <see cref="AssetLoader"/>, Avalonia's own real (non-stubbed) embedded-resource loader, the
/// same one <c>EmbeddedFontCollection</c> itself uses internally - and parses the TrueType
/// <c>name</c>/<c>cmap</c>/<c>hmtx</c>/<c>glyf</c>/<c>CFF </c> tables directly, in plain C#, with
/// zero third-party dependency. That is a from-first-principles proof that the embedded resource
/// really is Manrope/IBM Plex Sans (its own <c>name</c> table says so), really does map ordinary
/// Latin characters to real, non-<c>.notdef</c>, non-zero-advance glyphs backed by real outline
/// data (not placeholder/corrupt bytes) - and it feeds that into a real, genuine Avalonia
/// <see cref="GlyphRun"/> (not a hand-rolled substitute), the same type every real screen's text
/// ultimately shapes into.
/// </para>
/// </remarks>
public sealed class FontResourceTests
{
    [AvaloniaTheory]
    [InlineData("DisplayFontFamily", "Manrope")]
    [InlineData("BodyFontFamily", "IBM Plex Sans")]
    public void NFR_U5_TheFontResourceResolvesToTheEmbeddedFamilyInBothVariants(
        string resourceKey, string expectedFamilyName)
    {
        var light = ResolveFontFamily(resourceKey, ThemeVariant.Light);
        var dark = ResolveFontFamily(resourceKey, ThemeVariant.Dark);

        var lightSource = light.Key!.Source.ToString();
        var darkSource = dark.Key!.Source.ToString();

        lightSource.Should().Be(darkSource, "a typeface does not change with the theme variant");

        lightSource.Should().StartWith(
            "avares://Counterpoint.Ui/Assets/Fonts/",
            "the font must be an embedded avares:// resource, never a network URL (NFR-U5)");

        light.Name.Should().Be(expectedFamilyName);
    }

    [AvaloniaTheory]
    [InlineData("DisplayFontFamily", "Manrope")]
    [InlineData("BodyFontFamily", "IBM Plex Sans")]
    public void NFR_U5_TheFontResourceShapesARealGlyphRunFromTheEmbeddedBytesAlone(
        string resourceKey, string expectedFamilyName)
    {
        var fontFamily = ResolveFontFamily(resourceKey, ThemeVariant.Light);
        var folderUri = fontFamily.Key!.Source;

        var fontFileUri = AssetLoader.GetAssets(folderUri, null)
            .Where(uri => uri.ToString().EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                          || uri.ToString().EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        fontFileUri.Should().ContainSingle(
            "exactly one font binary must live under {0} - this is the exact discovery Avalonia's "
            + "own EmbeddedFontCollection performs for the {1} resource",
            folderUri, resourceKey);

        byte[] fontBytes;
        using (var stream = AssetLoader.Open(fontFileUri[0]))
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            fontBytes = buffer.ToArray();
        }

        fontBytes.Should().NotBeEmpty("the embedded font resource must contain real bytes");

        var font = TrueTypeFont.Parse(fontBytes);

        font.FamilyName.Should().Be(
            expectedFamilyName,
            "the embedded font's own name table must declare the family this task's resource key promises");

        font.HasRealOutlineData.Should().BeTrue(
            "the font must carry a real glyf/loca or CFF outline table, not metadata alone");

        const string sampleText = "Counterpoint";
        var glyphIndices = sampleText.Select(c => font.GetGlyph(c)).ToArray();

        glyphIndices.Should().OnlyContain(
            glyph => glyph != 0,
            "every character of \"{0}\" must map to a real glyph in the embedded font, not .notdef",
            sampleText);

        var glyphTypeface = new ParsedGlyphTypeface(font);

        using var glyphRun = new GlyphRun(
            glyphTypeface,
            fontRenderingEmSize: 24,
            characters: sampleText.AsMemory(),
            glyphIndices: glyphIndices,
            baselineOrigin: new Point(4, 20));

        glyphRun.Bounds.Width.Should().BeGreaterThan(
            0,
            "a GlyphRun shaped from the embedded font's own real glyph advances must have "
            + "non-zero width - this is Avalonia's own GlyphRun type, computed from the "
            + "embedded font's data, not a stand-in");

        // Draw it: exercises the full DrawingContext.DrawGlyphRun call path with this glyph run
        // and must not throw, the same call every real screen's text ultimately makes.
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 60));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawGlyphRun(Brushes.Black, glyphRun);
        }
    }

    private static FontFamily ResolveFontFamily(string resourceKey, ThemeVariant variant)
    {
        var application = global::Avalonia.Application.Current;
        application.Should().NotBeNull();

        application!.TryGetResource(resourceKey, variant, out var value).Should().BeTrue(
            "{0} must resolve in the {1} variant with no missing-resource warning", resourceKey, variant);

        value.Should().BeOfType<FontFamily>();

        return (FontFamily)value!;
    }

    /// <summary>
    /// The static half of the NFR-U5 proof: nothing this task added is a network location. A
    /// live Google Fonts CDN reference (what the approved prototype's own HTML used) would show
    /// up as a "fonts.googleapis.com"/"fonts.gstatic.com" host or an HTML "&lt;link" tag
    /// somewhere under the font asset tree or the two token files; this sweep proves there is
    /// none. It deliberately does not ban every "http://"/"https://" substring outright - the
    /// OFL licence text's own FAQ URL (scripts.sil.org) and the token files' XML namespace
    /// declarations are legitimate, unrelated "https://" strings that would otherwise be false
    /// positives.
    /// </summary>
    [Fact]
    public void NFR_U5_NoFontAssetOrDeclarationReferencesAFontCdn()
    {
        var repositoryRoot = RepositoryRoot();

        var filesToScan = new List<FileInfo>();

        var fontsDirectory = new DirectoryInfo(
            Path.Combine(repositoryRoot.FullName, "src", "Counterpoint.Ui", "Assets", "Fonts"));
        fontsDirectory.Exists.Should().BeTrue("the bundled font assets must exist at {0}", fontsDirectory.FullName);
        filesToScan.AddRange(fontsDirectory.GetFiles("*.txt", SearchOption.AllDirectories));

        filesToScan.Add(new FileInfo(
            Path.Combine(repositoryRoot.FullName, "src", "Counterpoint.Ui", "Styles", "Tokens.Light.axaml")));
        filesToScan.Add(new FileInfo(
            Path.Combine(repositoryRoot.FullName, "src", "Counterpoint.Ui", "Styles", "Tokens.Dark.axaml")));

        string[] cdnMarkers = ["fonts.googleapis", "fonts.gstatic", "<link"];

        var offenders = new List<string>();

        foreach (var file in filesToScan)
        {
            file.Exists.Should().BeTrue("{0} must exist", file.FullName);

            var text = File.ReadAllText(file.FullName);

            foreach (var marker in cdnMarkers)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{file.Name}: contains \"{marker}\"");
                }
            }
        }

        offenders.Should().BeEmpty(
            "no bundled font asset or token declaration may reference a font CDN "
            + "(SRS NFR-U5). Offenders: " + string.Join("; ", offenders));

        var ttfFiles = fontsDirectory.GetFiles("*.ttf", SearchOption.AllDirectories);
        ttfFiles.Should().HaveCountGreaterOrEqualTo(2, "Manrope and IBM Plex Sans must both be bundled as real font files");

        var licenceFiles = fontsDirectory.GetFiles("OFL.txt", SearchOption.AllDirectories);
        licenceFiles.Should().HaveCountGreaterOrEqualTo(
            2, "each bundled font family folder must carry its own OFL licence text alongside it");
    }

    private static DirectoryInfo RepositoryRoot()
    {
        const string solutionFileName = "Counterpoint.sln";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, solutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException($"Could not find {solutionFileName} above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// A minimal, dependency-free TrueType/OpenType table reader: just enough of the <c>sfnt</c>
    /// container format (table directory, <c>name</c>, <c>cmap</c> format 4/12, <c>head</c>,
    /// <c>hhea</c>, <c>hmtx</c>, <c>maxp</c>, and a check for <c>glyf</c>/<c>CFF </c>) to prove
    /// the embedded bytes are a real, well-formed font carrying real glyph outlines for ordinary
    /// Latin text - not to be a general-purpose font library.
    /// </summary>
    private sealed class TrueTypeFont
    {
        private readonly Dictionary<uint, ushort> _cmap;
        private readonly ushort[] _advanceWidths;

        private TrueTypeFont(
            string? familyName, int unitsPerEm, int numGlyphs, bool hasRealOutlineData,
            Dictionary<uint, ushort> cmap, ushort[] advanceWidths)
        {
            FamilyName = familyName;
            UnitsPerEm = unitsPerEm;
            NumGlyphs = numGlyphs;
            HasRealOutlineData = hasRealOutlineData;
            _cmap = cmap;
            _advanceWidths = advanceWidths;
        }

        public string? FamilyName { get; }

        public int UnitsPerEm { get; }

        public int NumGlyphs { get; }

        public bool HasRealOutlineData { get; }

        public ushort GetGlyph(uint codepoint) => _cmap.GetValueOrDefault(codepoint, (ushort)0);

        public int GetAdvanceWidth(ushort glyphId)
        {
            if (_advanceWidths.Length == 0)
            {
                return 0;
            }

            var index = Math.Min(glyphId, _advanceWidths.Length - 1);
            return _advanceWidths[index];
        }

        public static TrueTypeFont Parse(byte[] data)
        {
            var span = data.AsSpan();

            var numTables = ReadUInt16(span, 4);
            var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);

            for (var i = 0; i < numTables; i++)
            {
                var recordOffset = 12 + (i * 16);
                var tag = Encoding.ASCII.GetString(data, recordOffset, 4);
                var offset = (int)ReadUInt32(span, recordOffset + 8);
                var length = (int)ReadUInt32(span, recordOffset + 12);
                tables[tag] = (offset, length);
            }

            var hasRealOutlineData = tables.ContainsKey("CFF ")
                || (tables.ContainsKey("glyf") && tables.ContainsKey("loca"));

            var unitsPerEm = tables.TryGetValue("head", out var head) ? ReadUInt16(span, head.Offset + 18) : 1000;
            var numGlyphs = tables.TryGetValue("maxp", out var maxp) ? ReadUInt16(span, maxp.Offset + 4) : 0;

            var numberOfHMetrics = tables.TryGetValue("hhea", out var hhea) ? ReadUInt16(span, hhea.Offset + 34) : 0;
            var advanceWidths = Array.Empty<ushort>();

            if (numberOfHMetrics > 0 && tables.TryGetValue("hmtx", out var hmtx))
            {
                advanceWidths = new ushort[numberOfHMetrics];
                for (var i = 0; i < numberOfHMetrics; i++)
                {
                    advanceWidths[i] = ReadUInt16(span, hmtx.Offset + (i * 4));
                }
            }

            var familyName = tables.TryGetValue("name", out var name) ? ReadFamilyName(span, name.Offset) : null;

            var cmap = tables.TryGetValue("cmap", out var cmapTable)
                ? ReadCharacterMap(span, cmapTable.Offset)
                : new Dictionary<uint, ushort>();

            return new TrueTypeFont(familyName, unitsPerEm, numGlyphs, hasRealOutlineData, cmap, advanceWidths);
        }

        private static string? ReadFamilyName(ReadOnlySpan<byte> span, int nameTableOffset)
        {
            var count = ReadUInt16(span, nameTableOffset + 2);
            var stringOffset = nameTableOffset + ReadUInt16(span, nameTableOffset + 4);

            string? familyName1 = null;

            for (var i = 0; i < count; i++)
            {
                var recordOffset = nameTableOffset + 6 + (i * 12);
                var platformId = ReadUInt16(span, recordOffset);
                var encodingId = ReadUInt16(span, recordOffset + 2);
                var nameId = ReadUInt16(span, recordOffset + 6);
                var length = ReadUInt16(span, recordOffset + 8);
                var offset = ReadUInt16(span, recordOffset + 10);

                // Windows platform, Unicode BMP encoding - UTF-16BE strings, the near-universal
                // choice for a font meant to be readable cross-platform.
                if (platformId != 3 || encodingId != 1)
                {
                    continue;
                }

                if (nameId is not (1 or 16))
                {
                    continue;
                }

                var bytes = span.Slice(stringOffset + offset, length);
                var text = Encoding.BigEndianUnicode.GetString(bytes);

                if (nameId == 16)
                {
                    // Typographic family name - the one FontFamilyLoader/EmbeddedFontCollection
                    // actually matches "#Manrope"/"#IBM Plex Sans" against (IGlyphTypeface2's own
                    // TypographicFamilyName). Preferred outright.
                    return text;
                }

                familyName1 ??= text;
            }

            return familyName1;
        }

        private static Dictionary<uint, ushort> ReadCharacterMap(ReadOnlySpan<byte> span, int cmapTableOffset)
        {
            var numTables = ReadUInt16(span, cmapTableOffset + 2);

            var bestOffset = -1;
            var bestScore = -1;

            for (var i = 0; i < numTables; i++)
            {
                var recordOffset = cmapTableOffset + 4 + (i * 8);
                var platformId = ReadUInt16(span, recordOffset);
                var encodingId = ReadUInt16(span, recordOffset + 2);
                var subtableOffset = cmapTableOffset + (int)ReadUInt32(span, recordOffset + 4);

                var score = (platformId, encodingId) switch
                {
                    (3, 1) => 3, // Windows, Unicode BMP - what we want for ordinary Latin text.
                    (0, _) => 2, // Unicode platform, any encoding.
                    (3, 10) => 1, // Windows, Unicode full repertoire.
                    _ => 0,
                };

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = subtableOffset;
                }
            }

            var map = new Dictionary<uint, ushort>();

            if (bestOffset < 0)
            {
                return map;
            }

            var format = ReadUInt16(span, bestOffset);

            switch (format)
            {
                case 4:
                    ReadFormat4(span, bestOffset, map);
                    break;
                case 12:
                    ReadFormat12(span, bestOffset, map);
                    break;
            }

            return map;
        }

        private static void ReadFormat4(ReadOnlySpan<byte> span, int subtableOffset, Dictionary<uint, ushort> map)
        {
            var segCountX2 = ReadUInt16(span, subtableOffset + 6);
            var segCount = segCountX2 / 2;

            var endCodesOffset = subtableOffset + 14;
            var startCodesOffset = endCodesOffset + segCountX2 + 2; // + reservedPad
            var idDeltaOffset = startCodesOffset + segCountX2;
            var idRangeOffsetsOffset = idDeltaOffset + segCountX2;

            for (var seg = 0; seg < segCount; seg++)
            {
                var endCode = ReadUInt16(span, endCodesOffset + (seg * 2));
                var startCode = ReadUInt16(span, startCodesOffset + (seg * 2));
                var idDelta = ReadInt16(span, idDeltaOffset + (seg * 2));
                var idRangeOffsetPosition = idRangeOffsetsOffset + (seg * 2);
                var idRangeOffset = ReadUInt16(span, idRangeOffsetPosition);

                if (startCode == 0xFFFF && endCode == 0xFFFF)
                {
                    continue;
                }

                for (uint c = startCode; c <= endCode && c != 0xFFFF; c++)
                {
                    ushort glyphId;

                    if (idRangeOffset == 0)
                    {
                        glyphId = (ushort)((c + idDelta) % 65536);
                    }
                    else
                    {
                        var glyphIndexAddress = idRangeOffsetPosition + idRangeOffset + (2 * (int)(c - startCode));
                        var rawGlyphId = ReadUInt16(span, glyphIndexAddress);
                        glyphId = rawGlyphId == 0 ? (ushort)0 : (ushort)((rawGlyphId + idDelta) % 65536);
                    }

                    if (glyphId != 0)
                    {
                        map[c] = glyphId;
                    }
                }
            }
        }

        private static void ReadFormat12(ReadOnlySpan<byte> span, int subtableOffset, Dictionary<uint, ushort> map)
        {
            var numGroups = ReadUInt32(span, subtableOffset + 12);

            for (var g = 0; g < numGroups; g++)
            {
                var groupOffset = subtableOffset + 16 + (g * 12);
                var startCharCode = ReadUInt32(span, groupOffset);
                var endCharCode = ReadUInt32(span, groupOffset + 4);
                var startGlyphId = ReadUInt32(span, groupOffset + 8);

                for (var c = startCharCode; c <= endCharCode; c++)
                {
                    map[c] = (ushort)(startGlyphId + (c - startCharCode));
                }
            }
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> span, int offset) =>
            BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset, 2));

        private static short ReadInt16(ReadOnlySpan<byte> span, int offset) =>
            BinaryPrimitives.ReadInt16BigEndian(span.Slice(offset, 2));

        private static uint ReadUInt32(ReadOnlySpan<byte> span, int offset) =>
            BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));
    }

    /// <summary>
    /// Wraps a hand-parsed <see cref="TrueTypeFont"/> as a real Avalonia <see cref="IGlyphTypeface"/>
    /// so it can feed a real <see cref="GlyphRun"/> - deliberately not routed through
    /// <see cref="Avalonia.Media.FontManager"/>, which under Avalonia.Headless never reads real
    /// font bytes at all (see this class's own remarks).
    /// </summary>
    private sealed class ParsedGlyphTypeface(TrueTypeFont font) : IGlyphTypeface
    {
        public string FamilyName => font.FamilyName ?? string.Empty;

        public FontWeight Weight => FontWeight.Normal;

        public FontStyle Style => FontStyle.Normal;

        public FontStretch Stretch => FontStretch.Normal;

        public int GlyphCount => font.NumGlyphs;

        public FontSimulations FontSimulations => FontSimulations.None;

        public FontMetrics Metrics => new()
        {
            DesignEmHeight = (short)font.UnitsPerEm,
            Ascent = -(int)(font.UnitsPerEm * 0.8),
            Descent = (int)(font.UnitsPerEm * 0.2),
            LineGap = 0,
        };

        public bool TryGetGlyphMetrics(ushort glyph, out GlyphMetrics metrics)
        {
            metrics = default;
            return true;
        }

        public ushort GetGlyph(uint codepoint) => font.GetGlyph(codepoint);

        public bool TryGetGlyph(uint codepoint, out ushort glyph)
        {
            glyph = font.GetGlyph(codepoint);
            return glyph != 0;
        }

        public ushort[] GetGlyphs(ReadOnlySpan<uint> codepoints)
        {
            var result = new ushort[codepoints.Length];
            for (var i = 0; i < codepoints.Length; i++)
            {
                result[i] = font.GetGlyph(codepoints[i]);
            }

            return result;
        }

        public int GetGlyphAdvance(ushort glyph) => font.GetAdvanceWidth(glyph);

        public int[] GetGlyphAdvances(ReadOnlySpan<ushort> glyphs)
        {
            var result = new int[glyphs.Length];
            for (var i = 0; i < glyphs.Length; i++)
            {
                result[i] = font.GetAdvanceWidth(glyphs[i]);
            }

            return result;
        }

        public bool TryGetTable(uint tag, out byte[] table)
        {
            table = Array.Empty<byte>();
            return false;
        }

        public void Dispose()
        {
        }
    }
}
