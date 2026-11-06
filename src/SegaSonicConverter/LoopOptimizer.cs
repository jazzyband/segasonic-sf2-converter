namespace SegaSonicConverter;

internal readonly record struct LoopResult(bool OneShot, int Start, int End, bool Stable, double Score);

internal static class LoopOptimizer
{
    private const int Rate = 44100;
    private static int Position(double seconds) => (int)(seconds * Rate);
    private static double Rms(short[] x, int low, int high)
    {
        double sum = 0;
        for (int i = low; i < high; i++) sum += (double)x[i] * x[i];
        return Math.Sqrt(sum / (high - low));
    }
    private static double Rms(short[] x, double from, double to) => Rms(x, Position(from), Position(to));
    private static int[] Crossings(short[] x, int low, int high)
    {
        var items = new List<int>();
        for (int i = low; i < high; i++) if (x[i - 1] <= 0 && x[i] > 0) items.Add(i);
        if (items.Count == 0) for (int i = low; i < high; i += Math.Max(1, Position(.002))) items.Add(i);
        if (items.Count <= 32) return items.ToArray();
        int[] sampled = new int[32];
        for (int i = 0; i < 32; i++) sampled[i] = items[(int)Math.Round(i * (items.Count - 1.0) / 31)];
        return sampled;
    }
    public static LoopResult? Optimize(short[] pcm)
    {
        if (pcm.Length < Rate * 1.48) return null;
        double attack = Rms(pcm, .07, .17), late = Rms(pcm, 1.24, 1.34);
        if (attack < 3 && late < 3) return null;
        if (late / Math.Max(attack, 1) < .025 && late < 110) return new LoopResult(true, 0, 0, false, 0);
        double a = Rms(pcm, .83, .94), b = Rms(pcm, 1.23, 1.34);
        bool stable = Math.Abs(Math.Log(Math.Max(.01, b / Math.Max(1, a)))) < .20;
        int[] starts = Crossings(pcm, Position(stable ? .70 : 1.02), Position(stable ? 1.02 : 1.17));
        int[] ends = Crossings(pcm, Position(1.31), Position(1.41));
        if (starts.Length == 0 || ends.Length == 0) return null;
        int width = (int)Math.Round(.006 * Rate);
        int envelopeWidth = Position(.035);
        double[] levelsS = starts.Select(s => Rms(pcm, s - envelopeWidth, s)).ToArray();
        double[] levelsE = ends.Select(e => Rms(pcm, e - envelopeWidth, e)).ToArray();
        double[] energyS = starts.Select(s => Energy(pcm, s - width, s)).ToArray();
        double[] energyE = ends.Select(e => Energy(pcm, e - width, e)).ToArray();
        double lowest = double.MaxValue;
        int chosenStart = -1, chosenEnd = -1;
        for (int i = 0; i < starts.Length; i++)
        {
            if (levelsS[i] < Math.Max(5, late * .28)) continue;
            for (int j = 0; j < ends.Length; j++)
            {
                if (levelsE[j] < Math.Max(5, late * .28)) continue;
                double length = (ends[j] - starts[i]) / (double)Rate;
                if (length < .22) continue;
                double diff = Distance(pcm, starts[i] - width, ends[j] - width, width);
                double shape = diff / Math.Max(100, energyS[i] + energyE[j]);
                double level = Math.Abs(Math.Log(Math.Max(levelsS[i], 1) / Math.Max(levelsE[j], 1)));
                double score = shape + .9 * level + .025 / Math.Max(length, .05);
                if (score < lowest) { lowest = score; chosenStart = starts[i]; chosenEnd = ends[j]; }
            }
        }
        if (chosenStart < 0) return null;
        int fade = Position(.004);
        for (int i = 0; i < fade; i++)
        {
            double alpha = i / (double)fade;
            double mixed = (1.0 - alpha) * pcm[chosenEnd - fade + i] + alpha * pcm[chosenStart - fade + i];
            pcm[chosenEnd - fade + i] = (short)Math.Clamp((int)Math.Round(mixed), short.MinValue, short.MaxValue);
        }
        return new LoopResult(false, chosenStart, chosenEnd, stable, lowest);
    }
    private static double Energy(short[] samples, int start, int end)
    {
        double sum = 0;
        for (int i = start; i < end; i++) sum += (double)samples[i] * samples[i];
        return sum / (end - start);
    }
    private static double Distance(short[] samples, int a, int b, int count)
    {
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double difference = samples[a + i] - samples[b + i];
            sum += difference * difference;
        }
        return sum / count;
    }
}
