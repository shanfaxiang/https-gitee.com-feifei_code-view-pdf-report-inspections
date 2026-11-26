using Microsoft.AspNetCore.Mvc;

namespace Dezhen.PdfViewer.Controllers
{
    [Route("[controller]")]
    public class HealthController : ControllerBase
    {
        /// <summary>
        /// 心跳检测
        /// </summary>
        [HttpGet]
        public IActionResult Heartbeat()
        {
            return Ok("9999");
        }
    }
}
