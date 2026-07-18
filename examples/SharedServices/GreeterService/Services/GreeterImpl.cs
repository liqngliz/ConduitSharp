using Grpc.Core;
using GreeterService.Protos;
using Serilog;

namespace GreeterService.Services;

public sealed class GreeterImpl : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        var protocol = context.GetHttpContext().Request.Protocol;
        Log.Information("SayHello({Name}) over {Protocol}", request.Name, protocol);

        return Task.FromResult(new HelloReply
        {
            Message  = $"Hello, {request.Name}!",
            Protocol = protocol,
        });
    }
}
