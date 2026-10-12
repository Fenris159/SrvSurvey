using System.Net;
using System.Net.Http.Headers;
using SrvSurvey.Core.Exploration;

namespace SrvSurvey.Core.Tests.Exploration;

public sealed class EdAstroTrafficBudgetTests
{
    /// <summary>Verifies catalogue and journal requests share one sliding window.</summary>
    [Fact]
    public void CatalogueAndJournalRequestsShareOneSlidingWindow()
    {
        var budget = new EdAstroTrafficBudget();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 100; i++)
        {
            Assert.True(budget.TryAcquire(now, out _));
        }
        Assert.False(budget.TryAcquire(now + TimeSpan.FromSeconds(899), out DateTimeOffset retry));
        Assert.Equal(now + TimeSpan.FromMinutes(15), retry);
        Assert.True(budget.TryAcquire(retry, out _));
    }

    /// <summary>Verifies provider cooldown is shared across clients.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProviderCooldownIsSharedAcrossClients(bool absolute)
    {
        var budget = new EdAstroTrafficBudget();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = absolute
            ? new RetryConditionHeaderValue(now + TimeSpan.FromMinutes(2))
            : new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
        response.Headers.Add("RateLimit-Remaining", "0");
        response.Headers.Add("RateLimit-Reset", "900");
        budget.Observe(response.Headers, now);
        Assert.False(budget.TryAcquire(now + TimeSpan.FromMinutes(14), out DateTimeOffset retry));
        Assert.Equal(now + TimeSpan.FromMinutes(15), retry);
        Assert.True(budget.TryAcquire(retry, out _));
    }

    /// <summary>Verifies non exhausted and malformed quota headers do not extend cooldown.</summary>
    [Fact]
    public void NonExhaustedAndMalformedQuotaHeadersDoNotExtendCooldown()
    {
        var budget = new EdAstroTrafficBudget();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("RateLimit-Remaining", "0");
        response.Headers.Add("RateLimit-Reset", "invalid");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        budget.Observe(response.Headers, now);
        Assert.True(budget.TryAcquire(now, out _));
        Assert.NotNull(EdAstroTrafficBudget.Shared);
    }
}
