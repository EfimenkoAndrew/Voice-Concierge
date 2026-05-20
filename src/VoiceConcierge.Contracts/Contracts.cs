namespace VoiceConcierge.Contracts;

public sealed record FaqSearchRequest(string Query);

public sealed record FaqSearchResult(
    bool Match,
    string? Answer,
    double Score,
    Guid? FaqId,
    string Strategy);

public sealed record RecordUnansweredRequest(string Question);

public sealed record LiveKitTokenRequest(string Room, string Identity);
public sealed record LiveKitTokenResponse(string Token, string Url);
public sealed record LiveKitConfigDto(string Room);

public sealed record VoiceDto(int Id, string Name, string Description, string ProviderVoiceId, bool Active, bool IsActive);
public sealed record ActiveVoiceDto(int VoiceId, DateTimeOffset AppliedAt);
public sealed record SetActiveVoiceRequest(int VoiceId);
public sealed record UpdateVoiceRequest(string Description, string? SampleText, bool IsActive);
public sealed record PreviewWithRequest(string Text);

public sealed record FaqDto(Guid Id, string Question, string Answer, string[] Tags, DateTimeOffset UpdatedAt);
public sealed record UpsertFaqRequest(string Question, string Answer, string[]? Tags, bool UpdateTags = false);

public sealed record UnansweredDto(
    Guid Id, string Question, int Frequency,
    DateTimeOffset FirstAskedAt, DateTimeOffset LastAskedAt,
    string Status);
public sealed record ConvertUnansweredRequest(string Answer, string[]? Tags);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, Guid? NextCursor);
