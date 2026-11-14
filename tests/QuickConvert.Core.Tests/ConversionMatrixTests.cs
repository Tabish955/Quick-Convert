using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using Xunit;

namespace QuickConvert.Core.Tests;

public class ConversionMatrixTests
{
    /// <summary>
    /// Licence guard: RAR is proprietary and may only ever be read, never produced.
    /// If this test fails, do not "fix the test" — remove the target.
    /// </summary>
    [Fact]
    public void Rar_IsNeverATarget_AnywhereInTheMatrix()
    {
        var allTargets = ConversionMatrix.Sources.SelectMany(ConversionMatrix.GetTargets).ToList();

        Assert.DoesNotContain(".rar", allTargets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(".rar", ConversionMatrix.ForbiddenTargets, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rar_IsAValidSource()
    {
        Assert.True(ConversionMatrix.IsKnownSource(".rar"));
        Assert.NotEmpty(ConversionMatrix.GetTargets(".rar"));
    }

    [Fact]
    public void EveryKnownSource_HasACategory()
    {
        foreach (var source in ConversionMatrix.Sources)
        {
            Assert.NotEqual(FileCategory.Unknown, FormatRegistry.GetCategory(source));
        }
    }

    [Fact]
    public void Zip_CannotConvertToPng() =>
        Assert.False(ConversionMatrix.IsAllowed(".zip", ".png"));

    [Fact]
    public void Jpg_CanConvertToPng() =>
        Assert.True(ConversionMatrix.IsAllowed(".jpg", ".png"));

    [Theory]
    [InlineData("jpg", ".png")]      // missing dot is normalized
    [InlineData(".JPG", ".PNG")]     // case-insensitive on both sides
    public void IsAllowed_NormalizesVariants(string source, string target) =>
        Assert.True(ConversionMatrix.IsAllowed(source, target));

    [Fact]
    public void UnknownSource_HasNoTargets()
    {
        Assert.Empty(ConversionMatrix.GetTargets(".xyz"));
        Assert.Empty(ConversionMatrix.GetTargets(""));
        Assert.False(ConversionMatrix.IsKnownSource(".xyz"));
    }

    [Fact]
    public void Targets_NeverIncludeTheSourceItself()
    {
        foreach (var source in ConversionMatrix.Sources)
        {
            Assert.DoesNotContain(source, ConversionMatrix.GetTargets(source),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ReservedTargets_AreNeverInTheMatrix()
    {
        var allTargets = ConversionMatrix.Sources.SelectMany(ConversionMatrix.GetTargets).Distinct();
        foreach (var target in allTargets)
        {
            Assert.False(ConversionTargets.IsReserved(target), $"ConversionMatrix contains reserved target '{target}'.");
        }
        Assert.DoesNotContain(ConversionMatrix.Sources, ConversionTargets.IsReserved);
    }
}
