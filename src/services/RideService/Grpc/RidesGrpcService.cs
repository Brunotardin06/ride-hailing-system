using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using RideHailing.Grpc.Rides;
using RideHailing.Shared.Hosting;
using RideService.Services;

namespace RideService.Grpc;

/// <summary>
/// gRPC entry point (like a controller, but for gRPC). The base class "Rides.RidesBase" is GENERATED from
/// Protos/rides.proto at build time; we only override the methods. Logic stays in RideWorkflow.
/// </summary>
public class RidesGrpcService(RideWorkflow rides) : Rides.RidesBase
{
    public override async Task<AssignDriverReply> AssignDriver(AssignDriverRequest request, ServerCallContext context)
    {
        try
        {
            var ride = await rides.AssignDriverAsync(
                ParseGuid(request.RideId, "ride_id"), ParseGuid(request.DriverId, "driver_id"),
                request.DriverName, request.Vehicle, request.LicensePlate, context.CancellationToken);
            return new AssignDriverReply { Status = ride.Status.ToString() };
        }
        // gRPC has its own status codes (not HTTP 404/409): translate our domain exceptions.
        catch (NotFoundException ex) { throw new RpcException(new Status(StatusCode.NotFound, ex.Message)); }
        catch (ConflictException ex) { throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message)); }
        catch (DbUpdateConcurrencyException) { throw new RpcException(new Status(StatusCode.Aborted, "Concurrent update, retry.")); }
    }

    private static Guid ParseGuid(string value, string field) =>
        Guid.TryParse(value, out var id) ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid {field}."));
}
