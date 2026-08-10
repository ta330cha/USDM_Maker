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
}
