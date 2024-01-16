using System;
using Microsoft.AspNetCore.Mvc;
using OpenAI_API.Models;
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
                Console.WriteLine($"RAW MODEL Data: {values.value}");
                if (isDataValid(values.value))
                    subCall.PostDataProcess(values);
                else
                    return BadRequest(new { Message = "API was called, but data was in incorrect format" });
            }
            else
            {
                Console.WriteLine("No DATA");
                return BadRequest(new { Message = "API was called, but no data was recieved" });
            }
            return Ok(new { Message = "Data received successfully." });
        }
        private bool isDataValid(string data)
        {
            string[] words = data.Split(' ');
            if (words.Length > 8 + 1)
                return true;
            else
                return false;
        }
    }   
}
