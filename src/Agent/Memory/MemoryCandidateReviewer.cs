namespace Agent.Memory;

public sealed class MemoryCandidateReviewer(IMemoryStore memoryStore) : IMemoryCandidateReviewer
{
    public async Task<MemoryCandidateReviewResult> Review(
        MemoryCandidateReviewRequest request,
        CancellationToken cancellationToken)
    {
        List<MemoryCandidateReview> reviews = [];
        HashSet<string> acceptedKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in request.Candidates)
        {
            var review = await ReviewCandidate(request.ConversationId, candidate, acceptedKeys, cancellationToken);
            reviews.Add(review);

            if (review.Accepted)
            {
                acceptedKeys.Add(GetMemoryKey(review.Candidate.Text, review.Candidate.Segment));
            }
        }

        return new MemoryCandidateReviewResult(reviews);
    }

    private async Task<MemoryCandidateReview> ReviewCandidate(
        string conversationId,
        ExtractedMemory candidate,
        IReadOnlySet<string> acceptedKeys,
        CancellationToken cancellationToken)
    {
        var score = GetScore(candidate);

        if (score < 0.55)
        {
            return new MemoryCandidateReview(
                candidate,
                false,
                "Candidate score is below the durable-memory threshold.",
                score,
                null,
                []);
        }

        var candidateKey = GetMemoryKey(candidate.Text, candidate.Segment);

        if (acceptedKeys.Contains(candidateKey))
        {
            return new MemoryCandidateReview(
                candidate,
                false,
                "A similar candidate was already accepted in this extraction batch.",
                score,
                null,
                []);
        }

        var existingMemories = await memoryStore.Search(
            new MemorySearchRequest(
                GetLookupQuery(candidate.Text),
                20,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string>
                {
                    ["conversationId"] = conversationId,
                    ["source"] = "memory-candidate-review"
                }),
            cancellationToken);
        var duplicate = existingMemories.FirstOrDefault(x => IsDuplicate(x.Text, candidate.Text, candidateKey, candidate.Segment));

        if (duplicate is not null)
        {
            return new MemoryCandidateReview(
                candidate,
                false,
                "A similar active memory already exists.",
                score,
                duplicate.Id,
                []);
        }

        var conflicts = existingMemories
            .Where(x => IsConflict(x, candidate))
            .Select(x => x.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MemoryCandidateReview(
            candidate,
            true,
            conflicts.Length == 0 ? "Candidate accepted." : "Candidate accepted and supersedes conflicting active memories.",
            score,
            null,
            conflicts);
    }

    private static double GetScore(ExtractedMemory candidate)
    {
        var segmentBoost = candidate.Segment switch
        {
            MemorySegment.Correction => 0.14,
            MemorySegment.Identity => 0.12,
            MemorySegment.Relationship => 0.12,
            MemorySegment.Preference => 0.12,
            MemorySegment.Project => 0.08,
            _ => 0
        };

        var tierBoost = candidate.Tier switch
        {
            MemoryTier.Permanent => 0.08,
            MemoryTier.Long => 0.04,
            _ => 0
        };

        var lengthPenalty = candidate.Text.Length < 8 ? 0.2 : 0;

        return Math.Clamp(
            (candidate.Importance * 0.42) + (candidate.Confidence * 0.42) + segmentBoost + tierBoost - lengthPenalty,
            0,
            1);
    }

    private static bool IsConflict(MemoryRecord existingMemory, ExtractedMemory candidate)
    {
        if (existingMemory.Segment != candidate.Segment)
        {
            return false;
        }

        var existingTerms = GetTerms(existingMemory.Text);
        var candidateTerms = GetTerms(candidate.Text);

        if (existingTerms.Intersect(candidateTerms, StringComparer.OrdinalIgnoreCase).Count() < 2)
        {
            return false;
        }

        return HasOpposingTerm(existingTerms, candidateTerms)
            || candidate.Segment is MemorySegment.Preference or MemorySegment.Identity or MemorySegment.Correction;
    }

    private static bool IsDuplicate(
        string existingText,
        string candidateText,
        string candidateKey,
        MemorySegment candidateSegment)
    {
        var existingKey = GetMemoryKey(existingText, candidateSegment);

        return string.Equals(existingText, candidateText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(existingKey, candidateKey, StringComparison.OrdinalIgnoreCase)
            || Normalize(existingText).Contains(Normalize(candidateText), StringComparison.OrdinalIgnoreCase)
            || Normalize(candidateText).Contains(Normalize(existingText), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var normalized = value
            .Replace("favourite", "favorite", StringComparison.OrdinalIgnoreCase)
            .Replace("colour", "color", StringComparison.OrdinalIgnoreCase)
            .Replace("actually", string.Empty, StringComparison.OrdinalIgnoreCase);

        return string.Join(
            " ",
            normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim('.', ',', ';', ':', '!', '?').ToLowerInvariant())
                .Where(x => !StopWords.Contains(x)));
    }

    private static string GetLookupQuery(string text)
    {
        var words = Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3)
            .ToArray();

        return words.Length == 0
            ? text
            : string.Join(' ', words);
    }

    private static ISet<string> GetTerms(string value)
    {
        return Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasOpposingTerm(ISet<string> a, ISet<string> b)
    {
        return OpposingTerms.Any(x =>
            (a.Contains(x.Key) && x.Value.Any(b.Contains))
            || (b.Contains(x.Key) && x.Value.Any(a.Contains)));
    }

    private static string GetMemoryKey(string text, MemorySegment segment)
    {
        var normalized = Normalize(text);

        if (segment == MemorySegment.Preference)
        {
            var favoriteIndex = normalized.IndexOf("favorite", StringComparison.OrdinalIgnoreCase);

            if (favoriteIndex >= 0)
            {
                return normalized[favoriteIndex..];
            }
        }

        return normalized;
    }

    private static HashSet<string> StopWords { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "is",
        "are",
        "be",
        "my",
        "the",
        "that",
        "to",
        "user",
        "prefers",
        "prefer",
        "preference"
    };

    private static Dictionary<string, string[]> OpposingTerms { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["verbose"] = ["concise", "short", "brief"],
        ["concise"] = ["verbose", "detailed", "long"],
        ["short"] = ["verbose", "detailed", "long"],
        ["brief"] = ["verbose", "detailed", "long"],
        ["like"] = ["dislike", "hate"],
        ["likes"] = ["dislikes", "hates"],
        ["prefer"] = ["avoid", "dislike"],
        ["prefers"] = ["avoids", "dislikes"],
        ["best"] = ["worst"]
    };
}
