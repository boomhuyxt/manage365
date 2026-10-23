using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using manage365.Routes.API.Auth;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Tags("Attendance")]
[Route("api/attendance-qr")]
public sealed class AttendanceKioskController(IQrSignatureService qrSignatureService) : ControllerBase
{
    [HttpGet("kiosk")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<KioskQrResponse>(StatusCodes.Status200OK)]
    public ActionResult<KioskQrResponse> GetKioskQr(
        [FromQuery] string storeCode = "STORE-01",
        [FromQuery] string storeName = "Chi Nhánh Bến Nghé, Quận 1")
    {
        var response = qrSignatureService.CreateKioskQr(storeCode, storeName);
        return Ok(response);
    }
}
