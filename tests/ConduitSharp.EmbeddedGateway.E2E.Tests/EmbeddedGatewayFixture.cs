using ConduitSharp.E2E.Shared;

namespace ConduitSharp.EmbeddedGateway.E2E.Tests;

[CollectionDefinition("EmbeddedGateway E2E", DisableParallelization = true)]
public sealed class EmbeddedGatewayCollection : ICollectionFixture<EmbeddedGatewayFixture>;

/// <summary>EmbeddedGateway example on the 6xxx port block, no path prefix.</summary>
public sealed class EmbeddedGatewayFixture : GatewayProcessFixture
{
    protected override string ExampleDirName => "EmbeddedGateway";
    protected override int GatewayPort => 6050;
    protected override int GrpcPort => 6060;
    public override string PathPrefix => "";
    public override (string A, string B) InventoryUpstreamPorts => ("6101", "6102");
}
