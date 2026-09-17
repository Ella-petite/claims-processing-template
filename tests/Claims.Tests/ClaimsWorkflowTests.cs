using Claims.Contracts.Models;
using Xunit;

namespace Claims.Tests;

public class ClaimsWorkflowTests
{
    [Fact]
    public void NewClaimCanBeRepresentedAsSubmitted()
    {
        var claim = new ClaimDto(Guid.NewGuid(), "CLM-001", "CLIENT-001", "POL-001", "DEATH", 10000m, DateTime.UtcNow, "Example", ClaimStatus.Submitted, null, null, [], []);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
    }

    [Theory]
    [InlineData(10000, false)]
    [InlineData(25001, true)]
    public void HigherValueClaimsCanEnterManualReview(decimal amount, bool expected)
    {
        var requiresReview = amount > 25000m;
        Assert.Equal(expected, requiresReview);
    }
}
