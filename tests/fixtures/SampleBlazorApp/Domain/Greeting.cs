namespace SampleBlazorApp.Domain;

// An interface and an implementation used by the find_implementations navigation tests.
public interface IGreeter
{
    string Greet();
}

public class EnglishGreeter : IGreeter
{
    public string Greet() => "Hello";
}
