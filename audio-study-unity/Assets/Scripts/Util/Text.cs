public record LocalText(string English, string German)
{
    static bool IsEnglish => false;
    public static implicit operator string(LocalText text) => IsEnglish ? text.English : text.German;
}
