using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Incidents;
using smart_locking_be.Application.DTOs.Notifications;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class IncidentService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider, ITokenHashService tokenHashService,
    IOperationsRealtimeNotifier? realtimeNotifier = null) : IIncidentService
{
    // Actions without a user (for example automatic SLA escalation) are shown as the system.
    private const string SystemActorName = "Hệ thống";

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Locker",
        "Compartment",
        "Parcel",
        "Retrieval",
        "Return",
        "Payment",
        "Other"
    };

    public async Task<IncidentDetailResponse> CreateResidentIncidentAsync(
        Guid residentUserId,
        CreateIncidentRequest request,
        CancellationToken cancellationToken = default)
    {
        string type = RequireValue(request.Type, nameof(request.Type), 100);
        if (!SupportedTypes.Contains(type))
        {
            throw BadRequestException.For("incidentType", "Incident type is invalid.");
        }
        int relatedRecordCount = new[]
        {
            request.ParcelId,
            request.ReturnRequestId,
            request.PaymentTransactionId
        }.Count(id => id.HasValue);
        if (relatedRecordCount > 1)
        {
            throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Only one parcel, return request, or payment may be linked.");
        }

        ResidentProfile resident = await dbContext.ResidentProfiles
            .Include(profile => profile.User)
            .SingleOrDefaultAsync(
                profile => profile.UserId == residentUserId &&
                           profile.User.Role == UserRole.Resident &&
                           profile.User.Status == UserStatus.Active,
                cancellationToken)
            ?? throw new NotFoundException(UserErrorCodes.NotFound, "Resident profile not found.");

        Guid? lockerId = request.LockerId;
        Guid? compartmentId = request.LockerCompartmentId;
        Guid? parcelId = request.ParcelId;

        void SetContext(Guid relatedLockerId, Guid? relatedCompartmentId, string relatedRecord)
        {
            if (lockerId.HasValue && lockerId != relatedLockerId)
            {
                throw new BadRequestException(IncidentErrorCodes.InvalidLink, $"{relatedRecord} does not belong to the selected locker.");
            }
            if (compartmentId.HasValue && relatedCompartmentId.HasValue && compartmentId != relatedCompartmentId)
            {
                throw new BadRequestException(IncidentErrorCodes.InvalidLink, $"{relatedRecord} does not belong to the selected compartment.");
            }

            lockerId = relatedLockerId;
            compartmentId ??= relatedCompartmentId;
        }

        if (request.ParcelId.HasValue)
        {
            Parcel parcel = await dbContext.Parcels
                .Include(item => item.DeliveryRequest)
                .SingleOrDefaultAsync(
                    item => item.Id == request.ParcelId &&
                            item.DeliveryRequest.ResidentProfileId == resident.Id,
                    cancellationToken)
                ?? throw new NotFoundException(ParcelErrorCodes.NotFound, "Parcel not found.");
            SetContext(parcel.DeliveryRequest.LockerId, parcel.DeliveryRequest.AllocatedCompartmentId, "Parcel");
        }

        if (request.ReturnRequestId.HasValue)
        {
            ReturnRequest returnRequest = await dbContext.ReturnRequests.SingleOrDefaultAsync(
                item => item.Id == request.ReturnRequestId && item.ResidentProfileId == resident.Id,
                cancellationToken) ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
            SetContext(returnRequest.LockerId, returnRequest.AllocatedCompartmentId, "Return request");
        }

        if (request.PaymentTransactionId.HasValue)
        {
            PaymentTransaction payment = await dbContext.PaymentTransactions
                .Include(item => item.OverdueCharge)
                    .ThenInclude(charge => charge.Parcel)
                        .ThenInclude(parcel => parcel.DeliveryRequest)
                .SingleOrDefaultAsync(
                    item => item.Id == request.PaymentTransactionId &&
                            item.OverdueCharge.Parcel.DeliveryRequest.ResidentProfileId == resident.Id,
                    cancellationToken)
                ?? throw new NotFoundException(PaymentErrorCodes.NotFound, "Payment transaction not found.");
            if (parcelId.HasValue && parcelId != payment.OverdueCharge.ParcelId)
            {
                throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Payment transaction does not belong to the selected parcel.");
            }
            parcelId = payment.OverdueCharge.ParcelId;
            SetContext(
                payment.OverdueCharge.Parcel.DeliveryRequest.LockerId,
                payment.OverdueCharge.Parcel.DeliveryRequest.AllocatedCompartmentId,
                "Payment transaction");
        }

        if (!lockerId.HasValue)
        {
            throw new BadRequestException(IncidentErrorCodes.InvalidLink, "A related locker, parcel, return request, or payment is required.");
        }
        if (resident.RegisteredLockerId != lockerId)
        {
            throw new ForbiddenException(IncidentErrorCodes.AccessDenied, "The incident locker is not registered to this resident.");
        }
        if (!await dbContext.Lockers.AnyAsync(locker => locker.Id == lockerId, cancellationToken))
        {
            throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        }
        if (compartmentId.HasValue && !await dbContext.LockerCompartments.AnyAsync(
            compartment => compartment.Id == compartmentId && compartment.LockerId == lockerId,
            cancellationToken))
        {
            throw new NotFoundException(LockerErrorCodes.InvalidCompartment, "Locker compartment not found.");
        }

        Guid? assignedOperatorUserId = await dbContext.OperatorAssignments
            .Where(assignment =>
                assignment.LockerId == lockerId &&
                assignment.RevokedAt == null &&
                assignment.OperatorUser.Status == UserStatus.Active)
            .Select(assignment => (Guid?)assignment.OperatorUserId)
            .SingleOrDefaultAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            ReporterUserId = residentUserId,
            ParcelId = parcelId,
            ReturnRequestId = request.ReturnRequestId,
            PaymentTransactionId = request.PaymentTransactionId,
            LockerId = lockerId,
            LockerCompartmentId = compartmentId,
            AssignedOperatorUserId = assignedOperatorUserId,
            Type = SupportedTypes.Single(candidate => candidate.Equals(type, StringComparison.OrdinalIgnoreCase)),
            Source = IncidentSource.Resident,
            Status = IncidentStatus.Open,
            Title = RequireValue(request.Title, nameof(request.Title), 200),
            Description = RequireValue(request.Description, nameof(request.Description), 4000),
            EvidenceUrl = ValidateOptionalUrl(request.EvidenceUrl),
            CreatedAt = now,
            UpdatedAt = now
        };
        incident.Actions.Add(new IncidentAction
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ActionByUserId = residentUserId,
            ActionType = "Created",
            ToStatus = IncidentStatus.Open,
            Notes = "Resident submitted the incident.",
            CreatedAt = now
        });
        dbContext.Incidents.Add(incident);

        IReadOnlyCollection<Guid> recipients = assignedOperatorUserId.HasValue
            ? [assignedOperatorUserId.Value]
            : await dbContext.Users
                .Where(user => user.Role == UserRole.Administrator && user.Status == UserStatus.Active)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
        foreach (Guid recipient in recipients)
        {
            dbContext.Notifications.Add(CreateNotification(
                recipient,
                incident,
                assignedOperatorUserId.HasValue ? "IncidentAssigned" : "IncidentUnassigned",
                "New incident needs attention",
                incident.Title,
                now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(incident, cancellationToken);
        return await LoadDetailAsync(incident.Id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<IncidentListItemResponse>> GetResidentIncidentsAsync(
        Guid residentUserId,
        CancellationToken cancellationToken = default) =>
        await MapList(dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.ReporterUserId == residentUserId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<IncidentListItemResponse>> GetOperationalIncidentsAsync(
        Guid userId,
        string role,
        IncidentStatus? status,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Incident> query = ScopeOperational(userId, role).AsNoTracking();
        if (status.HasValue)
        {
            query = query.Where(incident => incident.Status == status);
        }
        return await MapList(query).ToListAsync(cancellationToken);
    }

    public async Task<IncidentDetailResponse> GetIncidentAsync(
        Guid userId,
        string role,
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        bool canAccess = role switch
        {
            nameof(UserRole.Resident) => await dbContext.Incidents.AnyAsync(
                incident => incident.Id == incidentId && incident.ReporterUserId == userId,
                cancellationToken),
            nameof(UserRole.LockerOperator) or nameof(UserRole.Administrator) => await ScopeOperational(userId, role)
                .AnyAsync(incident => incident.Id == incidentId, cancellationToken),
            _ => false
        };
        if (!canAccess)
        {
            throw new NotFoundException(IncidentErrorCodes.NotFound, "Incident not found.");
        }
        return await LoadDetailAsync(incidentId, cancellationToken);
    }

    public async Task<IncidentDetailResponse> AddActionAsync(
        Guid userId,
        string role,
        Guid incidentId,
        AddIncidentActionRequest request,
        CancellationToken cancellationToken = default)
    {
        Incident incident = await FindOperationalIncidentAsync(userId, role, incidentId, cancellationToken);
        if (incident.Status == IncidentStatus.Resolved)
        {
            throw new ConflictException(IncidentErrorCodes.InvalidState, "Resolved incidents cannot receive new actions.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        dbContext.IncidentActions.Add(new IncidentAction
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ActionByUserId = userId,
            ActionType = "NoteAdded",
            Notes = RequireValue(request.Notes, nameof(request.Notes), 4000),
            CreatedAt = now
        });
        incident.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(incident, cancellationToken);
        return await LoadDetailAsync(incidentId, cancellationToken);
    }

    public async Task<IncidentDetailResponse> UpdateStatusAsync(
        Guid userId,
        string role,
        Guid incidentId,
        UpdateIncidentStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Status))
        {
            throw BadRequestException.For("status", "Incident status is invalid.");
        }

        Incident incident = await FindOperationalIncidentAsync(userId, role, incidentId, cancellationToken);
        if (!IsValidTransition(incident.Status, request.Status, role))
        {
            throw new ConflictException(IncidentErrorCodes.InvalidState, $"Cannot change incident from {incident.Status} to {request.Status}.");
        }

        string? notes = NormalizeOptional(request.Notes, nameof(request.Notes), 4000);
        string? resolution = NormalizeOptional(request.ResolutionSummary, nameof(request.ResolutionSummary), 4000);
        if (request.Status == IncidentStatus.Resolved && resolution is null)
        {
            throw BadRequestException.For("resolutionSummary", "Resolution summary is required when resolving an incident.");
        }
        if (request.Status == IncidentStatus.Escalated && notes is null)
        {
            throw BadRequestException.For("escalationNotes", "Escalation notes are required.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        IncidentStatus previousStatus = incident.Status;
        incident.Status = request.Status;
        incident.UpdatedAt = now;
        incident.EscalatedAt = request.Status == IncidentStatus.Escalated ? now : incident.EscalatedAt;
        incident.ResolvedAt = request.Status == IncidentStatus.Resolved ? now : null;
        incident.ResolutionSummary = request.Status == IncidentStatus.Resolved ? resolution : incident.ResolutionSummary;
        dbContext.IncidentActions.Add(new IncidentAction
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ActionByUserId = userId,
            ActionType = "StatusChanged",
            FromStatus = previousStatus,
            ToStatus = request.Status,
            Notes = notes ?? resolution,
            CreatedAt = now
        });

        if (incident.ReporterUserId.HasValue)
        {
            dbContext.Notifications.Add(CreateNotification(
                incident.ReporterUserId.Value,
                incident,
                "IncidentStatusChanged",
                "Incident updated",
                $"Incident '{incident.Title}' changed to {request.Status}.",
                now));
        }
        if (request.Status == IncidentStatus.Escalated)
        {
            List<Guid> administratorIds = await dbContext.Users
                .Where(user => user.Role == UserRole.Administrator && user.Status == UserStatus.Active && user.Id != userId)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            foreach (Guid administratorId in administratorIds)
            {
                dbContext.Notifications.Add(CreateNotification(
                    administratorId,
                    incident,
                    "IncidentEscalated",
                    "Incident escalated",
                    incident.Title,
                    now));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(incident, cancellationToken);
        return await LoadDetailAsync(incidentId, cancellationToken);
    }

    private IQueryable<Incident> ScopeOperational(Guid userId, string role) => role switch
    {
        nameof(UserRole.Administrator) => dbContext.Incidents,
        nameof(UserRole.LockerOperator) => dbContext.Incidents.Where(incident =>
            incident.LockerId.HasValue &&
            dbContext.OperatorAssignments.Any(assignment =>
                assignment.OperatorUserId == userId &&
                assignment.LockerId == incident.LockerId &&
                assignment.RevokedAt == null)),
        _ => throw new ForbiddenException(IncidentErrorCodes.AccessDenied, "Role cannot access operational incidents.")
    };

    private async Task<Incident> FindOperationalIncidentAsync(
        Guid userId,
        string role,
        Guid incidentId,
        CancellationToken cancellationToken) =>
        await ScopeOperational(userId, role)
            .Include(incident => incident.Actions)
            .SingleOrDefaultAsync(incident => incident.Id == incidentId, cancellationToken)
        ?? throw new NotFoundException(IncidentErrorCodes.NotFound, "Incident not found.");

    private async Task<IncidentDetailResponse> LoadDetailAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        Incident incident = await dbContext.Incidents
            .AsNoTracking()
            .Include(item => item.Locker)
            .Include(item => item.LockerCompartment)
            .Include(item => item.Parcel)
            .Include(item => item.ReturnRequest)
            .Include(item => item.AssignedOperatorUser)
                .ThenInclude(user => user!.ResidentProfile)
            .Include(item => item.Actions)
                .ThenInclude(action => action.ActionByUser)
                    .ThenInclude(user => user.ResidentProfile)
            .SingleAsync(item => item.Id == incidentId, cancellationToken);

        return new IncidentDetailResponse(
            incident.Id,
            incident.Type,
            incident.Source,
            incident.Status,
            incident.Title,
            incident.Description,
            incident.EvidenceUrl,
            incident.ReporterPhone,
            incident.LockerId!.Value,
            incident.Locker!.Code,
            incident.Locker.Address,
            incident.LockerCompartmentId,
            incident.LockerCompartment?.Code,
            incident.ParcelId,
            incident.Parcel?.ParcelCode,
            incident.ReturnRequestId,
            incident.ReturnRequest?.ReturnCode,
            incident.PaymentTransactionId,
            incident.AssignedOperatorUserId,
            incident.AssignedOperatorUser is null ? null : GetUserName(incident.AssignedOperatorUser),
            incident.ResolutionSummary,
            incident.EscalatedAt,
            incident.ResolvedAt,
            incident.CreatedAt,
            incident.UpdatedAt,
            incident.Actions
                .OrderBy(action => action.CreatedAt)
                .Select(action => new IncidentActionResponse(
                    action.Id,
                    action.ActionByUserId ?? Guid.Empty,
                    action.ActionByUser is null ? SystemActorName : GetUserName(action.ActionByUser),
                    action.ActionType,
                    action.FromStatus,
                    action.ToStatus,
                    action.Notes,
                    action.CreatedAt))
                .ToList());
    }

    private static IQueryable<IncidentListItemResponse> MapList(IQueryable<Incident> query) => query
        .OrderByDescending(incident => incident.UpdatedAt)
        .Select(incident => new IncidentListItemResponse(
            incident.Id,
            incident.Type,
            incident.Source,
            incident.Status,
            incident.Title,
            incident.LockerId!.Value,
            incident.Locker!.Code,
            incident.Locker.Address,
            incident.ParcelId,
            incident.Parcel == null ? null : incident.Parcel.ParcelCode,
            incident.ReturnRequestId,
            incident.ReturnRequest == null ? null : incident.ReturnRequest.ReturnCode,
            incident.PaymentTransactionId,
            incident.AssignedOperatorUserId,
            incident.CreatedAt,
            incident.UpdatedAt));

    private static bool IsValidTransition(IncidentStatus current, IncidentStatus next, string role) =>
        current switch
        {
            IncidentStatus.Open => next is IncidentStatus.Investigating or IncidentStatus.Escalated,
            IncidentStatus.Investigating => next is IncidentStatus.Resolved or IncidentStatus.Escalated,
            IncidentStatus.Escalated when role == nameof(UserRole.Administrator) =>
                next is IncidentStatus.Investigating or IncidentStatus.Resolved,
            _ => false
        };

    private static Notification CreateNotification(
        Guid userId,
        Incident incident,
        string type,
        string title,
        string message,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IncidentId = incident.Id,
            ParcelId = incident.ParcelId,
            PaymentTransactionId = incident.PaymentTransactionId,
            Type = type,
            Channel = NotificationChannel.InApp,
            Title = title,
            Message = message,
            DeliveryStatus = NotificationDeliveryStatus.Sent,
            SentAt = now,
            CreatedAt = now
        };

    private static string GetUserName(User user) =>
        user.ResidentProfile?.FullName ?? user.PhoneNumber ?? user.Email ?? user.Id.ToString();

    private static string RequireValue(string? value, string parameterName, int maxLength) =>
        NormalizeOptional(value, parameterName, maxLength)
        ?? throw BadRequestException.For(parameterName, $"{parameterName} is required.");

    private static string? NormalizeOptional(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw BadRequestException.For(parameterName, $"{parameterName} must not exceed {maxLength} characters.");
        }
        return trimmed;
    }

    private static string? ValidateOptionalUrl(string? value)
    {
        string? url = NormalizeOptional(value, "EvidenceUrl", 2048);
        if (url is not null &&
            (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw BadRequestException.For("evidenceUrl", "EvidenceUrl must be an absolute HTTP or HTTPS URL.");
        }
        return url;
    }
    public async Task<GuestIncidentResponse> CreateGuestIncidentAsync(
        CreateGuestIncidentRequest request,
        string? guestSessionToken,
        CancellationToken cancellationToken = default)
    {
        string type = RequireValue(request.Type, nameof(request.Type), 100);
        if (!SupportedTypes.Contains(type))
            throw BadRequestException.For("incidentType", "Incident type is invalid.");
        if (request.DeliveryRequestId.HasValue && request.ReturnRequestId.HasValue)
            throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Only one delivery request or return request may be linked.");

        if (string.IsNullOrWhiteSpace(request.LockerCode))
            throw BadRequestException.For(nameof(request.LockerCode), "Locker code is required.");
        Locker locker = await dbContext.Lockers.SingleOrDefaultAsync(
            item => item.Code == request.LockerCode.Trim(), cancellationToken)
            ?? throw new NotFoundException(LockerErrorCodes.NotFound, "Locker not found.");
        Guid? deliveryId = request.DeliveryRequestId;
        Guid? returnId = request.ReturnRequestId;
        if (!string.IsNullOrWhiteSpace(guestSessionToken))
        {
            string hash = tokenHashService.HashToken(guestSessionToken.Trim());
            DateTimeOffset current = timeProvider.GetUtcNow();
            DeliveryRequest? session = await dbContext.DeliveryRequests.AsNoTracking().SingleOrDefaultAsync(item =>
                item.GuestSessionTokenHash == hash && item.LockerId == locker.Id && item.SessionExpiresAt > current &&
                item.Status != DeliveryRequestStatus.Deposited && item.Status != DeliveryRequestStatus.Rejected &&
                item.Status != DeliveryRequestStatus.Expired && item.Status != DeliveryRequestStatus.Cancelled &&
                item.Status != DeliveryRequestStatus.Failed, cancellationToken);
            if (session is not null)
            {
                if (returnId.HasValue || (deliveryId.HasValue && deliveryId != session.Id))
                    throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Incident does not match the guest session.");
                deliveryId = session.Id;
            }
            else
            {
                ReturnRequest? pickup = await dbContext.ReturnRequests.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.ShipperSessionTokenHash == hash && item.LockerId == locker.Id &&
                    item.Status == ReturnRequestStatus.Deposited, cancellationToken);
                SystemPolicy? policy = await dbContext.SystemPolicies.AsNoTracking()
                    .Where(item => item.IsActive && item.EffectiveFrom <= current &&
                        (!item.EffectiveTo.HasValue || item.EffectiveTo > current))
                    .OrderByDescending(item => item.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);
                if (pickup is null || policy is null || pickup.UpdatedAt.AddMinutes(policy.GuestSessionTimeoutMinutes) <= current)
                    throw new UnauthorizedException(AuthErrorCodes.InvalidGuestSession, "Guest session is invalid or expired.");
                if (deliveryId.HasValue || (returnId.HasValue && returnId != pickup.Id))
                    throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Incident does not match the guest session.");
                returnId = pickup.Id;
            }
        }
        else if (deliveryId.HasValue || returnId.HasValue)
            throw new UnauthorizedException(AuthErrorCodes.MissingGuestSession, "X-Guest-Session-Token is required for a related request.");
        Guid? compartmentId = request.LockerCompartmentId;
        if (deliveryId.HasValue)
        {
            DeliveryRequest delivery = await dbContext.DeliveryRequests.SingleOrDefaultAsync(
                item => item.Id == deliveryId && item.LockerId == locker.Id,
                cancellationToken) ?? throw new NotFoundException(DeliveryErrorCodes.NotFound, "Delivery request not found.");
            if (compartmentId.HasValue && delivery.AllocatedCompartmentId.HasValue && compartmentId != delivery.AllocatedCompartmentId)
                throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Compartment does not match the guest session.");
            compartmentId ??= delivery.AllocatedCompartmentId;
        }
        if (returnId.HasValue)
        {
            ReturnRequest returnRequest = await dbContext.ReturnRequests.SingleOrDefaultAsync(
                item => item.Id == returnId && item.LockerId == locker.Id,
                cancellationToken) ?? throw new NotFoundException(ReturnErrorCodes.NotFound, "Return request not found.");
            if (compartmentId.HasValue && returnRequest.AllocatedCompartmentId.HasValue && compartmentId != returnRequest.AllocatedCompartmentId)
                throw new BadRequestException(IncidentErrorCodes.InvalidLink, "Compartment does not match the guest session.");
            compartmentId ??= returnRequest.AllocatedCompartmentId;
        }
        if (compartmentId.HasValue && !await dbContext.LockerCompartments.AnyAsync(
            item => item.Id == compartmentId && item.LockerId == locker.Id, cancellationToken))
            throw new NotFoundException(LockerErrorCodes.InvalidCompartment, "Locker compartment not found.");

        Guid? assignedOperatorUserId = await dbContext.OperatorAssignments
            .Where(item => item.LockerId == locker.Id && item.RevokedAt == null && item.OperatorUser.Status == UserStatus.Active)
            .Select(item => (Guid?)item.OperatorUserId)
            .SingleOrDefaultAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Incident incident = new()
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = deliveryId,
            ReturnRequestId = returnId,
            LockerId = locker.Id,
            LockerCompartmentId = compartmentId,
            AssignedOperatorUserId = assignedOperatorUserId,
            Type = SupportedTypes.Single(candidate => candidate.Equals(type, StringComparison.OrdinalIgnoreCase)),
            Source = IncidentSource.Guest,
            Status = IncidentStatus.Open,
            Title = RequireValue(request.Title, nameof(request.Title), 200),
            Description = RequireValue(request.Description, nameof(request.Description), 4000),
            EvidenceUrl = ValidateOptionalUrl(request.EvidenceUrl),
            ReporterPhone = NormalizeOptional(request.ContactPhone, nameof(request.ContactPhone), 20),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Incidents.Add(incident);

        IReadOnlyCollection<Guid> recipients = assignedOperatorUserId.HasValue
            ? [assignedOperatorUserId.Value]
            : await dbContext.Users.Where(user => user.Role == UserRole.Administrator && user.Status == UserStatus.Active)
                .Select(user => user.Id).ToListAsync(cancellationToken);
        foreach (Guid recipient in recipients)
            dbContext.Notifications.Add(CreateNotification(recipient, incident, "GuestIncidentCreated", "Guest incident needs attention", incident.Title, now));
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = "Incident.GuestCreated",
            EntityType = nameof(Incident),
            EntityId = incident.Id,
            Result = AuditLogResult.Succeeded,
            Details = $"LockerId={locker.Id}",
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(incident, cancellationToken);
        return new GuestIncidentResponse(incident.Id, $"INC-{incident.Id.ToString("N")[..8].ToUpperInvariant()}", incident.Status, now);
    }


    private async Task PublishAsync(Incident incident, CancellationToken cancellationToken)
    {
        if (realtimeNotifier is null) return;
        RealtimeEvent message = new("IncidentUpdated", incident.Id, incident.LockerId,
            incident.Status.ToString(), incident.Title, incident.UpdatedAt);
        if (incident.LockerId.HasValue)
            await realtimeNotifier.PublishToLockerAsync(incident.LockerId.Value, message, cancellationToken);
        if (incident.ReporterUserId.HasValue)
            await realtimeNotifier.PublishToUserAsync(incident.ReporterUserId.Value, message, cancellationToken);
    }


}
