using DriverService.Models;
using DriverService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using RideHailing.Grpc.Drivers;
using RideHailing.Shared.Hosting;

namespace DriverService.Grpc;

/// <summary>
/// gRPC entry point for MatchingService (port 5012). "Drivers.DriversBase" is GENERATED from Protos/drivers.proto.
/// Same role as a controller: translate the request, call the service, translate the answer.
/// </summary>
public class DriversGrpcService(DriverAvailabilityQuery availability, OfferService offers) : Drivers.DriversBase
{
    public override async Task<GetAvailableDriversReply> GetAvailableDrivers(GetAvailableDriversRequest request, ServerCallContext context)
    {
        var exclude = request.ExcludeDriverIds.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).ToList();
        var drivers = await availability.GetAvailableAsync(exclude, request.MaxResults > 0 ? request.MaxResults : 5, context.CancellationToken);

        var reply = new GetAvailableDriversReply();
        reply.Drivers.AddRange(drivers.Select(d => new AvailableDriver
        {
            Id = d.Id.ToString(),
            Name = d.Name,
            Vehicle = d.Vehicle,
            LicensePlate = d.LicensePlate,
            Rating = d.Rating,
        }));
        return reply;
    }

    public override async Task<CreateOfferReply> CreateOffer(CreateOfferRequest request, ServerCallContext context)
    {
        try
        {
            var offer = await offers.CreateOfferAsync(new CreateOfferCommand(
                request.RequestKey, ParseGuid(request.RideId), ParseGuid(request.CorrelationId), ParseGuid(request.DriverId),
                ParseGuid(request.PassengerId), request.PassengerName, request.Pickup, request.Destination, request.Attempt,
                // proto3 "optional": Has… tells whether the field was sent (0.0 is a valid coordinate).
                request.HasPickupLat ? request.PickupLat : null, request.HasPickupLng ? request.PickupLng : null,
                request.HasDestinationLat ? request.DestinationLat : null, request.HasDestinationLng ? request.DestinationLng : null),
                context.CancellationToken);
            return new CreateOfferReply { OfferId = offer.Id.ToString(), ExpiresAtUnixMs = offer.ExpiresAt.ToUnixTimeMilliseconds() };
        }
        // Domain exceptions → gRPC status codes (gRPC does not use HTTP 404/409).
        catch (NotFoundException ex) { throw new RpcException(new Status(StatusCode.NotFound, ex.Message)); }
        catch (ConflictException ex) { throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message)); }
        catch (DbUpdateException) { throw new RpcException(new Status(StatusCode.FailedPrecondition, "Driver was reserved concurrently.")); }
    }

    private static Guid ParseGuid(string value) =>
        Guid.TryParse(value, out var id) ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid id '{value}'."));
}
