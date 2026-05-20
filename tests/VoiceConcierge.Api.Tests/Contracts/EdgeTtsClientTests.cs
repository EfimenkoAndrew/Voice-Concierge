using System.Text;
using System.Text.Json;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Tests;

/// <summary>
/// D4-01 / Q4-09: covers the two pure-logic parts of the shared Edge TTS
/// client that prior reviews left untested. The runtime WebSocket itself is
/// covered by the live-recipe in review-qa.md.
/// </summary>
public class EdgeTtsClientTests
{
    [Fact] // D4-01: the speech.config JSON must parse — the hand-rolled string
    // had unbalanced braces (5 `{` vs 4 `}`), so Edge TTS rejected every
    // request and returned 0 audio bytes. The new JsonSerializer-built body
    // guarantees this can never regress silently.
    public void BuildSpeechConfig_emits_parseable_json_with_expected_shape()
    {
        var json = EdgeTtsClient.BuildSpeechConfig(EdgeTtsClient.Mp3Format);
        using var doc = JsonDocument.Parse(json); // would throw if malformed
        var audio = doc.RootElement
            .GetProperty("context")
            .GetProperty("synthesis")
            .GetProperty("audio");
        Assert.Equal(EdgeTtsClient.Mp3Format, audio.GetProperty("outputFormat").GetString());
        var meta = audio.GetProperty("metadataoptions");
        Assert.False(meta.GetProperty("sentenceBoundaryEnabled").GetBoolean());
        Assert.False(meta.GetProperty("wordBoundaryEnabled").GetBoolean());
    }

    [Fact] // Q4-09a: typical Edge TTS RIFF — fmt chunk then data chunk → returns
    // the data-chunk payload only (16-bit mono 24kHz PCM).
    public void StripWavHeader_returns_payload_after_data_chunk()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6 };
        var wav = MakeRiff(("fmt ", new byte[16]), ("data", payload));
        Assert.Equal(payload, EdgeTtsClient.StripWavHeader(wav));
    }

    [Fact] // Q4-09b: WAVs with extra `LIST` / `bext` chunks before `data`
    // (some encoders emit these) still locate the right payload.
    public void StripWavHeader_skips_unknown_chunks_before_data()
    {
        var payload = new byte[] { 9, 9, 9, 9 };
        var wav = MakeRiff(
            ("fmt ", new byte[16]),
            ("LIST", "INFOICMT\0..."u8.ToArray()),
            ("bext", new byte[8]),
            ("data", payload));
        Assert.Equal(payload, EdgeTtsClient.StripWavHeader(wav));
    }

    [Fact] // Q4-09c: non-RIFF input is returned unchanged (defensive fallback).
    public void StripWavHeader_passes_through_non_riff()
    {
        var input = new byte[] { 0xff, 0xfb, 0x10, 0x00 }; // MP3-ish header
        Assert.Equal(input, EdgeTtsClient.StripWavHeader(input));
    }

    [Fact] // Q4-09d: a malformed chunk header (negative size) doesn't run the
    // parser off the end of the buffer — returns input unchanged.
    public void StripWavHeader_bails_on_negative_chunk_size()
    {
        var wav = MakeRiff(("fmt ", new byte[8]));
        // Stomp the fmt size with a negative int.
        Buffer.BlockCopy(BitConverter.GetBytes(-7), 0, wav, 16, 4);
        Assert.Equal(wav, EdgeTtsClient.StripWavHeader(wav));
    }

    // Build a minimal RIFF/WAVE buffer with the named chunks (each as id+size+data,
    // word-aligned). Total file size in the RIFF header is rounded to int.
    private static byte[] MakeRiff(params (string Id, byte[] Data)[] chunks)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(0); // size placeholder
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        foreach (var (id, data) in chunks)
        {
            bw.Write(Encoding.ASCII.GetBytes(id));
            bw.Write(data.Length);
            bw.Write(data);
            if ((data.Length & 1) == 1) bw.Write((byte)0);
        }
        var bytes = ms.ToArray();
        Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length - 8), 0, bytes, 4, 4);
        return bytes;
    }
}
