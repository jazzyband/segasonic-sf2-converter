using System.Text;

namespace SegaSonicConverter;

internal readonly record struct Generator(ushort Op, ushort Value)
{
    public Generator(int op, int value) : this((ushort)op, (ushort)value) { }
    public static Generator Range(int lo, int hi) => new(43, (lo & 255) | ((hi & 255) << 8));
    public int Lo => Value & 255;
    public int Hi => Value >> 8;
}
internal sealed record SfSample(string Name, uint Start, uint End, uint LoopStart, uint LoopEnd, uint Rate, byte Root, sbyte Correction);
internal sealed record SfInstrument(string Name, List<List<Generator>> Zones);
internal sealed record SfPreset(string Name, int Program, int Bank, int InstrumentId, int Coarse, int Attenuation);

internal sealed class SoundFont
{
    private readonly MemoryStream _pcm = new();
    public List<SfSample> Samples { get; } = new();
    public List<SfInstrument> Instruments { get; } = new();
    public List<SfPreset> Presets { get; } = new();
    public int AddSample(string name, short[] pcm, int rate, int root = 60, int pitch = 0, int? start = null, int? end = null)
    {
        uint offset = checked((uint)(_pcm.Length / 2));
        byte[] data = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, data, 0, data.Length);
        _pcm.Write(data);
        _pcm.Write(new byte[92]); // 46 points of SoundFont sample guard
        int loopStart = start ?? pcm.Length / 3;
        int loopEnd = end ?? pcm.Length * 2 / 3;
        Samples.Add(new SfSample(name, offset, offset + (uint)pcm.Length, offset + (uint)loopStart,
            offset + (uint)loopEnd, (uint)rate, (byte)root, checked((sbyte)pitch)));
        return Samples.Count - 1;
    }
    public int AddInstrument(string name, List<List<Generator>> zones)
    {
        int id = Instruments.Count;
        Instruments.Add(new SfInstrument(name, zones));
        return id;
    }
    public void AddPreset(string name, int program, int bank, int instrumentId, int coarse = 0, int attenuation = 0) =>
        Presets.Add(new SfPreset(name, program, bank, instrumentId, coarse, attenuation));
    public static int Timecents(double seconds) => (int)Math.Round(1200 * Math.Log2(Math.Max(.0001, seconds)));

    public static List<Generator> Zone(int sample, int? root = 60, int fine = 0, int? low = null,
        int? high = null, int pan = 0, bool loop = false, double release = .3)
    {
        var zone = new List<Generator>();
        if (low is not null && high is not null) zone.Add(Generator.Range(low.Value, high.Value));
        if (root is not null) zone.Add(new Generator(58, root.Value));
        if (fine != 0) zone.Add(new Generator(52, fine));
        if (pan != 0) zone.Add(new Generator(17, pan));
        if (loop) zone.Add(new Generator(54, 1));
        zone.Add(new Generator(38, Timecents(release)));
        zone.Add(new Generator(53, sample));
        return zone;
    }

    public void Write(string path)
    {
        using var phdr = new MemoryStream(); using var pbag = new MemoryStream(); using var pgen = new MemoryStream();
        using var inst = new MemoryStream(); using var ibag = new MemoryStream(); using var igen = new MemoryStream();
        using var samples = new MemoryStream();
        using var pw = new BinaryWriter(phdr); using var pb = new BinaryWriter(pbag); using var pg = new BinaryWriter(pgen);
        using var iw = new BinaryWriter(inst); using var ib = new BinaryWriter(ibag); using var ig = new BinaryWriter(igen);
        using var sw = new BinaryWriter(samples);
        int pbagCount = 0, pgenCount = 0, ibagCount = 0, igenCount = 0;
        foreach (SfPreset p in Presets.OrderBy(p => p.Bank).ThenBy(p => p.Program))
        {
            Name(pw, p.Name, 20); pw.Write((ushort)p.Program); pw.Write((ushort)p.Bank); pw.Write((ushort)pbagCount); pw.Write(0u); pw.Write(0u); pw.Write(0u);
            pb.Write((ushort)pgenCount); pb.Write((ushort)0); pbagCount++;
            if (p.Coarse != 0) { pg.Write((ushort)51); pg.Write((ushort)p.Coarse); pgenCount++; }
            if (p.Attenuation != 0) { pg.Write((ushort)48); pg.Write((ushort)p.Attenuation); pgenCount++; }
            pg.Write((ushort)41); pg.Write((ushort)p.InstrumentId); pgenCount++;
        }
        Name(pw, "EOP", 20); pw.Write((ushort)0); pw.Write((ushort)0); pw.Write((ushort)pbagCount); pw.Write(0u); pw.Write(0u); pw.Write(0u);
        pb.Write((ushort)pgenCount); pb.Write((ushort)0); pg.Write(0u);

        foreach (SfInstrument instrument in Instruments)
        {
            Name(iw, instrument.Name, 20); iw.Write((ushort)ibagCount);
            foreach (var zone in instrument.Zones)
            {
                ib.Write((ushort)igenCount); ib.Write((ushort)0); ibagCount++;
                foreach (Generator gen in zone) { ig.Write(gen.Op); ig.Write(gen.Value); igenCount++; }
            }
        }
        Name(iw, "EOI", 20); iw.Write((ushort)ibagCount);
        ib.Write((ushort)igenCount); ib.Write((ushort)0); ig.Write(0u);
        foreach (SfSample s in Samples)
        {
            Name(sw, s.Name, 20); sw.Write(s.Start); sw.Write(s.End); sw.Write(s.LoopStart); sw.Write(s.LoopEnd);
            sw.Write(s.Rate); sw.Write(s.Root); sw.Write(s.Correction); sw.Write((ushort)0); sw.Write((ushort)1);
        }
        uint limit = checked((uint)(_pcm.Length / 2));
        Name(sw, "EOS", 20); for (int i = 0; i < 4; i++) sw.Write(limit);
        sw.Write(44100u); sw.Write((byte)0); sw.Write((sbyte)0); sw.Write((ushort)0); sw.Write((ushort)1);

        byte[] info = List("INFO", Chunk("ifil", new byte[] { 2, 0, 1, 0 }),
            Chunk("isng", Encoding.ASCII.GetBytes("EMU8000\0")),
            Chunk("INAM", Encoding.ASCII.GetBytes("SegaSonic FM + PCM\0")),
            Chunk("IENG", Encoding.ASCII.GetBytes("SegaSonic user asset conversion\0")),
            Chunk("ICMT", Encoding.ASCII.GetBytes("FM synthesized by SegaSonic Decomp YM3438; PCM from user's game assets.\0")),
            Chunk("ISFT", Encoding.ASCII.GetBytes("SegaSonicConverter C#\0")));
        byte[] pdta = List("pdta", Chunk("phdr", phdr.ToArray()), Chunk("pbag", pbag.ToArray()),
            Chunk("pmod", new byte[10]), Chunk("pgen", pgen.ToArray()), Chunk("inst", inst.ToArray()),
            Chunk("ibag", ibag.ToArray()), Chunk("imod", new byte[10]), Chunk("igen", igen.ToArray()),
            Chunk("shdr", samples.ToArray()));
        // Stream the ~80 MB sample pool directly to file. don't embed it in another full-sized byte array
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path); using var writer = new BinaryWriter(output);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        uint dataLength = checked((uint)(4L + info.LongLength + 12L + 8L + _pcm.Length + (_pcm.Length % 2) + pdta.LongLength));
        writer.Write(dataLength); writer.Write(Encoding.ASCII.GetBytes("sfbk"));
        writer.Write(info);
        writer.Write(Encoding.ASCII.GetBytes("LIST"));
        writer.Write(checked((uint)(4L + 8L + _pcm.Length + (_pcm.Length % 2))));
        writer.Write(Encoding.ASCII.GetBytes("sdta"));
        writer.Write(Encoding.ASCII.GetBytes("smpl")); writer.Write(checked((uint)_pcm.Length));
        _pcm.Position = 0; _pcm.CopyTo(output);
        if ((_pcm.Length & 1) != 0) writer.Write((byte)0);
        writer.Write(pdta);
        Console.WriteLine($"SoundFont: {Samples.Count} sample headers, {Instruments.Count} instruments, {Presets.Count} presets");
    }
    private static void Name(BinaryWriter w, string s, int size)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        w.Write(bytes, 0, Math.Min(bytes.Length, size - 1));
        w.Write(new byte[size - Math.Min(bytes.Length, size - 1)]);
    }
    private static byte[] Chunk(string tag, byte[] payload)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(tag)); w.Write((uint)payload.Length); w.Write(payload);
        if ((payload.Length & 1) != 0) w.Write((byte)0);
        return ms.ToArray();
    }
    private static byte[] List(string listName, params byte[][] pieces)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(listName)); foreach (var c in pieces) w.Write(c);
        return Chunk("LIST", ms.ToArray());
    }
}
