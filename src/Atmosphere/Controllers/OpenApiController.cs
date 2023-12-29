using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Atmosphere.Controllers
{
    [Route(".metadata/open-api")]
    [ApiController]
    public class OpenApiController : ControllerBase
    {
        private readonly OpenApiBuilder openApiBuilder;

        public OpenApiController(
            OpenApiBuilder openApiBuilder)
        {
            this.openApiBuilder = openApiBuilder;
        }

        [HttpGet]
        //[OutputCache(Duration = 60 * 60)] // 1 hour
        public async Task<IActionResult> Get()
        {
            var openApiSpec = await this.openApiBuilder.BuildAsync();

            return Content(openApiSpec.ToString(), "application/json");
        }
    }
}
