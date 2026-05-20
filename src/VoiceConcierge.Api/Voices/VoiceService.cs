using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VoiceConcierge.Api.Data;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Voices;

public enum UpdateVoiceResult { Updated, NotFound, CannotDisableActive }

public sealed class VoiceService(AppDbContext db, ISpeechSynthesizer tts)
{
    private const string ActiveKey = "active_voice_id";
    private const int DefaultVoiceId = 1;

    public async Task<int> GetActiveVoiceIdAsync(CancellationToken ct = default)
    {
        var v = await db.AppConfigs.Where(c => c.Key == ActiveKey)
            .Select(c => c.Value).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id : DefaultVoiceId;
    }

    public async Task<IReadOnlyList<VoiceDto>> ListAsync(
        bool includeDisabled, CancellationToken ct = default)
    {
        var active = await GetActiveVoiceIdAsync(ct).ConfigureAwait(false);
        var q = db.Voices.OrderBy(v => v.Id).AsQueryable();
        if (!includeDisabled) q = q.Where(v => v.IsActive);
        return await q
            .Select(v => new VoiceDto(v.Id, v.Name, v.Description, v.ProviderVoiceId,
                v.Id == active, v.IsActive))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> SetActiveVoiceAsync(int voiceId, CancellationToken ct = default)
    {
        if (voiceId <= 0) throw new ArgumentOutOfRangeException(nameof(voiceId));
        var voice = await db.Voices.SingleOrDefaultAsync(v => v.Id == voiceId, ct)
            .ConfigureAwait(false);
        if (voice is null || !voice.IsActive) return false;

        var value = voiceId.ToString(CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO app_config (key, value)
               VALUES ({ActiveKey}, {value})
               ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value", ct).ConfigureAwait(false);
        return true;
    }

    public async Task<UpdateVoiceResult> UpdateAsync(int id, UpdateVoiceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var voice = await db.Voices.SingleOrDefaultAsync(v => v.Id == id, ct).ConfigureAwait(false);
        if (voice is null) return UpdateVoiceResult.NotFound;

        if (!req.IsActive && voice.IsActive)
        {
            var activeId = await GetActiveVoiceIdAsync(ct).ConfigureAwait(false);
            if (activeId == id) return UpdateVoiceResult.CannotDisableActive;
        }
        voice.Description = req.Description.Trim();
        if (req.SampleText is not null) voice.SampleText = req.SampleText.Trim();
        voice.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return UpdateVoiceResult.Updated;
    }

    public async Task<byte[]?> PreviewAsync(int voiceId, string? customText, CancellationToken ct = default)
    {
        if (voiceId <= 0) throw new ArgumentOutOfRangeException(nameof(voiceId));
        var v = await db.Voices.SingleOrDefaultAsync(x => x.Id == voiceId, ct).ConfigureAwait(false);
        if (v is null) return null;
        var text = string.IsNullOrWhiteSpace(customText) ? v.SampleText : customText.Trim();
        return await tts.SynthesizeAsync(text, v.ProviderVoiceId, ct).ConfigureAwait(false);
    }
}
