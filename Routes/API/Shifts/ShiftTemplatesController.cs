using manage365.Repositories.Shifts;
using manage365.Routes.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace manage365.Routes.API.Shifts;

[ApiController]
[ShiftApiExceptionFilter]
[Tags("Shift Templates - Mẫu Ca Làm Việc")]
[Route("api/shift-templates")]
public sealed class ShiftTemplatesController(IShiftSchedulingRepository shiftRepo) : ControllerBase
{
    /// <summary>
    /// Lấy danh sách tất cả các mẫu ca làm việc của cửa hàng
    /// </summary>
    /// <param name="activeOnly">Chỉ lấy các ca đang hoạt động (mặc định: true)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<List<ShiftTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ShiftTemplateDto>>> GetTemplates(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var templates = await shiftRepo.GetTemplatesAsync(activeOnly, cancellationToken);
        return Ok(templates);
    }

    /// <summary>
    /// Lấy thông tin chi tiết một mẫu ca làm việc theo ID
    /// </summary>
    /// <param name="id">ID của mẫu ca</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("{id:long}")]
    [Authorize]
    [ProducesResponseType<ShiftTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShiftTemplateDto>> GetTemplateById(
        long id,
        CancellationToken cancellationToken = default)
    {
        var template = await shiftRepo.GetTemplateByIdAsync(id, cancellationToken);
        if (template is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy mẫu ca làm việc có ID {id}.",
                extensions: new Dictionary<string, object?> { ["code"] = "shift_template_not_found" });
        }

        return Ok(template);
    }

    /// <summary>
    /// Tạo mới một mẫu ca làm việc cho cửa hàng (Admin / Manager)
    /// </summary>
    /// <param name="request">Thông tin mẫu ca mới (mã, tên, giờ bắt đầu, kết thúc, cờ qua đêm, màu sắc)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<ShiftTemplateDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ShiftTemplateDto>> CreateTemplate(
        [FromBody] CreateShiftTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Mã mẫu ca và Tên mẫu ca không được để trống.",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_arguments" });
        }

        try
        {
            ShiftInput.Template(request.Code, request.Name, request.StartTime, request.EndTime, request.IsOvernight, request.Color, request.Description);
            var created = await shiftRepo.CreateTemplateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetTemplateById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_shift_time" });
        }
    }

    /// <summary>
    /// Cập nhật thông tin mẫu ca làm việc (Admin / Manager)
    /// </summary>
    /// <param name="id">ID mẫu ca</param>
    /// <param name="request">Thông tin cập nhật</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPut("{id:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType<ShiftTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShiftTemplateDto>> UpdateTemplate(
        long id,
        [FromBody] UpdateShiftTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ShiftInput.Template(null, request.Name, request.StartTime, request.EndTime, request.IsOvernight, request.Color, request.Description);
            if (request.Status is not null && request.Status is not ("Active" or "Inactive"))
                throw new ArgumentException("Trạng thái mẫu ca phải là Active hoặc Inactive.");
            var updated = await shiftRepo.UpdateTemplateAsync(id, request, cancellationToken);
            if (updated is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: $"Không tìm thấy mẫu ca ID {id} để cập nhật.",
                    extensions: new Dictionary<string, object?> { ["code"] = "shift_template_not_found" });
            }

            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_shift_time" });
        }
    }

    /// <summary>
    /// Vô hiệu hóa một mẫu ca làm việc (Admin / Manager)
    /// </summary>
    /// <param name="id">ID mẫu ca</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = AppRoles.Managers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTemplate(
        long id,
        CancellationToken cancellationToken = default)
    {
        var success = await shiftRepo.DeleteTemplateAsync(id, cancellationToken);
        if (!success)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"Không tìm thấy mẫu ca ID {id} để xóa.",
                extensions: new Dictionary<string, object?> { ["code"] = "shift_template_not_found" });
        }

        return NoContent();
    }
}
