using ConduitSharp.E2E.Shared;

namespace ConduitSharp.LegacyGateway.E2E.Tests;

[CollectionDefinition("LegacyGateway E2E", DisableParallelization = true)]
public sealed class LegacyGatewayCollection : ICollectionFixture<LegacyGatewayFixture>;

/// <summary>LegacyGateway example on the 5xxx port block, no path prefix.</summary>
public sealed class LegacyGatewayFixture : GatewayProcessFixture
{
    protected override string ExampleDirName => "LegacyGateway";
    protected override int GatewayPort => 5050;
    protected override int GrpcPort => 5060;
    public override string PathPrefix => "";
    public override (string A, string B) InventoryUpstreamPorts => ("5101", "5102");
}
