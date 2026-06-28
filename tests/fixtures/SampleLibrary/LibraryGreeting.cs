namespace SampleLibrary;

// Used by SampleBlazorApp (see Domain/UsesLibrary.cs) so SampleLibrary is a *used* dependency.
public static class LibraryGreeting
{
    public static string Text => "From the library";
}
