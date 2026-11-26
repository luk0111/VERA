namespace Vera.Application.Tests;

public sealed class StepFileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vera-step-files-tests", Guid.NewGuid().ToString("N"));
    private readonly StepFileStore _store;

    public StepFileStoreTests() => _store = new StepFileStore(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Saved_steps_load_back_the_same()
    {
        StepCommand[] steps =
        [
            new ClickStep("the search box"),
            new TypeStep("say \"hi\" \\ {{secret:pw}}", "the search box"),
            new PressStep("ctrl+s"),
            new ExpectShownStep("the results list"),
            new ExpectContainsStep("the search box", "cats"),
            new ExpectTitleStep("cats"),
        ];

        _store.Save("search cats", steps);

        _store.Load("search cats").ShouldBe(steps);
    }

    [Fact]
    public void The_file_has_one_step_per_line_as_written()
    {
        var path = _store.Save("login", [new ClickStep("the log in button"), new PressStep("enter")]);

        File.ReadAllLines(path).ShouldBe(["click the log in button", "press enter"]);
    }

    [Fact]
    public void Saving_again_replaces_the_file()
    {
        _store.Save("login", [new ClickStep("a")]);
        _store.Save("login", [new ClickStep("b")]);

        _store.Load("login").ShouldBe([new ClickStep("b")]);
    }

    [Fact]
    public void List_gives_the_names_sorted_and_is_empty_without_a_folder()
    {
        _store.List().ShouldBeEmpty();

        _store.Save("b", [new PressStep("enter")]);
        _store.Save("A", [new PressStep("enter")]);

        _store.List().ShouldBe(["A", "b"]);
        _store.Exists("b").ShouldBeTrue();
        _store.Exists("c").ShouldBeFalse();
    }

    [Fact]
    public void Deleting_removes_only_that_file()
    {
        _store.Save("a", [new PressStep("enter")]);
        _store.Save("b", [new PressStep("esc")]);

        _store.Delete("a");

        _store.List().ShouldBe(["b"]);
        Should.NotThrow(() => _store.Delete("a"));
    }

    [Fact]
    public void Blank_lines_and_comments_are_skipped()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "edited.txt"), ["# log in", "", "click the log in button", "  "]);

        _store.Load("edited").ShouldBe([new ClickStep("the log in button")]);
    }

    [Fact]
    public void A_wrong_line_names_its_line_number()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "edited.txt"), ["click the log in button", "jump"]);

        Should.Throw<FormatException>(() => _store.Load("edited")).Message.ShouldStartWith("Line 2 of edited.txt: Unknown command");
    }

    [Fact]
    public void A_file_without_steps_is_refused()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(Path.Combine(_folder, "empty.txt"), ["# nothing yet"]);

        Should.Throw<FormatException>(() => _store.Load("empty"));
    }

    [Theory]
    [InlineData("login", true)]
    [InlineData("search cats-2", true)]
    [InlineData("my_steps", true)]
    [InlineData("", false)]
    [InlineData(" login", false)]
    [InlineData("../login", false)]
    [InlineData("a.b", false)]
    [InlineData("a:b", false)]
    [InlineData("Autentificare-ț", true)]
    [InlineData("Căutare pisici", true)]
    [InlineData("CON", false)]
    [InlineData("com1", false)]
    [InlineData("console", true)]
    public void Names_are_plain_file_names(string name, bool valid) =>
        StepFileStore.IsValidName(name).ShouldBe(valid);

    [Fact]
    public void A_step_with_a_line_break_is_not_saved()
    {
        var e = Should.Throw<ArgumentException>(() => _store.Save("broken", [new ClickStep("the search button"), new ClickStep("button \"Save\nfile\"")]));

        e.Message.ShouldBe("Step 2 has a line break, so it can't be saved. Edit it into one line.");
        _store.Exists("broken").ShouldBeFalse();
    }
}
