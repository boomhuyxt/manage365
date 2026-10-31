namespace manage365.Routes.API.Attendance;

public sealed record CreateBleCredentialRequest(string DeviceId);
public sealed record CreateBleCredentialResponse(Guid CredentialId, string AdvertisementToken, DateTimeOffset ExpiresAtUtc, int RefreshAfterSeconds);
public sealed record RegisterBleScannerRequest(string ScannerId, string StoreCode, long? ShopOwnerId = null);
public sealed record RegisterBleScannerResponse(string ScannerId, string StoreCode, string ScannerToken);
public sealed record BleScannerEventRequest(Guid BatchId, DateTimeOffset ObservedAtUtc, IReadOnlyList<BleObservationRequest> Observations, string ScannerState = "running", string? ScannerMessage = null);
public sealed record BleObservationRequest(string AdvertisementToken, int RssiDbm, long Sequence);
public sealed record BleCheckoutRequest(long? ShiftAssignmentId, Guid CredentialId, string PresenceToken, Guid IdempotencyKey, string ScannerId);
public sealed record BleAttendanceResponse(bool Success, string Message, string? ErrorCode, long? ShiftAssignmentId, string? ShiftName, string Action, DateTimeOffset TimestampUtc, decimal? ActualHours);
public sealed record BleAttendanceStatusResponse(bool ScannerOnline, bool BleReady, DateTimeOffset? LastObservedAtUtc, string? ScannerId, string? LastAction, string? LastShiftName, DateTimeOffset? LastAttendanceAtUtc, string? Message);
public sealed record BleScannerSummary(string ScannerId, string StoreCode, bool IsActive, DateTimeOffset? LastSeenAt, bool Online, string ScannerState, string? ScannerMessage, long EmployeeCount, string? AgentVersion = null);
public sealed record BleScannerControlRequest(string State, string? Message, string Version);
public sealed record BleScannerControlResponse(bool Enabled, int PollSeconds = 5);
public sealed record BleEmployeePresence(long EmployeeId, string DisplayName, string Status, DateTimeOffset? LastObservedAt, int? RssiDbm, string? ResultCode, string? Message);
public sealed record BleAttendanceEvent(string DisplayName, string Action, DateTimeOffset Timestamp, string ShiftName);
public sealed record BleScannerOverview(IReadOnlyList<BleEmployeePresence> Employees, IReadOnlyList<BleAttendanceEvent> Events);
public sealed record BleScannerActiveRequest(bool IsActive);
public sealed record BleScannerPackageRequest(string ApiBaseUrl);
