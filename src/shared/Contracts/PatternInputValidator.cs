namespace Bimwright.Ipt.Shared.Contracts;

public static class PatternInputValidator
{
    public static bool TryValidateRectangular(
        int count1,
        double spacingMm1,
        string? dir2,
        int? count2,
        double? spacingMm2,
        out string error)
    {
        if (count1 < 2) { error = "count1 must be >= 2"; return false; }
        if (spacingMm1 <= 0) { error = "spacing_mm1 must be > 0"; return false; }

        if (!string.IsNullOrWhiteSpace(dir2))
        {
            if (count2 is null or < 2) { error = "count2 must be >= 2 when dir2 is set"; return false; }
            if (spacingMm2 is null or <= 0) { error = "spacing_mm2 must be > 0 when dir2 is set"; return false; }
        }

        error = "";
        return true;
    }
}
