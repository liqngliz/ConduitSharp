using ConduitSharp.E2E.Shared;

namespace ConduitSharp.EmbeddedGatewayPrefixed.E2E.Tests;

[CollectionDefinition("EmbeddedGatewayPrefixed E2E", DisableParallelization = true)]
public sealed class EmbeddedGatewayPrefixedCollection : ICollectionFixture<EmbeddedGatewayPrefixedFixture>;

/// <summary>EmbeddedGatewayPrefixed example on the 7xxx port block; gateway paths carry "/api".</summary>
public sealed class EmbeddedGatewayPrefixedFixture : GatewayProcessFixture
{
    protected override string ExampleDirName => "EmbeddedGatewayPrefixed";
    protected override int GatewayPort => 7050;
    protected override int GrpcPort => 7060;
    public override string PathPrefix => "/api";
    public override (string A, string B) InventoryUpstreamPorts => ("7101", "7102");
}
