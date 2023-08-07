using Atmosphere;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var key = Encoding.ASCII.GetBytes(builder.Configuration.GetSection("Jwt:Key").Value);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidIssuer = builder.Configuration.GetSection("Jwt:Issuer").Value,
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("jwt", policy =>
        policy.RequireAuthenticatedUser());
});

var configuration = builder.Configuration.GetSection("ReverseProxy");

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(configuration)
    .AddTransforms(builderContext =>
    {
        if (!string.IsNullOrEmpty(builderContext.Route.AuthorizationPolicy))
        {
            if (builderContext.Route.AuthorizationPolicy == "jwt")
            {
                builderContext.RequestTransforms.Add(new JwtTransform(builderContext, builderContext.Route));
            }
        }
    });

var app = builder.Build();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.Run();