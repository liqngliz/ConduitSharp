using System.Text.Json.Serialization;

namespace ConduitSharp.Core.Routing;

/// <summary>
/// The plugin to activate on a route. JSON accepts kebab-case ("jwt-auth"),
/// deserialized strictly via <see cref="StrictEnumConverter{T}"/>.
///
/// This is the only routing type left in Core, and deliberately so: it is part of the plugin
/// contract. The routes.json schema itself lives in ConduitSharp.Gateway.AspNetCore, so a plugin
/// author does not inherit the gateway's config model — or YARP, which that model is built on.
/// </summary>
[JsonConverter(typeof(StrictEnumConverter<PluginName>))]
public enum PluginName
{
#pragma warning disable CS1591
    JwtAuth,
    JwksJwtAuth,
    ApiKeyAuth,
    ApiKeyAuthHashed,
    RateLimit,
    HeaderTransform,
    Cache,
    Custom,
    HttpProxy
#pragma warning restore CS1591
}
