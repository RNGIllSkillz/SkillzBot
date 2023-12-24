using System;
using Microsoft.AspNetCore.Mvc;
using SkillzBot.MODELS;
using SkillzBot.SubUtils;

namespace SkillzBot.Controller
{    
    [Route("api/[controller]")]
    [ApiController]
    public class SubController : ControllerBase
    {
        [HttpPost]
        public IActionResult Post([FromForm] apiPost values)
        {
            SubCall subCall = new SubCall();
            if (!string.IsNullOrEmpty(values?.value))
            {
                subCall.PostDataProcess(values);
            }
            else
            {
                Console.WriteLine("No DATA");
            }
            return Ok(new { Message = "Data received successfully." });
        }
    }
}
