namespace SampleBlazorApp.Pages;

public partial class Calendar
{
    // Referenced only from Calendar.razor via @onclick (line 3), so its sole usage lives in
    // the generated BuildRenderTree. A generic LSP misses this; SharpSeek must find it and
    // map it back to the .razor line. This is the headline scenario, pinned as a fixture.
    private Task ShowPreviousYearAsync() => Task.CompletedTask;
}
