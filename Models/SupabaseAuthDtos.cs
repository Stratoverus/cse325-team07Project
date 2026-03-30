namespace TaskDone.Models;

using System.Text.Json.Serialization;

public sealed class SupabaseUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

public sealed class SupabaseSession
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public SupabaseUser? User { get; set; }
}

public sealed class SupabaseSignUpResponse
{
    [JsonPropertyName("user")]
    public SupabaseUser? User { get; set; }

    [JsonPropertyName("session")]
    public SupabaseSession? Session { get; set; }

    // Some Supabase responses return user fields at the top level.
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

public sealed class SupabaseAuthError
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

public sealed class AuthResult
{
    public bool Succeeded { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static AuthResult Success(string message = "") => new() { Succeeded = true, Message = message };
    public static AuthResult Failure(string message) => new() { Succeeded = false, ErrorMessage = message };
}
