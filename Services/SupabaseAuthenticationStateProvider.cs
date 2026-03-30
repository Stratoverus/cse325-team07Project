using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace TaskDone.Services;

public sealed class SupabaseAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly SupabaseAuthService _authService;
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public SupabaseAuthenticationStateProvider(SupabaseAuthService authService)
    {
        _authService = authService;
        _authService.AuthStateChanged += HandleAuthStateChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_authService.IsAuthenticated || _authService.CurrentSession?.User is null)
        {
            return Task.FromResult(new AuthenticationState(Anonymous));
        }

        var user = _authService.CurrentSession.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Email)
        };

        var identity = new ClaimsIdentity(claims, authenticationType: "Supabase");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private void HandleAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
