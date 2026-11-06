using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SegaSonicConverter;

internal sealed record Part(string Kind, int Program, int Transpose, int Volume, int Detune);
internal sealed record ProgramDef(string Kind, int Id, int Left, int Right, int Detune);
internal sealed record DrumNote(int Step, int Left, int Right, int Sample);
internal sealed record FmOperator(int Dt, int Mul, int Tl, string Mode, int Ks, int Ar, int Am, int D1r, int D2r, int Sl, int Rr);
internal sealed class FmPatch
{
    public int Algorithm { get; set; }
    public int Feedback { get; set; }
    public Dictionary<int, FmOperator> Operators { get; } = new();
}

internal sealed class Assets : IDisposable
{
    private readonly FileStream _stream;
    private readonly ZipArchive _zip;
    public SortedDictionary<int, ProgramDef> Programs { get; } = new();
    public Dictionary<int, Dictionary<int, Part>> Templates { get; } = new();
    public SortedDictionary<int, SortedDictionary<int, DrumNote>> Kits { get; } = new();
    public Dictionary<int, FmPatch> Patches { get; } = new();
    public SortedDictionary<int, string> Samples { get; } = new();
    public List<string> MusicFiles { get; } = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static int Dec(string s) => int.Parse(s, Inv);
    private static int Hex(string s) => int.Parse(s, NumberStyles.HexNumber, Inv);

    public Assets(string zipPath)
    {
        _stream = File.OpenRead(zipPath);
        _zip = new ZipArchive(_stream, ZipArchiveMode.Read);
        if (_zip.GetEntry("data/instruments.txt") is null)
            throw new InvalidDataException("sound.zip must contain data/instruments.txt at its root, not an extra sound/ folder.");
        ParseInstruments(ReadText("data/instruments.txt"));
        foreach (var entry in _zip.Entries)
        {
            var hit = Regex.Match(entry.FullName, @"sample_([0-9a-fA-F]+)\.wav$", RegexOptions.IgnoreCase);
            if (hit.Success) Samples.Add(Hex(hit.Groups[1].Value), entry.FullName);
            if (entry.FullName.StartsWith("data/music/", StringComparison.Ordinal) && entry.FullName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(entry.FullName).StartsWith("stop_", StringComparison.Ordinal)) MusicFiles.Add(entry.FullName);
        }
        MusicFiles.Sort(StringComparer.Ordinal);
        if (Programs.Count != 37 || Templates.Count != 13 || Kits.Count != 3 || Samples.Count != 175 || MusicFiles.Count != 14)
            throw new InvalidDataException($"Unexpected SegaSonic asset counts: programs={Programs.Count}, templates={Templates.Count}, kits={Kits.Count}, samples={Samples.Count}, songs={MusicFiles.Count}. Check the ROM/extractor revision.");
    }

    public byte[] Read(string name)
    {
        var entry = _zip.GetEntry(name) ?? throw new FileNotFoundException("Missing sound asset", name);
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
    public string ReadText(string name) => Encoding.UTF8.GetString(Read(name));

    private void ParseInstruments(string text)
    {
        string section = "";
        int template = -1, program = -1, patch = -1, kit = -1;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r', '\n');
            if (line.StartsWith("======== ", StringComparison.Ordinal))
            {
                section = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                template = program = patch = kit = -1;
                continue;
            }
            Match m;
            switch (section)
            {
                case "part_templates":
                    m = Regex.Match(line, @"^template\s+(\d+)\s+0x");
                    if (m.Success) { template = Dec(m.Groups[1].Value); Templates[template] = new(); break; }
                    if (template < 0) break;
                    m = Regex.Match(line, @"midi_channel\s+([0-9A-F])\s+volume\s+(\d+)\s+(program|drums)\s+(\d+)\s+transpose\s+(-?\d+)\s+fm_detune\s+(-?\d+)");
                    if (m.Success) Templates[template][Hex(m.Groups[1].Value)] = new Part(m.Groups[3].Value, Dec(m.Groups[4].Value), Dec(m.Groups[5].Value), Dec(m.Groups[2].Value), Dec(m.Groups[6].Value));
                    break;
                case "instruments":
                    m = Regex.Match(line, @"^program\s+(\d+)\s+0x[0-9a-fA-F]+$");
                    if (m.Success) { program = Dec(m.Groups[1].Value); break; }
                    if (program < 0) break;
                    m = Regex.Match(line.Trim(), @"^(fm patch|sample)\s+([0-9a-fA-F]+)\s+spare\s+[0-9a-fA-F]+\s+pan L(\d+) R(\d+)\s+detune\s+([+-]?\d+)");
                    if (m.Success) Programs[program] = new ProgramDef(m.Groups[1].Value == "fm patch" ? "fm" : "pcm", Hex(m.Groups[2].Value), Dec(m.Groups[3].Value), Dec(m.Groups[4].Value), Dec(m.Groups[5].Value));
                    break;
                case "drum_kits":
                    m = Regex.Match(line, @"^drums\s+(\d+)\s+0x");
                    if (m.Success) { kit = Dec(m.Groups[1].Value); Kits[kit] = new(); break; }
                    if (kit < 0) break;
                    m = Regex.Match(line, @"^note\s+(\d+)\s+[^:]+:\s+step 0x([0-9a-fA-F]+)\s+pan L(\d+) R(\d+)\s+sample ([0-9a-fA-F]+)");
                    if (m.Success) Kits[kit][Dec(m.Groups[1].Value)] = new DrumNote(Hex(m.Groups[2].Value), Dec(m.Groups[3].Value), Dec(m.Groups[4].Value), Hex(m.Groups[5].Value));
                    break;
                case "fm_patches":
                    m = Regex.Match(line, @"^patch\s+([0-9a-fA-F]+)\s+0x[0-9a-fA-F]+$");
                    if (m.Success) { patch = Hex(m.Groups[1].Value); Patches[patch] = new(); break; }
                    if (patch < 0) break;
                    m = Regex.Match(line, @"^\s+algorithm\s+(\d+)\s+feedback\s+(\d+)");
                    if (m.Success) { Patches[patch].Algorithm = Dec(m.Groups[1].Value); Patches[patch].Feedback = Dec(m.Groups[2].Value); }
                    m = Regex.Match(line, @"^\s+op(\d)\s+DT\s+(\d+)\s+MUL\s+(\d+)\s+TL\s+(\d+)\s+(scaled|fixed)\s+KS\s+(\d+)\s+AR\s+(\d+)\s+AM\s+(\d+)\s+D1R\s+(\d+)\s+D2R\s+(\d+)\s+SL\s+(\d+)\s+RR\s+(\d+)");
                    if (m.Success) Patches[patch].Operators[Dec(m.Groups[1].Value)] = new FmOperator(Dec(m.Groups[2].Value), Dec(m.Groups[3].Value), Dec(m.Groups[4].Value), m.Groups[5].Value, Dec(m.Groups[6].Value), Dec(m.Groups[7].Value), Dec(m.Groups[8].Value), Dec(m.Groups[9].Value), Dec(m.Groups[10].Value), Dec(m.Groups[11].Value), Dec(m.Groups[12].Value));
                    break;
            }
        }
    }

    public (short[] Pcm, int Rate) LoadWave(string name)
    {
        byte[] bytes = Read(name);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Invalid WAV");
        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Invalid WAV");
        ushort codec = 0, channels = 0, width = 0;
        int rate = 0;
        byte[]? sampleBytes = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string tag = new string(reader.ReadChars(4));
            uint size = reader.ReadUInt32();
            long next = stream.Position + size + (size & 1);
            if (tag == "fmt ")
            {
                codec = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadUInt16(); width = reader.ReadUInt16();
            }
            else if (tag == "data") sampleBytes = reader.ReadBytes(checked((int)size));
            stream.Position = next;
        }
        if (codec != 1 || channels != 1 || (width != 8 && width != 16) || sampleBytes is null)
            throw new InvalidDataException($"Unsupported WAV format: {name}");
        short[] pcm = new short[sampleBytes.Length / (width / 8)];
        if (width == 8) for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)((sampleBytes[i] - 128) * 256);
        else Buffer.BlockCopy(sampleBytes, 0, pcm, 0, sampleBytes.Length);
        return (pcm, rate);
    }
    public void Dispose() { _zip.Dispose(); _stream.Dispose(); }
}
