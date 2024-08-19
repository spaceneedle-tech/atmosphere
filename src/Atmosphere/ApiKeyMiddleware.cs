namespace Atmosphere;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate next;
    private readonly IConfiguration configuration;

    private readonly string apiKey1;
    private readonly string apiKey2;
    private readonly string headerName;
    private readonly string parameterName;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        this.next = next;
        this.configuration = configuration;

        this.apiKey1 = this.configuration["ApiKeys:Key1"];
        this.apiKey2 = this.configuration["ApiKeys:Key2"];
        this.headerName = this.configuration["ApiKeys:HeaderName"];
        this.parameterName = this.configuration["ApiKeys:ParameterName"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string extractedApiKey = string.Empty;

        if (context.Request.Headers.TryGetValue(this.headerName, out var headerApiKey))
        {
            extractedApiKey = headerApiKey;
        }
        else if (context.Request.Query.TryGetValue(this.parameterName, out var queryApiKey))
        {
            extractedApiKey = queryApiKey;
        }

        if (string.IsNullOrEmpty(extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key was not provided.");
            return;
        }

        if (this.apiKey1 != extractedApiKey && this.apiKey2 != extractedApiKey)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized client.");
            return;
        }

        await this.next(context);
    }
}
