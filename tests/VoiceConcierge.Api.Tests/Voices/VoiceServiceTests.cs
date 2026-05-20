using VoiceConcierge.Api.Voices;

namespace VoiceConcierge.Api.Tests;

public class VoiceServiceTests : PostgresTestBase
{
    [Fact]
    public async Task List_returns_seeded_voices_and_default_active_is_one()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var svc = new VoiceService(db, new RecordingSynth());

        var list = await svc.ListAsync(false, ct);

        Assert.Equal(4, list.Count);
        Assert.Single(list, v => v.Active);
        Assert.True(list.Single(v => v.Id == 1).Active);
    }

    [Fact]
    public async Task SetActiveVoice_updates_active_and_returns_false_for_unknown_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var svc = new VoiceService(db, new RecordingSynth());

        Assert.True(await svc.SetActiveVoiceAsync(3, ct));
        Assert.Equal(3, await svc.GetActiveVoiceIdAsync(ct));
        Assert.False(await svc.SetActiveVoiceAsync(99, ct));
    }

    [Fact]
    public async Task Preview_returns_audio_for_known_id_and_null_for_unknown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var synth = new RecordingSynth();
        var svc = new VoiceService(db, synth);

        var audio = await svc.PreviewAsync(2, null, ct);

        Assert.NotNull(audio);
        Assert.Equal("en-IE-EmilyNeural", synth.LastVoice);
        Assert.Null(await svc.PreviewAsync(99, null, ct));
    }
}
