using TaskDone.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TaskDone.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddOptions<SupabaseOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        configuration.GetSection(SupabaseOptions.SectionName).Bind(options);

        var envUrl =
            Environment.GetEnvironmentVariable("SUPABASE_URL") ??
            configuration["SUPABASE_URL"] ??
            configuration["Supabase:Url"] ??
            configuration["Supabase__Url"];

        var envKey =
            Environment.GetEnvironmentVariable("SUPABASE_KEY") ??
            Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ??
            configuration["SUPABASE_KEY"] ??
            configuration["SUPABASE_ANON_KEY"] ??
            configuration["Supabase:AnonKey"] ??
            configuration["Supabase__AnonKey"];

        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            options.Url = envUrl;
        }

        if (!string.IsNullOrWhiteSpace(envKey))
        {
            options.AnonKey = envKey;
        }
    });

builder.Services.AddScoped<HttpClient>();
builder.Services.AddScoped<SupabaseAuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, SupabaseAuthenticationStateProvider>();
builder.Services.AddSingleton<UserProfileService>();

builder.Services.AddSingleton<TaskDone.Services.TaskService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
//app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
