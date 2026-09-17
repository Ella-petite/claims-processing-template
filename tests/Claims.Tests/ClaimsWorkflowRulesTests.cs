using Claims.Contracts.Models;
using Xunit;

namespace Claims.Tests;

public class ClaimsWorkflowRulesTests
{
    [Fact]
    public void HighValueClaim_ShouldBeRepresentedAsManualReview()
    {
        var result = new RulesResult(true, true, "HighValueOrComplexClaim", "Demo rule triggered");
        Assert.True(result.Passed);
        Assert.True(result.RequiresManualReview);
    }

    [Fact]
    public void PaymentRequest_UsesStableIdempotencyKey()
    {
        var claimId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var request = new PaymentRequest(claimId, 1000, "ZAR", $"claim-{claimId:N}", "workflow-001");
        Assert.Equal("claim-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", request.IdempotencyKey);
        Assert.Equal("ZAR", request.Currency);
        Assert.Equal("workflow-001", request.WorkflowInstanceId);
    }
}


public class ClaimsStatusTests
{
    [Fact]
    public void ClaimStatuses_IncludeManualReviewAndPaymentStates()
    {
        Assert.Contains(ClaimStatus.UnderReview, Enum.GetValues<ClaimStatus>());
        Assert.Contains(ClaimStatus.PaymentPending, Enum.GetValues<ClaimStatus>());
        Assert.Contains(ClaimStatus.Paid, Enum.GetValues<ClaimStatus>());
    }

    [Fact]
    public void PaymentCompletionEvent_ContainsClaimAndProviderReferences()
    {
        var claimId = Guid.NewGuid();
        var evt = new PaymentCompletedEvent(claimId, "GW-001", "completed", DateTimeOffset.UtcNow, "PROV-001");
        Assert.Equal(claimId, evt.ClaimId);
        Assert.Equal("GW-001", evt.PaymentReference);
        Assert.Equal("PROV-001", evt.ProviderReference);
    }
}
