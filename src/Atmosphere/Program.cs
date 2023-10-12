using Atmosphere;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<OpenApiBuilder>();

builder.Services.AddOutputCache();
builder.Services.AddHttpClient();
builder.Services.AddControllers();

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

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
});

var app = builder.Build();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseOutputCache();

app.UseEndpoints(endpoints =>
{
    //endpoints.Map("/test", async (context) =>
    //{


    //    await context.Response.WriteAsync("This is the custom logic endpoint.");

    //});
    endpoints.MapControllers();
    endpoints.MapReverseProxy();
});

//app.MapReverseProxy();

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/.metadata/open-api", "My API V1");  // Point to your own OpenAPI json file
    c.RoutePrefix = ".metadata/swagger";  // Set up the route to be '/swagger'
});



app.Run();