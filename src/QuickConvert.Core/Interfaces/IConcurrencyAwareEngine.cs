namespace QuickConvert.Core.Interfaces;

/// <summary>Optional hint (D7). Engines without a hint simply do not implement this.</summary>
public interface IConcurrencyAwareEngine
{
    int RecommendedMaxDegreeOfParallelism { get; }
}
