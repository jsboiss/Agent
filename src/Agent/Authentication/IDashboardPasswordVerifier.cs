namespace Agent.Authentication;

public interface IDashboardPasswordVerifier
{
    bool IsConfigured { get; }

    bool Verify(string password);
}
