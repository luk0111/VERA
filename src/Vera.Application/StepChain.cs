namespace Vera.Application;

/// <summary>The list of steps the tester is working on, and whether leaving it would lose something of theirs.</summary>
public sealed class StepChain
{
    private readonly IReadOnlyList<StepCommand> _proposal;
    private IReadOnlyList<StepCommand> _saved = [];

    public StepChain()
        : this([], [])
    {
    }

    private StepChain(IEnumerable<StepCommand> steps, IReadOnlyList<StepCommand> proposal)
    {
        Steps = [.. steps];
        _proposal = proposal;
    }

    public List<StepCommand> Steps { get; }

    /// <summary>The name they were saved or loaded as; null when they never were.</summary>
    public string? Name { get; private set; }

    /// <summary>The tester found, wrote, recorded, loaded or changed these steps. Steps VERA proposed are theirs once they change them.</summary>
    public bool Own => Steps.Count > 0 && !Steps.SequenceEqual(_proposal);

    /// <summary>The tester's own steps, different from what is saved.</summary>
    public bool Unsaved => Own && !Steps.SequenceEqual(_saved);

    /// <summary>Steps VERA proposed in the chat.</summary>
    public static StepChain Proposed(IReadOnlyList<StepCommand> steps) => new(steps, [.. steps]);

    /// <summary>Steps the tester wrote or recorded.</summary>
    public static StepChain Written(IEnumerable<StepCommand> steps) => new(steps, []);

    /// <summary>Steps from a file.</summary>
    public static StepChain Loaded(string name, IEnumerable<StepCommand> steps)
    {
        var chain = new StepChain(steps, []);
        chain.MarkSaved(name);
        return chain;
    }

    public void MarkSaved(string name)
    {
        Name = name;
        _saved = [.. Steps];
    }

    /// <summary>A copy to work on, so the changes can be thrown away.</summary>
    public StepChain Copy() => new(Steps, _proposal) { Name = Name, _saved = _saved };
}
