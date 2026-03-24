// SubmissionType — the kind of content a user submits as evidence.
// Covers all media types the mobile app can capture or the user can attach.
// Example: var type = SubmissionType.Image; // user snapped a photo

namespace TheWatch.Shared.Enums;

public enum SubmissionType
{
    Image = 0,
    Audio = 1,
    Video = 2,
    Text = 3,
    Document = 4,
    Survey = 5
}
