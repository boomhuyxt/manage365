using Microsoft.AspNetCore.Mvc;

namespace manage365.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestApiController : ControllerBase
    {
        /// <summary>
        /// Endpoint kiểm tra kết nối API và Swagger
        /// </summary>
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "success",
                message = "Swagger đã được tích hợp thành công vào manage365!",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
