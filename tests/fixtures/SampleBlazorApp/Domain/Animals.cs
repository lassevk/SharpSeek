namespace SampleBlazorApp.Domain;

// A small class hierarchy used by the type_hierarchy and find_overrides navigation tests.
public abstract class Animal
{
    public abstract string Speak();
}

public class Dog : Animal
{
    public override string Speak() => "Woof";
}

public class Puppy : Dog
{
    public override string Speak() => "Yip";
}
