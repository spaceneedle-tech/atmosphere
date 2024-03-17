using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Atmosphere
{
    public class JwtTransform : RequestTransform
    {
        private readonly TransformBuilderContext builderContext;
        private readonly RouteConfig route;
        private readonly IReadOnlyList<IReadOnlyDictionary<string, string>> transforms;

        private const string subClaimType = ClaimTypes.NameIdentifier;

        public JwtTransform(TransformBuilderContext builderContext, RouteConfig route)
        {
            this.builderContext = builderContext;
            this.route = route;
            this.transforms = route.Transforms;
        }

        public override async ValueTask ApplyAsync(RequestTransformContext context)
        {
            if (context.HttpContext.User.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
            {
                if(this.transforms == null)
                {
                    return;
                }

                var claims = new Dictionary<string, string>();

                foreach (var claim in identity.Claims)
                {
                    if(!claims.ContainsKey(claim.Type))
                    {
                        claims.Add(claim.Type, claim.Value);
                    }
                }

                if(!claims.ContainsKey("sub"))
                {
                    claims.Add("sub", this.GetSub(identity.Claims));
                }

                if(!claims.ContainsKey("Authorization") && 
                    context.HttpContext.Request.Headers.ContainsKey("Authorization"))
                {
                    claims.Add("Authorization", context.HttpContext.Request.Headers["Authorization"]);
                }

                foreach (var transform in this.transforms)
                {
                    if (transform.ContainsKey("PathPattern"))
                    {
                        var newPath = this.PathPattern(context, claims, transform["PathPattern"]);

                        var tranformParameters = ExtractParametersFromUrl(newPath);
                        var matchParameters = ExtractParametersFromUrl(this.route.Match.Path);

                        foreach (var param in tranformParameters)
                        {
                            var paramIndex = FindParameterIndexInPath(this.route.Match.Path, param);
                            var paramValue = GetPathSegmentAtIndex(context.HttpContext.Request.Path, paramIndex);

                            newPath = newPath.Replace("{" + param + "}", paramValue);
                        }

                        context.Path = newPath;

                    }
                    else if (transform.ContainsKey("QueryValueParameter"))
                    {
                        if(transform.ContainsKey("Set"))
                        {
                            this.QueryValueParameterSet(context, claims, transform["Set"]);
                        }                        
                    }
                    else if (transform.ContainsKey("RequestHeader"))
                    {
                        if (transform.ContainsKey("Set"))
                        {
                            this.RequestHeaderSet(context, claims, transform["RequestHeader"], transform["Set"]);
                        }
                    }
                }
            }
        }

        string GetPathSegmentAtIndex(string path, int index)
        {
            // Split the path into segments, removing any leading '/'
            string[] segments = path.TrimStart('/').Split('/');

            // Check if the index is within the range of the segments array
            if (index >= 0 && index < segments.Length)
            {
                return segments[index]; // Return the segment at the specified index
            }

            return "Index out of range"; // Return an error message if index is out of range
        }

        int FindParameterIndexInPath(string path, string parameterName)
        {
            // Split the path into segments
            string[] segments = path.TrimStart('/').Split('/');

            // Iterate over the segments to find the parameter
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Equals($"{{{parameterName}}}", StringComparison.OrdinalIgnoreCase))
                {
                    return i; // Return the index position of the parameter
                }
            }

            return -1; // Parameter not found, return -1
        }

        private HashSet<string> ExtractParametersFromUrl(string url)
        {
            Regex regex = new Regex(@"\{([^}]+)\}");
            MatchCollection matches = regex.Matches(url);

            HashSet<string> parameters = new();

            foreach (Match match in matches)
            {
                parameters.Add(match.Groups[1].Value);
            }

            return parameters;
        }

        private string PathPattern(RequestTransformContext context, Dictionary<string, string> claims, string pathPattern)
        {
           // var tempPathPattern = pathPattern.Replace("{", "{{{").Replace("}", "}}}");

            foreach (var claim in claims)
            {
                var key = "{" + claim.Key + "}";

                if (pathPattern.Contains(key))
                {
                    pathPattern = pathPattern.Replace(key, claim.Value);
                    //context.Path = pathPattern;
                    //context.Path = pathPattern.Replace(key, claim.Value);
                    //context.Path. = new PathString(pathPattern.Replace(key, claim.Value),);
                    //context.Path.
                }
            }

            return pathPattern;
        }

        private void RequestHeaderSet(RequestTransformContext context, Dictionary<string, string> claims, string headerName, string headerPatternValue)
        {
            string finalValue = headerPatternValue;

            foreach (var claim in claims)
            {
                var key = "{" + claim.Key + "}";

                if(finalValue.Contains(key))
                {
                    finalValue = finalValue.Replace(key, claim.Value);
                }
            }

            context.ProxyRequest.Headers.Remove(headerName);

            context.ProxyRequest.Headers.Add(headerName, finalValue);
        }

        private void QueryValueParameterSet(RequestTransformContext context, Dictionary<string, string> claims, string queryPattern)
        {
            var key = context.Query.Collection.ElementAt(0).Key;
            var val = context.Query.Collection[key][0];

            foreach (var claim in claims)
            {
                for (int i = 0; i < context.Query.Collection.Count; i++)
                {
                    context.Query.Collection[key] = val.Replace("{" + claim.Key + "}", claim.Value);
                }
            }
        }

        private string GetSub(IEnumerable<Claim> claims)
        {
            return claims.FirstOrDefault(c => c.Type == subClaimType)?.Value;
        }
    }
}
