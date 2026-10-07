namespace smart_locking_be.Application.DTOs.DeliveryRequests;

public sealed record SubmitDeliveryRequest(string ParcelImageUrl, string RecipientPhone);
