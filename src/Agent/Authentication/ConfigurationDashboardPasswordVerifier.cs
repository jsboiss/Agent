using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Agent.Authentication;

public sealed class ConfigurationDashboardPasswordVerifier(
    IOptions<DashboardAuthOptions> options) : IDashboardPasswordVerifier
{
    private DashboardAuthOptions Options { get; } = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Options.Password);

    public bool Verify(string password)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(Options.Password));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
