using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    public class WMIController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
