using Newtonsoft.Json.Linq;

namespace Atmosphere
{
    public class OpenApiSpec
    {
        public string Uri { get; set; }
        public JObject Spec { get; set; }
    }
}
