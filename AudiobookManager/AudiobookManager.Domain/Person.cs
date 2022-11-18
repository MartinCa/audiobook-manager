namespace AudiobookManager.Domain;

public class Person
{
    public long? Id { get; set; }
    public string Name { get; set; }
    public string? Role { get; set; }

    public Person(string name)
    {
        Name = name;
    }
}
