using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var key = Encoding.ASCII.GetBytes(builder.Configuration.GetSection("Jwt:Key").Value);

//var key = Convert.FromBase64String(builder.Configuration.GetSection("Jwt:Key").Value);

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

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
       

        // Added to all routes.
        //builderContext.AddPathPrefix("/prefix");

        // Conditionally add a transform for routes that require auth.
        if (!string.IsNullOrEmpty(builderContext.Route.AuthorizationPolicy))
        {
            if (builderContext.Route.AuthorizationPolicy == "jwt")
            {
                builderContext.AddRequestTransform(async transformContext =>
                {
                    if (transformContext.HttpContext.User.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
                    {
                        var sub = identity.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

                        if (!string.IsNullOrEmpty(sub))
                        {
                            transformContext.ProxyRequest.Headers.Add("X-Sub", sub);
                        }
                    }

                    // transformContext.ProxyRequest.Headers.Add("CustomHeader", "CustomValue");
                });
            }
        }
    });



var app = builder.Build();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    //if (context.User.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
    //{
    //    var sub = identity.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
    //    if (!string.IsNullOrEmpty(sub))
    //    {
    //        context.Request.Headers.Add("X-Sub", sub);
    //    }
    //}

    await next();
});

app.UseEndpoints(endpoints =>
{
    //endpoints.MapControllers();
    endpoints.MapReverseProxy();
});

app.Run();