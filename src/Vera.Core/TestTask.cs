namespace Vera.Core;

/// <summary>One test, written by the tester in plain language.</summary>
/// <param name="Description">What to do, e.g. <c>Log in with the wrong password</c>.</param>
/// <param name="ExpectedResult">What must happen, e.g. <c>An error message is shown</c>.</param>
public sealed record TestTask(string Description, string ExpectedResult, AppTarget Target);