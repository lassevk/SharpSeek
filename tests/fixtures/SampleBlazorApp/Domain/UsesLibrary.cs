using SampleLibrary;

namespace SampleBlazorApp.Domain;

// Actually uses a type from SampleLibrary, making it a real (usage-based) dependency. SampleUnused
// is referenced in the .csproj but deliberately not used here.
public static class UsesLibrary
{
    public static string Greeting() => LibraryGreeting.Text;
}
