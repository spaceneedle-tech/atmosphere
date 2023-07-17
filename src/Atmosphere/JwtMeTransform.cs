using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using Yarp.ReverseProxy.Transforms;

namespace Atmosphere
{
    public class JwtMePathTransform : RequestTransform
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, string>> transforms;

        private const string subClaimType = ClaimTypes.NameIdentifier;

        public JwtMePathTransform(IReadOnlyList<IReadOnlyDictionary<string, string>> transforms)
        {
            this.transforms = transforms;
        }

        public override async ValueTask ApplyAsync(RequestTransformContext context)
        {
            if (context.HttpContext.User.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
            {
                var claims = new Dictionary<string, string>()
                {
                    ["sub"] = this.GetSub(identity.Claims)
                };

                foreach (var transform in this.transforms)
                {
                    var item = transform.First();

                    switch (item.Key)
                    {
                        case "PathPattern":
                            this.PathPattern(context, claims, item.Value);
                            break;
                        case "QueryValueParameter":
                            this.QueryValueParameter(context, claims, transform.Last().Value);

                            break;
                        default:
                            break;
                    }
                }
            }
        }

        private void PathPattern(RequestTransformContext context, Dictionary<string, string> claims, string pathPattern)
        {
            foreach (var claim in claims)
            {
                context.Path = pathPattern.Replace("{" + claim.Key + "}", claim.Value);
            }
        }

        private void QueryValueParameter(RequestTransformContext context, Dictionary<string, string> claims, string queryPattern)
        {
            foreach (var claim in claims)
            {
                for (int i = 0; i < context.Query.Collection.Count; i++)
                {
                    var key = context.Query.Collection.ElementAt(0).Key;
                    var val = context.Query.Collection[key][0];
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
