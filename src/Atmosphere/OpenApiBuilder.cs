using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.Text.Json;
using Yarp.ReverseProxy.Configuration;

namespace Atmosphere
{
    public class OpenApiBuilder
    {
        private readonly HttpClient httpClient;
        private readonly IProxyConfigProvider proxyConfigProvider;

        public OpenApiBuilder(
            IHttpClientFactory httpClientFactory,
            IProxyConfigProvider proxyConfigProvider)
        {
            this.httpClient = httpClientFactory.CreateClient();
            this.proxyConfigProvider = proxyConfigProvider;
        }

        internal async Task<JObject> BuildAsync()
        {
            var openApiSpecs = new List<JObject>();

            var uris = new List<string>();

            var proxyConfig = this.proxyConfigProvider.GetConfig();

            foreach (var cluster in proxyConfig.Clusters)
            {
                if (cluster.Metadata != null && cluster.Metadata.ContainsKey("OpenApiAddress"))
                {
                    uris.Add(cluster.Metadata["OpenApiAddress"]);
                }
            }

            foreach (var uri in uris)
            {
                var response = await httpClient.GetStringAsync(uri);
                openApiSpecs.Add(JObject.Parse(response));
            }

            var routes = proxyConfig.Routes;

            var openApiPaths = openApiSpecs.SelectMany(x => x["paths"].Children<JProperty>());
            var openApiComponents = openApiSpecs.SelectMany(x => x["components"].Children<JProperty>());
            var openApiSchemas = openApiSpecs.SelectMany(x => x["components"]["schemas"].Children<JProperty>());

            var unifiedPaths = new JObject();
            var unifiedComponents = new JObject();
            var unifiedSchemas = new JObject();

            var notFoundPaths = new List<IReadOnlyDictionary<string, string>>();

            foreach (var route in routes)
            {
                if (route.Transforms != null)
                {
                    var pathPattern = route.Transforms.SingleOrDefault(x => x.Keys.Contains("PathPattern"));

                    if (pathPattern != null)
                    {
                        var routePattern = route.Match.Path;

                        var openApiMatchingPath = openApiPaths.SingleOrDefault(x => x.Name == pathPattern["PathPattern"]);

                        if (openApiMatchingPath?.Value != null)
                        {
                            try
                            {
                                foreach (JProperty operation in openApiMatchingPath.Value.Children<JProperty>())
                                { 
                                    var references = new HashSet<string>();

                                    this.ExtractReferences(operation.Value, references);

                                    this.AddSchemas(unifiedSchemas, openApiSchemas, references);
                                }

                                JObject routeNode = JObject.Parse(openApiMatchingPath.Value.DeepClone().ToString());

                                if (route.AuthorizationPolicy != null && !string.IsNullOrEmpty(route.AuthorizationPolicy))
                                {
                                    AddSecurityToMethod("get", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("post", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("put", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("delete", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("patch", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("options", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("head", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("connect", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("trace", openApiMatchingPath, routeNode);
                                }

                                unifiedPaths[routePattern] = routeNode;
                            }
                            catch (Exception ex)
                            {
                                int a = 0;  // NOTE: Placeholder exception handling. You should replace this with actual logging or error handling.
                            }
                        }
                        else
                        {
                            notFoundPaths.Add(pathPattern);
                        }
                    }
                }
            }

            


            unifiedComponents["securitySchemes"] = new JObject
            {
                ["bearerAuth"] = new JObject
                {
                    ["type"] = "http",
                    ["scheme"] = "bearer",
                    ["bearerFormat"] = "JWT"
                }
            };

            unifiedComponents["schemas"] = unifiedSchemas;

            var unifiedSpec = new JObject
            {
                ["openapi"] = "3.0.0",
                ["info"] = new JObject { ["version"] = "1.0.0", ["title"] = "Unified API" },
                //["security"] = new JArray { new JObject { ["bearerAuth"] = new JArray() } },
                ["paths"] = unifiedPaths,
                ["components"] = unifiedComponents,
                
            };

            return unifiedSpec;
        }

        private void AddSecurityToMethod(string methodName, JProperty prop, JObject o)
        {
            try
            {
                if(!o.ContainsKey(methodName))
                {
                    return;
                }

                o[methodName]["security"] = new JArray
                {
                    new JObject { ["bearerAuth"] = new JArray() }
                };
                //var val = prop.Value[methodName];

                //if(val == null)
                //{
                //    return;
                //}

                //var methodElement = (JObject)val;

                //methodElement
            }
            catch (Exception ex)
            {
                // Handle the exception
            }
        }

   
        private void AddSchemas(JObject unifiedSchemas, IEnumerable<JProperty> openApiSchemas, HashSet<string> references)
        {
            foreach (var reference in references)
            {
                var referenceSchema = reference.Replace("#/components/schemas/", "");

                foreach (var openApiSchema in openApiSchemas)
                {
                    if (openApiSchema.Name == referenceSchema)
                    {
                        unifiedSchemas[referenceSchema] = openApiSchema.Value.DeepClone();
                    }
                }
            }
        }

        private void ExtractReferences(JToken token, HashSet<string> refValues)
        {
                        // Check if this token has a "$ref" property
            JToken refValue;

            if (token.Type == JTokenType.Object && ((JObject)token).TryGetValue("$ref", out refValue))
            {
                refValues.Add(refValue.ToString());
            }

            // Recursively check children
            if (token.HasValues)
            {
                foreach (var child in token.Children())
                {
                    ExtractReferences(child, refValues);
                }
            }
        }

        //private HashSet<string> ExtractReferences(JToken operation)
        //{
        //    var references = new HashSet<string>();

        //    if (operation["responses"] is JObject responses)
        //    {
        //        foreach (var response in responses.Properties())
        //        {
        //            if (response.Value["content"] is JObject content)
        //            {
        //                foreach (var mediaType in content.Properties())
        //                {
        //                    if (mediaType.Value["$ref"] is JToken refElement)
        //                    {
        //                        references.Add(refElement.ToString());
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    return references;
        //}


        //private void AddChildToJsonObject(JObject parentNode, string childName, JArray childValue)
        //{
        //    parentNode[childName] = childValue;
        //}

        //private JToken AddChildToJsonPropertyValue(JProperty parentProperty, string childName, JToken childValue)
        //{
        //    var modifiedObjectValue = (JObject)parentProperty.Value;
        //    modifiedObjectValue[childName] = childValue;

        //    return modifiedObjectValue;
        //}


        /*
        internal async Task<JsonObject> BuildAsync()
        {
            var openApiSpecs = new List<JsonDocument>();

            var uris = new List<string>();

            var proxyConfig = this.proxyConfigProvider.GetConfig();

            foreach(var cluster in proxyConfig.Clusters)
            {
                if(cluster.Metadata != null && cluster.Metadata.ContainsKey("OpenApiAddress"))
                {
                    uris.Add(cluster.Metadata["OpenApiAddress"]);
                }
            }

            foreach (var uri in uris)
            {
                var response = await httpClient.GetStringAsync(uri);
                openApiSpecs.Add(JsonDocument.Parse(response));
            }

            var routes = proxyConfig.Routes;

            var openApiPaths = openApiSpecs.SelectMany(x=>x.RootElement.GetProperty("paths").EnumerateObject());
            var openApiComponents = openApiSpecs.SelectMany(x=>x.RootElement.GetProperty("components").EnumerateObject());
            var openApiSchemas = openApiSpecs.SelectMany(x => x.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject());

            var unifiedPaths = new JsonObject();
            var unifiedComponents = new JsonObject();
            var unifiedSchemas = new JsonObject();

            var notFoundPaths = new List<IReadOnlyDictionary<string, string>>();    

            foreach (var route in routes)
            {
                if(route.Transforms != null)
                {
                    var pathPattern = route.Transforms.SingleOrDefault(x=> x.Keys.Contains("PathPattern"));

                    if(pathPattern != null)
                    {
                        var routePattern = route.Match.Path;

                        var openApiMatchingPath = openApiPaths.Where(x => x.Name == pathPattern["PathPattern"]).SingleOrDefault();

                        if(openApiMatchingPath.Value.ValueKind != JsonValueKind.Undefined)
                        {
                            try
                            {
                                foreach (JsonProperty operation in openApiMatchingPath.Value.EnumerateObject())
                                {
                                    var references = this.ExtractReferences(operation.Value);

                                    this.AddSchemas(unifiedSchemas, openApiSchemas, references);
                                }

                                JsonObject routeNode = JsonObject.Parse(openApiMatchingPath.Value.Clone().ToString()).AsObject();

                                if (route.AuthorizationPolicy != null && !string.IsNullOrEmpty(route.AuthorizationPolicy))
                                {
                                    AddSecurityToMethod("get", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("post", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("put", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("delete", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("patch", openApiMatchingPath, routeNode);
                                    AddSecurityToMethod("options", openApiMatchingPath, routeNode);
                                }

                                unifiedPaths.TryAdd(routePattern, routeNode);
                            }
                            catch(Exception ex)
                            {
                                int a = 0;
                            }
                        }
                        else
                        {
                            notFoundPaths.Add(pathPattern);
                        }
                        
                    }
                }
            }

            unifiedComponents.Add("schemas", unifiedSchemas);

            var unifiedSpec = new JsonObject
            {
                { "openapi", "3.0.0" },
                { "info", new JsonObject { { "version", "1.0.0" }, { "title", "Unified API" } } },
                { "paths", unifiedPaths },
                { "components", unifiedComponents },
                { "security", new JsonArray { new JsonObject { { "bearerAuth", new JsonArray() } } } }
            };

            return unifiedSpec;
        }

        private void AddSecurityToMethod(string methodName, JsonProperty prop, JsonObject o)
        {
            try
            {
                var methodElement = JsonObject.Parse(prop.Value.GetProperty(methodName).ToString()).AsObject();
                
                    AddChildToJsonObject(methodElement, "security",
                        new JsonArray
                        {
                                                        new JsonObject {
                                                            { "bearerAuth", new JsonArray()
                                                            }
                                                        }
                        });
            }
            catch(Exception ex)
            {
                int a = 0;
            }
        }

        private void AddChildToJsonObject(JsonObject parentNode, string childName, JsonArray childValue)
        {
            parentNode.TryAdd(childName, childValue);
        }

        private JsonNode AddChildToJsonPropertyValue(JsonProperty parentProperty, string childName, JsonElement childValue)
        {
            JsonObject modifiedObjectValue;

            if (parentProperty.Value.ValueKind == JsonValueKind.Object)
            {
                modifiedObjectValue = new JsonObject();

                foreach (var property in parentProperty.Value.EnumerateObject())
                {
                    modifiedObjectValue.TryAdd(property.Name, JsonNode.Parse(property.ToString()));
                }
            }
            else
            {
                modifiedObjectValue = new JsonObject();
            }

            modifiedObjectValue.Add(childName, childValue.GetRawText());

            return JsonNode.Parse(modifiedObjectValue.ToString());
        }

        private void AddSchemas(JsonObject unifiedSchemas, IEnumerable<JsonProperty> openApiSchemas, HashSet<string> references)
        {
            foreach (var reference in references)
            {
                var referenceSchema = reference.Replace("#/components/schemas/", "");

                foreach (var openApiSchema in openApiSchemas)
                {
                    if(openApiSchema.Name == referenceSchema)
                    {
                        unifiedSchemas.TryAdd(referenceSchema, JsonNode.Parse(openApiSchema.Value.Clone().ToString()));
                    }
                }
            }
        }

        private HashSet<string> ExtractReferences(JsonElement operation)
        {
            var references = new HashSet<string>();

            if (operation.TryGetProperty("responses", out JsonElement responses))
            {
                foreach (JsonProperty response in responses.EnumerateObject())
                {
                    if (response.Value.TryGetProperty("content", out JsonElement content))
                    {
                        foreach (JsonProperty mediaType in content.EnumerateObject())
                        {
                            if (mediaType.Value.TryGetProperty("schema", out JsonElement schema))
                            {
                                if (schema.TryGetProperty("$ref", out JsonElement refElement))
                                {
                                    references.Add(refElement.GetString());
                                }
                            }
                        }
                    }
                }
            }

            return references;
        }*/
    }
}
