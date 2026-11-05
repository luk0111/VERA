namespace Vera.Llm;

/// <summary>The model answered twice, and neither answer was one valid action.</summary>
public sealed class UnreadableAnswerException : Exception
{
    public UnreadableAnswerException()
        : base("The model's answer could not be read as an action. Try again.")
    {
    }

    public UnreadableAnswerException(string message)
        : base(message)
    {
    }

    public UnreadableAnswerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
