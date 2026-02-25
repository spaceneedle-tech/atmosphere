using Atmosphere;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<OpenApiBuilder>();

builder.Services.AddOutputCache();
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddApplicationInsightsTelemetry();

bool shouldAddJwtPolicy = false;

if (builder.Configuration.GetSection("Jwt").GetChildren().Count() > 0)
{
    shouldAddJwtPolicy = true;

    var publicKeyString = builder.Configuration.GetSection("Jwt:PublicKey").Value;
    var privateKeyString = builder.Configuration.GetSection("Jwt:PrivateKey").Value;

    SecurityKey key = null;

    if (!string.IsNullOrEmpty(privateKeyString))
    {
        key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(privateKeyString));
    }
    else
    {
        byte[] publicKeyBytes = Convert.FromBase64String(publicKeyString);

        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);

        key = new RsaSecurityKey(rsa);
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidIssuer = builder.Configuration.GetSection("Jwt:Issuer").Value,
        };
    });    
}

if (builder.Configuration.GetSection("Entra").GetChildren().Count() > 0)
{
    shouldAddJwtPolicy = true;

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration, "Entra");

    // CIAM usa endpoint v2.0 com issuer diferente do endpoint padrao.
    // O AddMicrosoftIdentityWebApi usa authority sem /v2.0, mas os tokens CIAM
    // sao emitidos com issuer que inclui /v2.0. Forcamos o MetadataAddress correto.
    //
    // Problema adicional: Microsoft.Identity.Web 1.x espera JwtSecurityToken (handler antigo),
    // mas .NET 8 usa JsonWebTokenHandler por padrao que produz JsonWebToken.
    // UseSecurityTokenValidators=true forca o JwtSecurityTokenHandler antigo.
    var entraConfig = builder.Configuration.GetSection("Entra");
    var ciamAuthority = $"{entraConfig["Instance"]}{entraConfig["TenantId"]}/v2.0";
    builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.MetadataAddress = $"{ciamAuthority}/.well-known/openid-configuration";
        options.Authority = ciamAuthority;
        options.UseSecurityTokenValidators = true;
        options.TokenValidationParameters.ValidIssuers = new[] { ciamAuthority };
        options.TokenValidationParameters.IssuerValidator = (issuer, token, parameters) =>
        {
            if (parameters.ValidIssuers != null && parameters.ValidIssuers.Contains(issuer))
                return issuer;
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException(
                $"Invalid Issuer '{issuer}'. Expected: {ciamAuthority}");
        };
    });
}

if (builder.Configuration.GetSection("Cognito").GetChildren().Count() > 0)
{
    shouldAddJwtPolicy = true;

    var region = builder.Configuration["Cognito:Region"];
    var userPoolId = builder.Configuration["Cognito:UserPoolId"];
    var clientId = builder.Configuration["Cognito:ClientId"];

    var authority = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer("Cognito", options =>
        {
            options.Authority = authority;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateLifetime = true,
            };
        });
}

if (shouldAddJwtPolicy)
{
    builder.Services.AddAuthorization(options =>
    {
        if (builder.Configuration.GetSection("Cognito").GetChildren().Count() > 0)
        {
            options.AddPolicy("jwt", policy =>
                policy.RequireAuthenticatedUser()
                      .AddAuthenticationSchemes("Cognito"));
        }
        else
        {
            options.AddPolicy("jwt", policy => policy.RequireAuthenticatedUser());
        }

    });
}

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

if (builder.Configuration.GetSection("ApiKeys").GetChildren().Count() > 0)
{
    app.UseMiddleware<ApiKeyMiddleware>();
}

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
    c.SwaggerEndpoint("/.metadata/open-api", $"{builder.Configuration["Info:Title"]} v{builder.Configuration["Info:Version"]}");
    c.RoutePrefix = ".metadata/swagger";
});

app.Run();
