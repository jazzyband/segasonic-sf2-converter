using System.Buffers.Binary;
using System.Text;

namespace SegaSonicConverter;

internal sealed record MidiEvent(long Tick, int Order, byte[] Data)
{
    public bool IsChannel => Data.Length > 0 && Data[0] >= 0x80 && Data[0] < 0xF0;
    public int Channel => IsChannel ? Data[0] & 0x0F : -1;
    public int Kind => Data.Length > 0 ? Data[0] & 0xF0 : -1;
    public bool IsTempo => Data.Length == 6 && Data[0] == 0xFF && Data[1] == 0x51 && Data[2] == 3;
    public bool IsNoteOn => IsChannel && Kind == 0x90 && Data[2] > 0;
}

internal sealed class MidiFileData
{
    public int TicksPerBeat { get; private init; }
    public List<List<MidiEvent>> Tracks { get; } = new();

    private static int Vlv(BinaryReader reader)
    {
        int n = 0;
        for (int i = 0; i < 4; i++)
        {
            byte b = reader.ReadByte();
            n = (n << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) return n;
        }
        throw new InvalidDataException("Invalid MIDI variable-length number");
    }
    private static void WriteVlv(BinaryWriter writer, long number)
    {
        if (number is < 0 or > 0x0FFFFFFF) throw new InvalidDataException("MIDI time exceeds supported limit");
        uint v = (uint)number;
        uint b = v & 0x7f;
        while ((v >>= 7) != 0) { b <<= 8; b |= (v & 0x7f) | 0x80u; }
        while (true) { writer.Write((byte)b); if ((b & 0x80) == 0) break; b >>= 8; }
    }

    public static MidiFileData Parse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        static int ReadBe32(BinaryReader r) => BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));
        static int ReadBe16(BinaryReader r) => BinaryPrimitives.ReadUInt16BigEndian(r.ReadBytes(2));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "MThd") throw new InvalidDataException("Not a MIDI file");
        int header = ReadBe32(reader);
        if (header < 6) throw new InvalidDataException("Invalid MIDI header");
        int format = ReadBe16(reader);
        int tracks = ReadBe16(reader);
        int division = ReadBe16(reader);
        if (format is < 0 or > 1 || (division & 0x8000) != 0) throw new InvalidDataException("Only standard ticks-per-beat MIDI files are supported");
        ms.Position = 8 + header;
        MidiFileData result = new() { TicksPerBeat = division };
        for (int trackId = 0; trackId < tracks; trackId++)
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "MTrk") throw new InvalidDataException("Invalid MIDI track");
            int size = ReadBe32(reader);
            long end = ms.Position + size;
            long tick = 0;
            int running = 0, order = 0;
            var events = new List<MidiEvent>();
            while (ms.Position < end)
            {
                tick += Vlv(reader);
                byte marker = reader.ReadByte();
                int status;
                int firstData = -1;
                if (marker < 0x80)
                {
                    if (running == 0) throw new InvalidDataException("MIDI running status without prior channel event");
                    status = running;
                    firstData = marker;
                }
                else status = marker;
                byte[] msg;
                if (status == 0xFF)
                {
                    byte type = reader.ReadByte();
                    int len = Vlv(reader);
                    byte[] payload = reader.ReadBytes(len);
                    using var sub = new MemoryStream();
                    using var bw = new BinaryWriter(sub);
                    bw.Write((byte)0xFF); bw.Write(type); WriteVlv(bw, len); bw.Write(payload);
                    msg = sub.ToArray();
                    // MIDI meta events do not cancel running status
                }
                else if (status is 0xF0 or 0xF7)
                {
                    int len = Vlv(reader);
                    using var sub = new MemoryStream(); using var bw = new BinaryWriter(sub);
                    bw.Write((byte)status); WriteVlv(bw, len); bw.Write(reader.ReadBytes(len));
                    msg = sub.ToArray();
                    running = 0;
                }
                else if (status is >= 0x80 and <= 0xEF)
                {
                    int count = ((status & 0xF0) is 0xC0 or 0xD0) ? 1 : 2;
                    msg = new byte[count + 1];
                    msg[0] = (byte)status;
                    if (firstData >= 0) { msg[1] = (byte)firstData; if (count == 2) msg[2] = reader.ReadByte(); }
                    else for (int i = 0; i < count; i++) msg[i + 1] = reader.ReadByte();
                    running = status;
                }
                else throw new InvalidDataException($"Unexpected MIDI status 0x{status:X2}");
                if (!(msg.Length >= 2 && msg[0] == 0xFF && msg[1] == 0x2F)) events.Add(new MidiEvent(tick, order++, msg));
            }
            ms.Position = end;
            result.Tracks.Add(events);
        }
        return result;
    }
    public static byte[] Meta(byte type, byte[] data)
    {
        using var ms = new MemoryStream(); using var writer = new BinaryWriter(ms);
        writer.Write((byte)0xFF); writer.Write(type); WriteVlv(writer, data.Length); writer.Write(data);
        return ms.ToArray();
    }
    public static byte[] Write(int ticksPerBeat, List<List<MidiEvent>> tracks)
    {
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        static void Be16(BinaryWriter w, int n) { w.Write((byte)(n >> 8)); w.Write((byte)n); }
        static void Be32(BinaryWriter w, int n) { w.Write((byte)(n >> 24)); w.Write((byte)(n >> 16)); w.Write((byte)(n >> 8)); w.Write((byte)n); }
        bw.Write(Encoding.ASCII.GetBytes("MThd")); Be32(bw, 6); Be16(bw, 1); Be16(bw, tracks.Count); Be16(bw, ticksPerBeat);
        foreach (var track in tracks)
        {
            using var data = new MemoryStream(); using var tw = new BinaryWriter(data);
            long prev = 0;
            foreach (var e in track.OrderBy(e => e.Tick).ThenBy(e => e.Order))
            {
                if (e.Data.Length >= 2 && e.Data[0] == 0xFF && e.Data[1] == 0x2F) continue;
                WriteVlv(tw, e.Tick - prev); tw.Write(e.Data); prev = e.Tick;
            }
            WriteVlv(tw, 0); tw.Write(new byte[] { 0xFF, 0x2F, 0x00 });
            bw.Write(Encoding.ASCII.GetBytes("MTrk")); Be32(bw, checked((int)data.Length)); bw.Write(data.ToArray());
        }
        return ms.ToArray();
    }
}

internal static class MidiConverter
{
    private static int Preset(Part p) => p.Kind == "drums" ? 120 + p.Program : p.Program + (p.Transpose switch { 0 => 0, -12 => 40, -24 => 80, _ => throw new InvalidDataException("Unrecognized transpose") });
    public static void Convert(Assets assets, string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "FL_Studio_MIDI"));
        var records = new List<string>();
        records.Add("song,source_channel,FL_track,kind,original_program_or_kit,source_transpose,sf2_preset,notes");
        foreach (string filename in assets.MusicFiles)
        {
            MidiFileData input = MidiFileData.Parse(assets.Read(filename));
            var events = new SortedDictionary<int, List<MidiEvent>>();
            List<MidiEvent> tempo = new();
            long runningTime = 0;
            int? template = null;
            // The original decompilation MIDI files are type 0, retain the original ordering
            foreach (var track in input.Tracks)
            {
                foreach (var msg in track)
                {
                    long t = msg.Tick + runningTime;
                    if (msg.IsChannel && msg.Channel == 14 && msg.Kind == 0xB0 && msg.Data[1] == 32)
                        template = msg.Data[2];
                    if (msg.IsTempo) tempo.Add(new MidiEvent(t, tempo.Count, msg.Data));
                    if (msg.IsChannel && msg.Channel != 14)
                    {
                        if (!events.TryGetValue(msg.Channel, out var list)) events[msg.Channel] = list = new();
                        list.Add(new MidiEvent(t, list.Count, msg.Data));
                    }
                }
                if (track.Count > 0) runningTime += track[^1].Tick;
            }
            if (template is null || !assets.Templates.TryGetValue(template.Value, out var partMap))
                throw new InvalidDataException($"Missing template for {filename}");
            string title = Path.GetFileNameWithoutExtension(filename);
            var output = new List<List<MidiEvent>>();
            List<MidiEvent> meta = new() { new MidiEvent(0, -1, MidiFileData.Meta(3, Encoding.UTF8.GetBytes(title))) };
            meta.AddRange(tempo);
            output.Add(meta);
            foreach (var (ch, messages) in events)
            {
                if (!partMap.TryGetValue(ch, out Part? part))
                {
                    Console.Error.WriteLine($"Warning: {title} channel {ch} not found in template");
                    continue;
                }
                int preset = Preset(part);
                bool drums = part.Kind == "drums";
                int dstCh = drums ? 9 : ch;
                string trackName = $"Ch {ch + 1:00} | {(drums ? "Drums" : "FM/PCM")} | SF2 preset {preset:000}";
                var result = new List<MidiEvent>
                {
                    new(0, -3, MidiFileData.Meta(3, Encoding.UTF8.GetBytes(trackName))),
                    new(0, -2, new byte[] {(byte)(0xC0 | dstCh), (byte)preset}),
                    new(0, -1, new byte[] {(byte)(0xB0 | dstCh), 7, (byte)Math.Clamp((int)Math.Round(part.Volume / 2.0, MidpointRounding.ToEven), 0, 127)})
                };
                int drumOffs = 0;
                foreach (var msg in messages)
                {
                    byte[] data = (byte[])msg.Data.Clone();
                    data[0] = (byte)((data[0] & 0xF0) | dstCh);
                    result.Add(new MidiEvent(msg.Tick, msg.Order * 2, data));
                    if (drums && msg.IsNoteOn)
                    {
                        // The arcade percussion triggers have no note-off. DAW/FL needs finite lengths
                        result.Add(new MidiEvent(msg.Tick + 48, msg.Order * 2 + 1,
                            new byte[] {(byte)(0x80 | dstCh), data[1], 0}));
                        drumOffs++;
                    }
                }
                output.Add(result);
                int notes = messages.Count(m => m.IsNoteOn);
                records.Add(string.Join(",", new[] { title, (ch + 1).ToString(), Csv(trackName), part.Kind, part.Program.ToString(), part.Transpose.ToString(), preset.ToString(), notes.ToString() }));
            }
            File.WriteAllBytes(Path.Combine(folder, "FL_Studio_MIDI", Path.GetFileName(filename)), MidiFileData.Write(input.TicksPerBeat, output));
            Console.WriteLine($"MIDI: {title}");
        }
        File.WriteAllLines(Path.Combine(folder, "instrument_mapping.csv"), records, new UTF8Encoding(false));
    }
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
}
