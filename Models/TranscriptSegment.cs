namespace TeamsScribe.Models;

// One transcribed utterance from a single speaker's track.
readonly record struct TranscriptSegment(TimeSpan Start, string Speaker, string Text);
