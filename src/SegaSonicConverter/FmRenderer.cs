using System.Runtime.InteropServices;

namespace SegaSonicConverter;

internal sealed class FmRenderer : IDisposable
{
    private const int SourceRate = 55930;
    private const int TargetRate = 44100;
    private readonly IntPtr _handle;
    private readonly Render _render;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Render([In] byte[] patch, int note, int count, [Out] short[] output, int capacity);
    public FmRenderer(string libraryPath)
    {
        _handle = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        _render = Marshal.GetDelegateForFunctionPointer<Render>(NativeLibrary.GetExport(_handle, "ss_render"));
    }
    public void Dispose() => NativeLibrary.Free(_handle);

    public short[] RenderPatch(FmPatch patch, int note)
    {
        byte[] regs = new byte[25];
        int[] offsets = { 0, 8, 4, 12 };
        for (int op = 1; op <= 4; op++)
        {
            var o = patch.Operators[op];
            int index = offsets[op - 1] / 4;
            regs[index] = (byte)((o.Dt << 4) | o.Mul);
            regs[4 + index] = (byte)((o.Ks << 6) | o.Ar);
            regs[8 + index] = (byte)((o.Am << 7) | o.D1r);
            regs[12 + index] = (byte)o.D2r;
            regs[16 + index] = (byte)((o.Sl << 4) | o.Rr);
            regs[21 + index] = (byte)o.Tl;
        }
        regs[20] = (byte)((patch.Feedback << 3) | patch.Algorithm);
        int count = (int)(SourceRate * 1.6);
        short[] input = new short[count];
        int result = _render(regs, note, count, input, count);
        if (result != count) throw new InvalidOperationException($"YM3438 emulator returned {result} samples instead of {count} for note {note}");
        short[] samples = Resampler.ResamplePoly(input);
        // Preserve the exact initial crossfade behavior of the original Python builder
        int start = (int)(.70 * TargetRate);
        int end = (int)(1.46 * TargetRate);
        int fade = (int)(.025 * TargetRate);
        for (int i = 0; i < fade; i++)
        {
            double a = i / (double)(fade - 1);
            double blend = (1.0 - a) * samples[end - fade + i] + a * samples[start - fade + i];
            samples[end - fade + i] = (short)Math.Clamp((int)Math.Truncate(blend), short.MinValue, short.MaxValue);
        }
        return samples;
    }
}

// Port of scipy.signal.resample_poly for the only ratio we use (55930 -> 44100)
// Default scipy Kaiser beta=5 FIR, normalized lowpass and its default prepadding
internal static class Resampler
{
    private const int Up = 630;
    private const int Down = 799;
    private const int Half = 10 * Down;
    private const int PrePad = Down;
    private const int Remove = (Half + PrePad) / Down;
    private static readonly double[] Filter = CreateFilter();
    private static double I0(double x)
    {
        double value = 1, term = 1, squared = x * x / 4;
        for (int k = 1; k < 80; k++)
        {
            term *= squared / (k * k);
            value += term;
            if (term < value * 1e-16) break;
        }
        return value;
    }
    private static double[] CreateFilter()
    {
        double[] filter = new double[Half * 2 + 1];
        double norm = I0(5.0), sum = 0;
        for (int i = 0; i < filter.Length; i++)
        {
            int x = i - Half;
            double sinc = x == 0 ? 1.0 / Down : Math.Sin(Math.PI * x / Down) / (Math.PI * x);
            double v = sinc * I0(5.0 * Math.Sqrt(1 - (double)x * x / (Half * Half))) / norm;
            filter[i] = v; sum += v;
        }
        for (int i = 0; i < filter.Length; i++) filter[i] *= Up / sum;
        return filter;
    }
    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    private static int CeilDiv(int a, int b) => -FloorDiv(-a, b);
    public static short[] ResamplePoly(short[] input)
    {
        int nOut = (int)(((long)input.Length * Up + Down - 1) / Down);
        short[] output = new short[nOut];
        for (int t = 0; t < nOut; t++)
        {
            int location = (t + Remove) * Down - PrePad;
            int nMin = Math.Max(0, CeilDiv(location - Filter.Length + 1, Up));
            int nMax = Math.Min(input.Length - 1, FloorDiv(location, Up));
            double value = 0;
            for (int n = nMin; n <= nMax; n++) value += input[n] * Filter[location - n * Up];
            // NumPy casts a clipped floating point result to int16 by truncation
            output[t] = (short)Math.Clamp((int)Math.Truncate(value), short.MinValue, short.MaxValue);
        }
        return output;
    }
}
