public class GameWordService
{
    private readonly string[] _words = new[]
    {
        "APPLE", "BANANA", "CHERRY", "DATE", "FIG", "GRAPE",
        "KIWI", "LEMON", "MANGO"};

    public string[] GetWords()
    {
        // Optionally, you can shuffle or select a subset
        return _words;
    }

    public string GetWordsAsCsv()
    {
        return string.Join(',', _words);
    }
}
