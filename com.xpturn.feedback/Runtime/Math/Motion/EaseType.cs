namespace xpTURN.Feedback
{
    // saved in Ease — the values are the saved format, add members at the end only.
    // Easing derives the family from the value, so In · Out · InOut stay three apart in family order
    public enum EaseType
    {
        Linear = 0,
        SineIn = 1,
        SineOut = 2,
        SineInOut = 3,
        QuadIn = 4,
        QuadOut = 5,
        QuadInOut = 6,
        CubicIn = 7,
        CubicOut = 8,
        CubicInOut = 9,
        QuartIn = 10,
        QuartOut = 11,
        QuartInOut = 12,
        QuintIn = 13,
        QuintOut = 14,
        QuintInOut = 15,
        ExpoIn = 16,
        ExpoOut = 17,
        ExpoInOut = 18,
        CircIn = 19,
        CircOut = 20,
        CircInOut = 21,
        BackIn = 22,
        BackOut = 23,
        BackInOut = 24,
        ElasticIn = 25,
        ElasticOut = 26,
        ElasticInOut = 27,
        BounceIn = 28,
        BounceOut = 29,
        BounceInOut = 30,
        Custom = 31,
    }
}
