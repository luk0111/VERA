using Meziantou.Framework.Win32;

namespace Vera.Secrets.Tests;

public sealed class WindowsCredentialSecretStoreTests
{
    [Fact]
    public void The_target_name_has_the_VERA_prefix()
    {
        WindowsCredentialSecretStore.TargetName("gemini").ShouldBe("VERA/gemini");
    }

    [Fact]
    public void A_blank_name_is_rejected()
    {
        Should.Throw<ArgumentException>(() => WindowsCredentialSecretStore.TargetName(" "));
    }
}

/// <summary>Writes to the real Windows Credential Manager of the current user, so CI skips it. Cleans up after itself.</summary>
[Trait("Category", "Integration")]
public sealed class WindowsCredentialSecretStoreIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_saved_secret_can_be_read_back_and_replaced()
    {
        var store = new WindowsCredentialSecretStore();
        var name = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            (await store.GetAsync(name, Ct)).ShouldBeNull();

            await store.SetAsync(name, "first-value", Ct);
            (await store.GetAsync(name, Ct)).ShouldBe("first-value");

            await store.SetAsync(name, "second-value", Ct);
            (await store.GetAsync(name, Ct)).ShouldBe("second-value");
        }
        finally
        {
            CredentialManager.TryDeleteCredential(WindowsCredentialSecretStore.TargetName(name));
        }
    }
}
