using Vera.Core;

namespace Vera.Application;

/// <summary>What VERA found when asked where something is in an app.</summary>
/// <param name="Element">The element the model chose. Null when it was not found.</param>
/// <param name="Reason">The model's one-sentence reason, or why VERA did not ask it.</param>
/// <param name="ImagePath">The saved screenshot: with a red box around the element when it was found, without when it was not.</param>
public sealed record FindResult(bool Found, UiElement? Element, string Reason, string ImagePath);
