// QuestionType — the input type for a survey question rendered on mobile.
// Mobile clients switch on this to render the appropriate UI control.
// Example: case QuestionType.PhotoCapture: ShowCameraButton(); break;

namespace TheWatch.Shared.Enums;

public enum QuestionType
{
    YesNo = 0,
    MultipleChoice = 1,
    FreeText = 2,
    Scale = 3,
    PhotoCapture = 4,
    AudioCapture = 5,
    LocationPin = 6
}
