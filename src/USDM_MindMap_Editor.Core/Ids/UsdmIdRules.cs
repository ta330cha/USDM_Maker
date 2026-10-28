using System.Globalization;

namespace USDM_MindMap_Editor.Core.Ids;

public static class UsdmIdRules
{
    public const int MaximumSegment = 65_535;

    public static bool IsValidSegment(string? segment)
    {
        if (segment is null || !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return false;

        if (value is < 1 or > MaximumSegment)
            return false;

        return value <= 999 ? segment.Length == 3 : segment.Length == value.ToString(CultureInfo.InvariantCulture).Length;
    }

    public static bool IsValidId(string? id, int segmentCount) =>
        id is not null && id.Split('-') is var segments && segments.Length == segmentCount && segments.All(IsValidSegment);

    public static IComparer<string> NumericSegmentComparer { get; } = new NumericSegmentIdComparer();

    private sealed class NumericSegmentIdComparer : IComparer<string>
    {
        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var leftSegments = left.Split('-');
            var rightSegments = right.Split('-');
            for (var i = 0; i < Math.Min(leftSegments.Length, rightSegments.Length); i++)
            {
                var comparison = int.Parse(leftSegments[i], CultureInfo.InvariantCulture)
                    .CompareTo(int.Parse(rightSegments[i], CultureInfo.InvariantCulture));
                if (comparison != 0) return comparison;
            }
            return leftSegments.Length.CompareTo(rightSegments.Length);
        }
    }
}
