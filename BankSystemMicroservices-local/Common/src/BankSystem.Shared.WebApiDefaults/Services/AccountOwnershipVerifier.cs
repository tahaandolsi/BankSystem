using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace BankSystem.Shared.WebApiDefaults.Services;

/// <summary>Confirms banking ownership with Account using the current caller's JWT.</summary>
public sealed class AccountOwnershipVerifier(IHttpClientFactory clients)
{
    public const string ClientName = "AccountOwnership";

    public async Task<AccountOwnershipResult> VerifyAsync(
        HttpContext context,
        Guid accountId,
        CancellationToken ct
    )
    {
        if (context.User.Identity?.IsAuthenticated != true
            || accountId == Guid.Empty
            || !Guid.TryParse(context.User.FindFirst("clientId")?.Value, out var customerId)
            || customerId == Guid.Empty
            || !AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization.ToString(), out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
            return AccountOwnershipResult.Unauthenticated;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/accounts/{accountId}");
            request.Headers.Authorization = authorization;
            using var response = await clients.CreateClient(ClientName).SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return AccountOwnershipResult.Unauthenticated;

            // Account intentionally hides missing and non-owned accounts as 404.
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
                return AccountOwnershipResult.Denied;

            // Unexpected client errors, redirects, throttling, and server errors indicate
            // that ownership could not be established. They must never permit the operation.
            if (!response.IsSuccessStatusCode)
                return AccountOwnershipResult.Unavailable;

            var account = await response.Content.ReadFromJsonAsync<AccountOwner>(cancellationToken: ct);
            return account is not null && account.Id == accountId && account.CustomerId == customerId
                ? AccountOwnershipResult.Owner
                : AccountOwnershipResult.Denied;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return AccountOwnershipResult.Unavailable;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout (as distinct from caller cancellation).
            return AccountOwnershipResult.Unavailable;
        }
        catch (JsonException)
        {
            return AccountOwnershipResult.Unavailable;
        }
        catch (NotSupportedException)
        {
            return AccountOwnershipResult.Unavailable;
        }
        catch (InvalidOperationException)
        {
            return AccountOwnershipResult.Unavailable;
        }
    }

    private sealed record AccountOwner(Guid Id, Guid CustomerId);
}

public enum AccountOwnershipResult
{
    Owner,
    Denied,
    Unauthenticated,
    Unavailable,
}
