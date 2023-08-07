using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
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

                foreach (var transform in this.transforms)
                {
                    if(transform.ContainsKey("PathPattern"))
                    {
                        this.PathPattern(context, claims, transform["PathPattern"]);
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

        private void PathPattern(RequestTransformContext context, Dictionary<string, string> claims, string pathPattern)
        {
            foreach (var claim in claims)
            {
                var key = "{" + claim.Key + "}";

                if (pathPattern.Contains(key))
                {
                    context.Path = pathPattern.Replace(key, claim.Value);
                }
            }
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
