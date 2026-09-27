using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Attendance;

[ApiController]
[Route("api/attendance-qr")]
public sealed class AttendanceKioskController(IQrSignatureService qrSignatureService) : ControllerBase
{
    [HttpGet("kiosk")]
    [Authorize]
    [ProducesResponseType<KioskQrResponse>(StatusCodes.Status200OK)]
    public ActionResult<KioskQrResponse> GetKioskQr(
        [FromQuery] string storeCode = "STORE-01",
        [FromQuery] string storeName = "Chi Nhánh Bến Nghé, Quận 1")
    {
        var response = qrSignatureService.CreateKioskQr(storeCode, storeName);
        return Ok(response);
    }
}
