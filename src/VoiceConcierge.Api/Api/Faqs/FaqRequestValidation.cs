using VoiceConcierge.Api.Search;
using VoiceConcierge.Contracts;

namespace VoiceConcierge.Api.Api.Faqs;

internal static class FaqRequestValidation
{
    private const int MaxQuestionLength = 500;
    private const int MaxAnswerLength = 4000;

    public static UpsertFaqRequest Clean(UpsertFaqRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r with
        {
            Question = TextSanitization.Sanitize(r.Question),
            Answer = TextSanitization.Sanitize(r.Answer),
            Tags = r.Tags?.Select(TextSanitization.Sanitize).ToArray(),
        };
    }

    public static string? Validate(UpsertFaqRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return string.IsNullOrWhiteSpace(r.Question) || string.IsNullOrWhiteSpace(r.Answer)
            ? "question and answer are required"
            : r.Question.Length > MaxQuestionLength ? "question ≤500 chars"
            : r.Answer.Length > MaxAnswerLength ? "answer ≤4000 chars"
            : null;
    }
}
