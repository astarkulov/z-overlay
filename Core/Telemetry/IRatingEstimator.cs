using System;
using System.Collections.Generic;

namespace ApexHUD.Core.Telemetry;

/// <summary>
/// Computes authentic iRacing Strength of Field (SOF) and live projected +/- iRating per class.
/// </summary>
public static class IRatingEstimator
{
    private static readonly double Br1 = 1600.0 / Math.Log(2.0); // ~2308.312

    public static int CalculateSof(IReadOnlyList<int> iRatings)
    {
        if (iRatings.Count == 0) return 0;
        double sumExp = 0.0;
        int valid = 0;
        for (int i = 0; i < iRatings.Count; i++)
        {
            int ir = Math.Max(1, iRatings[i]);
            sumExp += Math.Exp(-ir / Br1);
            valid++;
        }
        if (valid == 0 || sumExp <= 0) return 0;
        double sof = Br1 * Math.Log(valid / sumExp);
        return Math.Max(0, (int)Math.Round(sof));
    }

    /// <summary>
    /// Computes projected +/- iRating for a class field sorted by finishing position (0 = P1).
    /// </summary>
    public static int[] EstimateDeltas(IReadOnlyList<int> sortedIRatings)
    {
        int n = sortedIRatings.Count;
        var deltas = new int[n];
        if (n <= 1) return deltas;

        var expNeg = new double[n];
        for (int i = 0; i < n; i++)
            expNeg[i] = Math.Exp(-Math.Max(1, sortedIRatings[i]) / Br1);

        for (int i = 0; i < n; i++)
        {
            double expectedScore = 0.0;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                double num = (1.0 - expNeg[i]) * expNeg[j];
                double den = (1.0 - expNeg[j]) * expNeg[i] + (1.0 - expNeg[i]) * expNeg[j];
                if (den > 1e-12)
                    expectedScore += num / den;
            }

            double actualScore = (n - 1) - i;
            double fudge = ((n / 2.0) - (i + 1)) / 100.0;
            double change = (actualScore - expectedScore - fudge) * (200.0 / n);
            deltas[i] = (int)Math.Round(change);
        }

        return deltas;
    }
}
