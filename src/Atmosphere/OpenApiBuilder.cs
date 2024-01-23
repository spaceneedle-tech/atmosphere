using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System;
using System.Text.Json;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace Atmosphere
{
    public class OpenApiBuilder
    {
        private readonly HttpClient httpClient;
        private readonly IProxyConfigProvider proxyConfigProvider;
        private readonly IConfiguration configuration;

        public OpenApiBuilder(
            IHttpClientFactory httpClientFactory,
            IProxyConfigProvider proxyConfigProvider,
            IConfiguration configuration)
        {
            this.httpClient = httpClientFactory.CreateClient();
            this.proxyConfigProvider = proxyConfigProvider;
            this.configuration = configuration;
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

            bool hasInsecureRoute = false;

            JObject templateRoute = null;

            foreach (var route in routes)
            {
                if (route.Transforms != null)
                {
                    var pathPattern = route.Transforms.SingleOrDefault(x => x.Keys.Contains("PathPattern"));
                    var match = route.Match;
                    var path = match.Path;
                    var methods = route.Match.Methods;
                    var metadata = route.Metadata;

                    string[] tags = new string[0];

                    if (metadata != null && metadata.TryGetValue("Section", out var tagsValue))
                    {
                        tags = new string[] { tagsValue };
                    }


                    if (pathPattern != null)
                    {
                        var routePattern = route.Match.Path;
                        var targetPattern = pathPattern["PathPattern"].Replace("{sub}", "{id}");
                        var openApiMatchingPath = openApiPaths.SingleOrDefault(x => x.Name == targetPattern);

                        if (openApiMatchingPath?.Value != null)
                        {
                            // if (templateRoute == null)
                            //{
                            // templateRoute = openApiMatchingPath.Value;
                            // }

                            try
                            {
                                foreach (JProperty operation in openApiMatchingPath.Value.Children<JProperty>())
                                {
                                    var references = new HashSet<string>();

                                    this.ExtractReferences(operation.Value, references);

                                    this.AddSchemas(unifiedSchemas, openApiSchemas, references);
                                }

                                JObject routeNode = JObject.Parse(openApiMatchingPath.Value.DeepClone().ToString());

                                RemoveNotPresentMethods(route.Match.Methods, routeNode);

                                if (route.AuthorizationPolicy != null && !string.IsNullOrEmpty(route.AuthorizationPolicy))
                                {
                                    if (methods == null || methods.Count == 0)
                                    {
                                        AddSecurityToMethod("get", routeNode);
                                        AddSecurityToMethod("post", routeNode);
                                        AddSecurityToMethod("put", routeNode);
                                        AddSecurityToMethod("delete", routeNode);
                                        AddSecurityToMethod("patch", routeNode);
                                        AddSecurityToMethod("options", routeNode);
                                        AddSecurityToMethod("head", routeNode);
                                        AddSecurityToMethod("connect", routeNode);
                                        AddSecurityToMethod("trace", routeNode);
                                    }
                                    else
                                    {
                                        foreach (var method in methods)
                                        {
                                            AddSecurityToMethod(method.ToLower(), routeNode);
                                        }
                                    }
                                }
                                else
                                {
                                    hasInsecureRoute = true;
                                }

                                if (tags.Length > 0)
                                {
                                    foreach (var item in routeNode)
                                    {
                                        item.Value["tags"] = new JArray(tags);
                                    }
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

                            templateRoute = JObject.Parse(File.ReadAllText("templateRoute.json"));

                            RemoveNotPresentMethods(route.Match.Methods, templateRoute);
                            /*
                              foreach (JProperty item in templateRoute)
                              {
                                  //foreach (var x in item)
                                  //{
                                  //    var a = x;

                                  //}
                                  // Navigate to the 'parameters' array and loop through each parameter
                                  JArray parameters = (JArray)item.Value["parameters"];

                                  if (parameters != null)
                                  {
                                      foreach (JObject parameter in parameters)
                                      {
                                          // Remove the 'schema' property from each parameter
                                          parameter.Property("schema")?.Remove();
                                      }
                                  }

                                  // Navigate to the 'responses' object and loop through each response
                                  JObject responses = (JObject)item.Value["responses"];
                                  foreach (var response in responses.Properties())
                                  {
                                      // Directly set the 'schema' property to a new generic object schema for each response

                                      if (((JObject)response.Value).ContainsKey("content"))
                                      {
                                          JObject content = (JObject)response.Value["content"]["application/json"];
                                          content["schema"] = new JObject(
                                              new JProperty("type", "object"),
                                              new JProperty("description", "An object with no defined schema")
                                          );
                                      }
                                  }

                              }*/

                            if (tags.Length > 0)
                            {
                                foreach (JProperty item in templateRoute as JToken)
                                {
                                    item.Value["tags"] = new JArray(tags);
                                }
                            }

                            // var x = templateRoute.ToString();

                            unifiedPaths[routePattern] = templateRoute;
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
                ["info"] = new JObject
                {
                    ["version"] = this.configuration["Info:Version"],
                    ["title"] = this.configuration["Info:Title"],
                    ["description"] = this.configuration["Info:Description"]
                },
                ["paths"] = unifiedPaths,
                ["components"] = unifiedComponents,

            };

            if (!hasInsecureRoute)
            {
                unifiedSpec["security"] = new JArray { new JObject { ["bearerAuth"] = new JArray() } };
            }

            return unifiedSpec;
        }

        private void RemoveNotPresentMethods(IReadOnlyList<string>? methods, JObject o)
        {
            if (methods == null || methods.Count == 0)
            {
                return;
            }

            var keysToRemove = new HashSet<string>();

            foreach (var key in o)
            {
                if (!methods.Contains(key.Key.ToUpper()))
                {
                    keysToRemove.Add(key.Key);
                }
            }

            foreach (var item in keysToRemove)
            {
                if (o.ContainsKey(item))
                {
                    o.Remove(item);
                }
            }
            //foreach (var method in methods)
            //{
            //    if (o.ContainsKey(method.ToLower()))
            //    {
            //        o.Remove(method);
            //    }
            //}
        }

        private void AddSecurityToMethod(string methodName, JObject o)
        {
            try
            {
                if (!o.ContainsKey(methodName))
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
                var openApiSchema = openApiSchemas?.FirstOrDefault(x => x.Name == referenceSchema);
                if (openApiSchema != null)
                {
                    var schema = openApiSchema.Value.DeepClone();
                    unifiedSchemas[referenceSchema] = schema;
                    ProcessSchema(schema.Value<JObject>(), unifiedSchemas, openApiSchemas);
                }
            }
        }

        private void ProcessSchema(JObject schema, JObject unifiedSchemas, IEnumerable<JProperty> openApiSchemas)
        {
            foreach (var property in schema.TryGetValue("properties", out var properties) ? properties.Value<JObject>().Properties() : Enumerable.Empty<JProperty>())
            {
                Console.WriteLine(property);
                if (property.Value?["$ref"] != null)
                {
                    CloneAndAddSchema(property.Value["$ref"]!.Value<string>(), unifiedSchemas, openApiSchemas);
                }
                var type = property.Value?["type"]?.Value<string>();
                if (type != "array") continue;
                var items = property.Value["items"]!.Value<JObject>();
                if (items.ContainsKey("$ref"))
                {
                    CloneAndAddSchema(items["$ref"]!.Value<string>(), unifiedSchemas, openApiSchemas);
                }

            }
        }

        private void CloneAndAddSchema(string refValue, JObject unifiedSchemas, IEnumerable<JProperty> openApiSchemas)
        {
            var refSchema = refValue.Replace("#/components/schemas/", "");
            if (unifiedSchemas.ContainsKey(refSchema)) return;
            var schemaProperty = openApiSchemas.FirstOrDefault(x => x.Name == refSchema);
            if (schemaProperty == null) return;
            var schema = schemaProperty.Value.DeepClone();
            unifiedSchemas[refSchema] = schema;
            ProcessSchema(schema.Value<JObject>(), unifiedSchemas, openApiSchemas); // Recursive call
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
