// Controllers/SmsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Services;
using System.Net.Http;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class SmsController : Controller
    {
        private readonly ISmsService _smsService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<SmsController> _logger;
        private readonly ApplicationDbContext _context;

        public SmsController(
            ApplicationDbContext context,
            ISmsService smsService,
            ICompanyContextService companyContextService,
            ILogger<SmsController> logger)
        {
            _context = context;
            _smsService = smsService;
            _companyContextService = companyContextService;
            _logger = logger;
        }

        // GET: /Sms/Index
        public async Task<IActionResult> Index(string status = null, string phoneNumber = null, int page = 1)
        {
            try
            {
                ViewBag.CurrentStatus = status;
                ViewBag.CurrentPhoneNumber = phoneNumber;
                ViewBag.CurrentPage = page;

                List<SmsResponseDTO> messages;

                if (!string.IsNullOrEmpty(phoneNumber))
                {
                    messages = await _smsService.GetSmsByPhoneNumberAsync(phoneNumber, page);
                    ViewBag.SearchType = "phone";
                }
                else if (!string.IsNullOrEmpty(status))
                {
                    messages = await _smsService.GetSmsByStatusAsync(status, page);
                    ViewBag.SearchType = "status";
                }
                else
                {
                    // Get all messages with pagination
                    var stats = await _smsService.GetSmsStatisticsAsync();
                    messages = stats.RecentMessages;
                }

                var statistics = await _smsService.GetSmsStatisticsAsync();

                ViewBag.Statistics = statistics;
                return View(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading SMS index");
                TempData["ErrorMessage"] = "Error loading SMS messages";
                return View(new List<SmsResponseDTO>());
            }
        }

        // GET: /Sms/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var sms = await _smsService.GetSmsByIdAsync(id);
                if (sms == null)
                {
                    TempData["ErrorMessage"] = "SMS message not found";
                    return RedirectToAction("Index");
                }

                return View(sms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading SMS details");
                TempData["ErrorMessage"] = "Error loading SMS details";
                return RedirectToAction("Index");
            }
        }

        // GET: /Sms/Send
        public async Task<IActionResult> Send()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);

                // Get company name from Companies table based on company code
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == companyCode);

                ViewBag.CompanyName = company?.CompanyName ?? companyCode;
                ViewBag.Templates = templates;
                ViewBag.CompanyCode = companyCode;

                return View(new SendSmsRequestDTO());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading send SMS form");
                TempData["ErrorMessage"] = "Error loading form";
                return RedirectToAction("Index");
            }
        }

        // POST: /Sms/Send
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(SendSmsRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var companyCode = _companyContextService.GetCurrentCompanyCode();
                    var templates = await _smsService.GetAllTemplatesAsync(companyCode);

                    // Get company name from Companies table
                    var company = await _context.Companies
                        .FirstOrDefaultAsync(c => c.CompanyCode == companyCode);

                    ViewBag.CompanyName = company?.CompanyName ?? companyCode ?? "JUHUDI SACCO";
                    ViewBag.Templates = templates;
                    ViewBag.CompanyCode = companyCode;

                    return View(request);
                }

                var result = await _smsService.SendSmsAsync(request);

                TempData["SuccessMessage"] = $"SMS sent successfully to {request.PhoneNumber}";
                return RedirectToAction("Details", new { id = result.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SMS");
                ModelState.AddModelError("", ex.Message);

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);

                // Get company name from Companies table
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == companyCode);

                ViewBag.CompanyName = company?.CompanyName ?? companyCode;
                ViewBag.Templates = templates;
                ViewBag.CompanyCode = companyCode;

                return View(request);
            }
        }

        //// GET: /Sms/Send
        //public async Task<IActionResult> Send()
        //{
        //    try
        //    {
        //        var companyCode = _companyContextService.GetCurrentCompanyCode();
        //        var templates = await _smsService.GetAllTemplatesAsync(companyCode);
        //        ViewBag.Templates = templates;
        //        return View(new SendSmsRequestDTO());
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error loading send SMS form");
        //        TempData["ErrorMessage"] = "Error loading form";
        //        return RedirectToAction("Index");
        //    }
        //}

        //// POST: /Sms/Send
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> Send(SendSmsRequestDTO request)
        //{
        //    try
        //    {
        //        if (!ModelState.IsValid)
        //        {
        //            var companyCode = _companyContextService.GetCurrentCompanyCode();
        //            var templates = await _smsService.GetAllTemplatesAsync(companyCode);
        //            ViewBag.Templates = templates;
        //            return View(request);
        //        }

        //        var result = await _smsService.SendSmsAsync(request);

        //        TempData["SuccessMessage"] = $"SMS sent successfully to {request.PhoneNumber}";
        //        return RedirectToAction("Details", new { id = result.Id });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error sending SMS");
        //        ModelState.AddModelError("", ex.Message);

        //        var companyCode = _companyContextService.GetCurrentCompanyCode();
        //        var templates = await _smsService.GetAllTemplatesAsync(companyCode);
        //        ViewBag.Templates = templates;
        //        return View(request);
        //    }
        //}

        // GET: /Sms/BulkSend
        public async Task<IActionResult> BulkSend()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);
                ViewBag.Templates = templates;
                return View(new BulkSmsRequestDTO());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading bulk send form");
                TempData["ErrorMessage"] = "Error loading form";
                return RedirectToAction("Index");
            }
        }

        // POST: /Sms/BulkSend
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkSend(BulkSmsRequestDTO request)
        {
            try
            {
                if (request.Messages == null || !request.Messages.Any())
                {
                    ModelState.AddModelError("", "Please add at least one message");
                    var companyCode = _companyContextService.GetCurrentCompanyCode();
                    var templates = await _smsService.GetAllTemplatesAsync(companyCode);
                    ViewBag.Templates = templates;
                    return View(request);
                }

                var results = await _smsService.SendBulkSmsAsync(request);

                var sentCount = results.Count(r => r.Status == "Sent");
                var failedCount = results.Count(r => r.Status == "Failed");

                TempData["SuccessMessage"] = $"Bulk SMS completed: {sentCount} sent, {failedCount} failed";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending bulk SMS");
                ModelState.AddModelError("", ex.Message);

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);
                ViewBag.Templates = templates;
                return View(request);
            }
        }

        [HttpGet]
        public async Task<IActionResult> RecentMessages()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var messages = await _smsService.GetSmsByStatusAsync("", 1, 5);

                return Json(new
                {
                    success = true,
                    messages = messages.Select(m => new
                    {
                        m.Id,
                        m.PhoneNumber,
                        m.MessageContent,
                        m.Status,
                        m.CreatedAt
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent messages");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Sms/Templates
        public async Task<IActionResult> Templates()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);
                return View(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading templates");
                TempData["ErrorMessage"] = "Error loading templates";
                return View(new List<SmsTemplate>());
            }
        }

        // GET: /Sms/CreateTemplate
        public IActionResult CreateTemplate()
        {
            return View(new SmsTemplateDTO());
        }

        // POST: /Sms/CreateTemplate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTemplate(SmsTemplateDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(dto);
                }

                await _smsService.CreateTemplateAsync(dto, User.Identity?.Name ?? "SYSTEM");

                TempData["SuccessMessage"] = $"Template '{dto.TemplateName}' created successfully";
                return RedirectToAction("Templates");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating template");
                ModelState.AddModelError("", ex.Message);
                return View(dto);
            }
        }

        // GET: /Sms/EditTemplate/5
        public async Task<IActionResult> EditTemplate(int id)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);
                var template = templates.FirstOrDefault(t => t.Id == id);

                if (template == null)
                {
                    TempData["ErrorMessage"] = "Template not found";
                    return RedirectToAction("Templates");
                }

                var dto = new SmsTemplateDTO
                {
                    Id = template.Id,
                    TemplateCode = template.TemplateCode,
                    TemplateName = template.TemplateName,
                    TemplateContent = template.TemplateContent,
                    Description = template.Description,
                    IsActive = template.IsActive
                };

                return View(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading edit template form");
                TempData["ErrorMessage"] = "Error loading form";
                return RedirectToAction("Templates");
            }
        }

        // POST: /Sms/EditTemplate/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTemplate(int id, SmsTemplateDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(dto);
                }

                await _smsService.UpdateTemplateAsync(id, dto, User.Identity?.Name ?? "SYSTEM");

                TempData["SuccessMessage"] = $"Template '{dto.TemplateName}' updated successfully";
                return RedirectToAction("Templates");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating template");
                ModelState.AddModelError("", ex.Message);
                return View(dto);
            }
        }

        // POST: /Sms/DeleteTemplate/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTemplate(int id)
        {
            try
            {
                await _smsService.DeleteTemplateAsync(id);
                return Json(new { success = true, message = "Template deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting template");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTemplate(int id)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);
                var template = templates.FirstOrDefault(t => t.Id == id);

                if (template == null)
                {
                    return Json(new { success = false, message = "Template not found" });
                }

                return Json(new
                {
                    success = true,
                    template = new
                    {
                        template.Id,
                        template.TemplateCode,
                        template.TemplateName,
                        template.Description,
                        template.TemplateContent,
                        template.IsActive,
                        template.CreatedAt,
                        template.CreatedBy
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting template");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendTestSms([FromBody] TestSmsRequest request)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var templates = await _smsService.GetAllTemplatesAsync(companyCode);
                var template = templates.FirstOrDefault(t => t.Id == request.TemplateId);

                if (template == null)
                {
                    return Json(new { success = false, message = "Template not found" });
                }

                // Prepare parameters
                var parameters = request.Parameters ?? new Dictionary<string, string>();

                // Add default parameters if not provided
                if (!parameters.ContainsKey("CompanyName"))
                {
                    var company = await _context.SaccoParram
                        .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);
                    parameters["CompanyName"] = company?.SaccoName ?? "JUHUDI SACCO";
                }
                if (!parameters.ContainsKey("Date"))
                {
                    parameters["Date"] = DateTime.Now.ToString("dd/MM/yyyy");
                }

                var result = await _smsService.SendTemplateSmsAsync(
                    template.TemplateCode,
                    request.PhoneNumber,
                    request.RecipientName ?? "Test User",
                    parameters,
                    "TEST_SMS");

                return Json(new { success = true, message = $"Test SMS sent to {request.PhoneNumber}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test SMS");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Add this class for the test request
        public class TestSmsRequest
        {
            public int TemplateId { get; set; }
            public string PhoneNumber { get; set; }
            public string RecipientName { get; set; }
            public Dictionary<string, string> Parameters { get; set; }
        }

        // GET: /Sms/Settings
        public async Task<IActionResult> Settings()
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var settings = await _smsService.GetSmsSettingsAsync(companyCode);

                // Get company name for display - FILTER BY COMPANY CODE
                var company = await _context.SaccoParram
                    .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);  // Add this filter

                // Also try to get from Member if SaccoParram doesn't have it
                if (company == null)
                {
                    var firstMember = await _context.Members
                        .FirstOrDefaultAsync(m => m.CompanyCode == companyCode);
                    if (firstMember != null)
                    {
                        ViewBag.CompanyName = firstMember.CompanyCode ?? "JUHUDI SACCO";
                    }
                    else
                    {
                        ViewBag.CompanyName = companyCode ?? "JUHUDI SACCO";
                    }
                }
                else
                {
                    ViewBag.CompanyName = company.SaccoName ?? companyCode ?? "JUHUDI SACCO";
                }

                var dto = new SmsSettingDTO
                {
                    Provider = settings.Provider,
                    ApiKey = settings.ApiKey,
                    ApiSecret = settings.ApiSecret,
                    SenderId = settings.SenderId,
                    Username = settings.Username,
                    ShortCode = settings.ShortCode,
                    IsEnabled = settings.IsEnabled,
                    SendOnRegistration = settings.SendOnRegistration,
                    SendOnWithdrawal = settings.SendOnWithdrawal,
                    SendOnLoanApproval = settings.SendOnLoanApproval,
                    SendOnShareTransfer = settings.SendOnShareTransfer,
                    SendOnContribution = settings.SendOnContribution,
                    SendOnLoanRepayment = settings.SendOnLoanRepayment,
                    SendOnAGM = settings.SendOnAGM,
                    SendOnDeposits = settings.SendOnDeposits,
                    CostPerSms = settings.CostPerSms,
                    ApiEndpoint = settings.ApiEndpoint
                };

                return View(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading SMS settings");
                TempData["ErrorMessage"] = "Error loading settings";
                return View(new SmsSettingDTO());
            }
        }

        // POST: /Sms/Settings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(SmsSettingDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(dto);
                }

                var companyCode = _companyContextService.GetCurrentCompanyCode();
                await _smsService.UpdateSmsSettingsAsync(companyCode, dto, User.Identity?.Name ?? "SYSTEM");

                TempData["SuccessMessage"] = "SMS settings updated successfully";
                return RedirectToAction("Settings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating SMS settings");
                ModelState.AddModelError("", ex.Message);
                return View(dto);
            }
        }

        // POST: /Sms/Resend/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Resend(int id)
        {
            try
            {
                var sms = await _smsService.GetSmsByIdAsync(id);
                if (sms == null)
                {
                    return Json(new { success = false, message = "SMS not found" });
                }

                var request = new SendSmsRequestDTO
                {
                    PhoneNumber = sms.PhoneNumber,
                    RecipientName = sms.RecipientName,
                    Message = sms.MessageContent,
                    MessageType = sms.MessageType,
                    Reference = sms.Reference
                };

                var result = await _smsService.SendSmsAsync(request);
                return Json(new { success = true, message = "SMS resent successfully", id = result.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending SMS");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Sms/Statistics
        public async Task<IActionResult> Statistics(DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                var statistics = await _smsService.GetSmsStatisticsAsync(fromDate, toDate);
                ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
                ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
                return View(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading SMS statistics");
                TempData["ErrorMessage"] = "Error loading statistics";
                return View(new SmsStatisticsDTO());
            }
        }

        // POST: /Sms/Webhook/DeliveryStatus
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> DeliveryStatus([FromForm] string providerMessageId, [FromForm] string status, [FromForm] string errorMessage)
        {
            try
            {
                await _smsService.UpdateDeliveryStatusAsync(providerMessageId, status, errorMessage);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing delivery webhook");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}