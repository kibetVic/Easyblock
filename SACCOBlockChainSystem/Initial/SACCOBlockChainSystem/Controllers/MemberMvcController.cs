// Controllers/MemberMvcController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;
using System.Security.Claims;
using System.Text.Json;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class MemberMvcController : Controller
    {
        private readonly IMemberService _memberService;
        private readonly ApplicationDbContext _context;
        private readonly AuditTrailService _auditService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<MemberMvcController> _logger;

        public MemberMvcController(
            IMemberService memberService,
            ApplicationDbContext context,
            ICompanyContextService companyContextService,
            ILogger<MemberMvcController> logger, AuditTrailService audit)
        {
            _memberService = memberService;
            _context = context;
            _companyContextService = companyContextService;
            _auditService = audit;
            _logger = logger;
        }


        public async Task<IActionResult> Index()
        {
            try
            {
                // Get current company code from user context
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

                // LOG: What's the current company code?
                _logger.LogInformation($"=== DEBUG: Current Company Code from context: '{currentCompanyCode}' ===");

                // Get ALL members from database (no filter first to see what exists)
                var allMembersInDb = await _context.Members.ToListAsync();
                _logger.LogInformation($"=== DEBUG: Total members in database: {allMembersInDb.Count} ===");

                // Log all company codes found in members
                var distinctCompanyCodes = allMembersInDb.Select(m => m.CompanyCode).Distinct().ToList();
                _logger.LogInformation($"=== DEBUG: Company codes in members: {string.Join(", ", distinctCompanyCodes)} ===");

                // Log first few members with their company codes
                foreach (var member in allMembersInDb.Take(5))
                {
                    _logger.LogInformation($"=== DEBUG: Member: {member.MemberNo}, CompanyCode: '{member.CompanyCode}' ===");
                }

                // Get members for current company
                var allMembers = await _context.Members
                    .Where(m => m.CompanyCode == currentCompanyCode && (m.Archived == false || m.Archived == null))
                    .OrderByDescending(m => m.ApplicDate)
                    .ToListAsync();

                _logger.LogInformation($"=== DEBUG: Members found for company '{currentCompanyCode}': {allMembers.Count} ===");

                // If no members found, check if the company code format might be different
                if (allMembers.Count == 0 && allMembersInDb.Any())
                {
                    // Try to find members with similar company code (case-insensitive or partial match)
                    var similarMembers = allMembersInDb
                        .Where(m => m.CompanyCode != null &&
                                   (m.CompanyCode.Equals(currentCompanyCode, StringComparison.OrdinalIgnoreCase) ||
                                    m.CompanyCode.Contains(currentCompanyCode) ||
                                    currentCompanyCode.Contains(m.CompanyCode)))
                        .ToList();

                    _logger.LogInformation($"=== DEBUG: Similar members found: {similarMembers.Count} ===");

                    if (similarMembers.Any())
                    {
                        _logger.LogInformation($"=== DEBUG: First similar member CompanyCode: '{similarMembers.First().CompanyCode}' ===");
                    }
                }

                // Get only active members (Status = 1)
                var activeMembers = allMembers.Where(m => m.Status == 1).ToList();

                // Calculate statistics
                var totalMembers = allMembers.Count;
                var activeMembersCount = activeMembers.Count;
                var totalShareCapital = allMembers.Sum(m => m.ShareCap ?? 0);
                var blockchainVerifiedCount = allMembers.Count(m => !string.IsNullOrEmpty(m.BlockchainTxId));

                // Get top 10 members
                var topMembers = allMembers
                    .OrderByDescending(m => m.ShareCap ?? 0)
                    .Take(10)
                    .ToList();

                // Create view model
                var viewModel = new MembersIndexViewModel
                {
                    Members = topMembers,
                    AllMembers = allMembers,
                    TotalMembers = totalMembers,
                    ActiveMembers = activeMembersCount,
                    TotalShareCapital = totalShareCapital,
                    BlockchainVerifiedCount = blockchainVerifiedCount,
                    UserCompanyCode = currentCompanyCode
                };

                // LOG: What we're sending to the view
                _logger.LogInformation($"Index view loaded: Total Members: {totalMembers}, Active: {activeMembersCount}, Company: {currentCompanyCode}");

                // Add a debug message to TempData if no members found
                if (totalMembers == 0)
                {
                    TempData["DebugMessage"] = $"No members found for company code: '{currentCompanyCode}'. Available codes: {string.Join(", ", distinctCompanyCodes)}";
                }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading members index");
                TempData["ErrorMessage"] = "Error loading members list: " + ex.Message;

                var emptyViewModel = new MembersIndexViewModel
                {
                    Members = new List<Member>(),
                    AllMembers = new List<Member>(),
                    TotalMembers = 0,
                    ActiveMembers = 0,
                    TotalShareCapital = 0,
                    BlockchainVerifiedCount = 0,
                    UserCompanyCode = _companyContextService.GetCurrentCompanyCode()
                };

                return View(emptyViewModel);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Register()
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            // Get CIGs for dropdown
            var cigs = await _context.CIGs
                .Where(c => c.CompanyCode == currentCompanyCode && c.Status == "Active")
                .OrderBy(c => c.GigName)
                .ToListAsync();

            // Get Counties for dropdown
            var counties = await _context.Counties
                .Where(c => c.Status == "Active")
                .OrderBy(c => c.CountyName)
                .ToListAsync();

            ViewBag.CIGs = cigs;
            ViewBag.Counties = counties;

            // Generate a preview member number for display
            var previewMemberNo = await GeneratePreviewMemberNumberAsync(currentCompanyCode);

            var model = new MemberRegistrationDTO
            {
                MemberNo = previewMemberNo,  // Show generated number
                RegistrationDate = DateTime.Now,
                Status = "Active",
                CompanyCode = currentCompanyCode  // Set the company code
            };

            return View(model);
        }

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> Register(MemberRegistrationDTO model)
        //{
        //    try
        //    {
        //        // Set default values
        //        if (string.IsNullOrEmpty(model.MemberNo))
        //        {
        //            model.MemberNo = await GeneratePreviewMemberNumberAsync(model.CompanyCode);
        //        }

        //        model.CompanyCode = _companyContextService.GetCurrentCompanyCode();
        //        model.CreatedBy = User.Identity?.Name ?? "SYSTEM";
        //        model.RegistrationDate = DateTime.Now;

        //        // Clear ModelState to re-validate with our custom rules
        //        ModelState.Clear();

        //        // Validate required fields
        //        if (string.IsNullOrEmpty(model.Surname))
        //        {
        //            ModelState.AddModelError("Surname", "Surname is required");
        //        }

        //        if (string.IsNullOrEmpty(model.OtherNames))
        //        {
        //            ModelState.AddModelError("OtherNames", "Other Names are required");
        //        }

        //        // Check for duplicate ID Number
        //        if (!string.IsNullOrEmpty(model.IdNo))
        //        {
        //            var existingIdNo = await _context.Members
        //                .AnyAsync(m => m.Idno == model.IdNo && m.CompanyCode == model.CompanyCode);

        //            if (existingIdNo)
        //            {
        //                ModelState.AddModelError("IdNo", $"ID Number '{model.IdNo}' is already registered to another member");
        //            }
        //        }
        //        else
        //        {
        //            ModelState.AddModelError("IdNo", "ID Number is required");
        //        }

        //        // Check for duplicate Phone Number
        //        if (!string.IsNullOrEmpty(model.PhoneNo))
        //        {
        //            var existingPhone = await _context.Members
        //                .AnyAsync(m => m.PhoneNo == model.PhoneNo && m.CompanyCode == model.CompanyCode);

        //            if (existingPhone)
        //            {
        //                ModelState.AddModelError("PhoneNo", $"Phone Number '{model.PhoneNo}' is already registered to another member");
        //            }
        //        }
        //        else
        //        {
        //            ModelState.AddModelError("PhoneNo", "Phone Number is required");
        //        }

        //        // Check for duplicate Email (if provided)
        //        if (!string.IsNullOrEmpty(model.Email))
        //        {
        //            var existingEmail = await _context.Members
        //                .AnyAsync(m => m.Email == model.Email && m.CompanyCode == model.CompanyCode);

        //            if (existingEmail)
        //            {
        //                ModelState.AddModelError("Email", $"Email '{model.Email}' is already registered to another member");
        //            }
        //        }

        //        // Check for duplicate Member Number
        //        var existingMemberNo = await _context.Members
        //            .AnyAsync(m => m.MemberNo == model.MemberNo && m.CompanyCode == model.CompanyCode);

        //        if (existingMemberNo)
        //        {
        //            ModelState.AddModelError("MemberNo", $"Member Number '{model.MemberNo}' already exists");
        //        }

        //        // If any validation errors, return to form with errors
        //        if (!ModelState.IsValid)
        //        {
        //            // Reload dropdown data
        //            var cigs = await _context.CIGs
        //                .Where(c => c.CompanyCode == model.CompanyCode && c.Status == "Active")
        //                .OrderBy(c => c.GigName)
        //                .ToListAsync();

        //            var counties = await _context.Counties
        //                .Where(c => c.Status == "Active")
        //                .OrderBy(c => c.CountyName)
        //                .ToListAsync();

        //            ViewBag.CIGs = cigs;
        //            ViewBag.Counties = counties;

        //            return View(model);
        //        }

        //        // Calculate age from date of birth
        //        if (model.DateOfBirth.HasValue)
        //        {
        //            var today = DateTime.Today;
        //            var age = today.Year - model.DateOfBirth.Value.Year;
        //            if (model.DateOfBirth.Value.Date > today.AddYears(-age)) age--;
        //            model.Age = age;
        //        }

        //        // Register member (this returns MemberResponseDTO)
        //        var memberResponse = await _memberService.RegisterMemberAsync(model);

        //        if (memberResponse == null || string.IsNullOrEmpty(memberResponse.MemberNo))
        //        {
        //            throw new Exception("Member registration failed - no member number returned");
        //        }

        //        // Get the FULL member object from database for audit
        //        var fullMember = await _context.Members
        //            .FirstOrDefaultAsync(m => m.MemberNo == memberResponse.MemberNo && m.CompanyCode == model.CompanyCode);

        //        if (fullMember != null)
        //        {
        //            // Prepare audit extra data
        //            var auditExtraData = new
        //            {
        //                amount = 0m,
        //                memberName = $"{fullMember.Surname} {fullMember.OtherNames}",
        //                memberNumber = fullMember.MemberNo,
        //                idNumber = fullMember.Idno,
        //                phoneNumber = fullMember.PhoneNo,
        //                email = fullMember.Email ?? "",
        //                dateOfBirth = fullMember.Dob?.ToString("yyyy-MM-dd"),
        //                registrationDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        //            };

        //            // Save audit log - ALL details (IP, HostName, Location) are auto-detected by the service!
        //            await _auditService.SaveLogAsync(
        //                actionType: AuditActionType.Insert,
        //                newModel: fullMember,
        //                tableName: "Members",
        //                recordId: fullMember.MemberNo,
        //                userId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "SYSTEM",
        //                userName: User.Identity?.Name ?? "SYSTEM",
        //                companyCode: model.CompanyCode,
        //                module: "MemberManagement",
        //                extraData: JsonSerializer.Serialize(auditExtraData)
        //            );

        //            _logger.LogInformation($"Member registered successfully: {fullMember.MemberNo} - {fullMember.Surname} {fullMember.OtherNames}");
        //        }
        //        else
        //        {
        //            _logger.LogWarning($"Member registered but could not retrieve full details for audit. MemberNo: {memberResponse.MemberNo}");

        //            // Fallback audit without full member object
        //            await _auditService.SaveLogAsync(
        //                actionType: AuditActionType.Insert,
        //                tableName: "Members",
        //                recordId: memberResponse.MemberNo,
        //                userId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "SYSTEM",
        //                userName: User.Identity?.Name ?? "SYSTEM",
        //                companyCode: model.CompanyCode,
        //                module: "MemberManagement",
        //                extraData: JsonSerializer.Serialize(new
        //                {
        //                    amount = 0m,
        //                    memberNumber = memberResponse.MemberNo,
        //                    message = "Member registered but details not available for audit"
        //                })
        //            );
        //        }

        //        // Return success response
        //        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        //        {
        //            return Json(new { success = true, data = memberResponse });
        //        }

        //        TempData["SuccessMessage"] = $"Member {fullMember?.Surname} {fullMember?.OtherNames} (ID: {fullMember?.MemberNo}) registered successfully!";
        //        return RedirectToAction("Details", new { memberNo = memberResponse.MemberNo });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error registering member");
        //        ModelState.AddModelError("", $"Error registering member: {ex.Message}");

        //        // Reload dropdown data
        //        try
        //        {
        //            var cigs = await _context.CIGs
        //                .Where(c => c.CompanyCode == model.CompanyCode && c.Status == "Active")
        //                .OrderBy(c => c.GigName)
        //                .ToListAsync();

        //            var counties = await _context.Counties
        //                .Where(c => c.Status == "Active")
        //                .OrderBy(c => c.CountyName)
        //                .ToListAsync();

        //            ViewBag.CIGs = cigs;
        //            ViewBag.Counties = counties;
        //        }
        //        catch (Exception reloadEx)
        //        {
        //            _logger.LogError(reloadEx, "Error reloading dropdown data");
        //        }

        //        return View(model);
        //    }
        //}


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(MemberRegistrationDTO model)
        {
            try
            {
                // DO NOT regenerate member number - keep the one from the view
                // Remove this line: model.MemberNo = await GeneratePreviewMemberNumberAsync(model.CompanyCode);

                model.CompanyCode = _companyContextService.GetCurrentCompanyCode();
                model.CreatedBy = User.Identity?.Name ?? "SYSTEM";
                model.RegistrationDate = DateTime.Now;

                // Clear ModelState to re-validate with our custom rules
                ModelState.Clear();

                // Validate required fields
                if (string.IsNullOrEmpty(model.Surname))
                {
                    ModelState.AddModelError("Surname", "Surname is required");
                }

                if (string.IsNullOrEmpty(model.OtherNames))
                {
                    ModelState.AddModelError("OtherNames", "Other Names are required");
                }

                // Check for duplicate ID Number
                if (!string.IsNullOrEmpty(model.IdNo))
                {
                    var existingIdNo = await _context.Members
                        .AnyAsync(m => m.Idno == model.IdNo && m.CompanyCode == model.CompanyCode);

                    if (existingIdNo)
                    {
                        ModelState.AddModelError("IdNo", $"ID Number '{model.IdNo}' is already registered to another member");
                    }
                }
                else
                {
                    ModelState.AddModelError("IdNo", "ID Number is required");
                }

                // Check for duplicate Phone Number
                if (!string.IsNullOrEmpty(model.PhoneNo))
                {
                    var existingPhone = await _context.Members
                        .AnyAsync(m => m.PhoneNo == model.PhoneNo && m.CompanyCode == model.CompanyCode);

                    if (existingPhone)
                    {
                        ModelState.AddModelError("PhoneNo", $"Phone Number '{model.PhoneNo}' is already registered to another member");
                    }
                }
                else
                {
                    ModelState.AddModelError("PhoneNo", "Phone Number is required");
                }

                // Check for duplicate Email (if provided)
                if (!string.IsNullOrEmpty(model.Email))
                {
                    var existingEmail = await _context.Members
                        .AnyAsync(m => m.Email == model.Email && m.CompanyCode == model.CompanyCode);

                    if (existingEmail)
                    {
                        ModelState.AddModelError("Email", $"Email '{model.Email}' is already registered to another member");
                    }
                }

                // Check for duplicate Member Number - IMPORTANT: Validate the one from the view
                if (!string.IsNullOrEmpty(model.MemberNo))
                {
                    var existingMemberNo = await _context.Members
                        .AnyAsync(m => m.MemberNo == model.MemberNo && m.CompanyCode == model.CompanyCode);

                    if (existingMemberNo)
                    {
                        ModelState.AddModelError("MemberNo", $"Member Number '{model.MemberNo}' already exists. Please use a different number or let the system generate one.");
                    }
                }
                else
                {
                    ModelState.AddModelError("MemberNo", "Member Number is required");
                }

                // If any validation errors, return to form with errors
                if (!ModelState.IsValid)
                {
                    // Reload dropdown data
                    var cigs = await _context.CIGs
                        .Where(c => c.CompanyCode == model.CompanyCode && c.Status == "Active")
                        .OrderBy(c => c.GigName)
                        .ToListAsync();

                    var counties = await _context.Counties
                        .Where(c => c.Status == "Active")
                        .OrderBy(c => c.CountyName)
                        .ToListAsync();

                    ViewBag.CIGs = cigs;
                    ViewBag.Counties = counties;

                    return View(model);
                }

                // Calculate age from date of birth
                if (model.DateOfBirth.HasValue)
                {
                    var today = DateTime.Today;
                    var age = today.Year - model.DateOfBirth.Value.Year;
                    if (model.DateOfBirth.Value.Date > today.AddYears(-age)) age--;
                    model.Age = age;
                }

                // Register member - the service will use the MemberNo from the model
                var memberResponse = await _memberService.RegisterMemberAsync(model);

                if (memberResponse == null || string.IsNullOrEmpty(memberResponse.MemberNo))
                {
                    throw new Exception("Member registration failed - no member number returned");
                }

                // Verify the member number used is the one from the view
                if (memberResponse.MemberNo != model.MemberNo)
                {
                    _logger.LogWarning($"Member number changed from view ({model.MemberNo}) to saved ({memberResponse.MemberNo})");
                }

                // Rest of your success handling remains the same...
                var fullMember = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberResponse.MemberNo && m.CompanyCode == model.CompanyCode);

                if (fullMember != null)
                {
                    var auditExtraData = new
                    {
                        amount = 0m,
                        memberName = $"{fullMember.Surname} {fullMember.OtherNames}",
                        memberNumber = fullMember.MemberNo,
                        idNumber = fullMember.Idno,
                        phoneNumber = fullMember.PhoneNo,
                        email = fullMember.Email ?? "",
                        dateOfBirth = fullMember.Dob?.ToString("yyyy-MM-dd"),
                        registrationDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    await _auditService.SaveLogAsync(
                        actionType: AuditActionType.Insert,
                        newModel: fullMember,
                        tableName: "Members",
                        recordId: fullMember.MemberNo,
                        userId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "SYSTEM",
                        userName: User.Identity?.Name ?? "SYSTEM",
                        companyCode: model.CompanyCode,
                        module: "MemberManagement",
                        extraData: JsonSerializer.Serialize(auditExtraData)
                    );

                    _logger.LogInformation($"Member registered successfully with MemberNo: {fullMember.MemberNo} (from view: {model.MemberNo})");
                }

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, data = memberResponse });
                }

                TempData["SuccessMessage"] = $"Member {fullMember?.Surname} {fullMember?.OtherNames} (ID: {memberResponse.MemberNo}) registered successfully!";
                return RedirectToAction("Details", new { memberNo = memberResponse.MemberNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering member");
                ModelState.AddModelError("", $"Error registering member: {ex.Message}");

                try
                {
                    var cigs = await _context.CIGs
                        .Where(c => c.CompanyCode == model.CompanyCode && c.Status == "Active")
                        .OrderBy(c => c.GigName)
                        .ToListAsync();

                    var counties = await _context.Counties
                        .Where(c => c.Status == "Active")
                        .OrderBy(c => c.CountyName)
                        .ToListAsync();

                    ViewBag.CIGs = cigs;
                    ViewBag.Counties = counties;
                }
                catch (Exception reloadEx)
                {
                    _logger.LogError(reloadEx, "Error reloading dropdown data");
                }

                return View(model);
            }
        }

        private async Task<string> GeneratePreviewMemberNumberAsync(string companyCode)
        {
            string monthInitial = DateTime.Now.ToString("MMM").Substring(0, 1).ToUpper();

            // Generate 11 unique digits using timestamp + sequence
            string datePart = DateTime.Now.ToString("yyMMdd"); // 6 digits: 211223 (year,month,day)
            string timePart = DateTime.Now.ToString("HHmmss"); // 6 digits: 145530
            string combined = $"{datePart}{timePart}"; // 12 digits
            string uniqueDigits = combined.Length > 11 ? combined.Substring(0, 11) : combined.PadLeft(11, '0');

            // Try to get the last member number for today to maintain sequence
            var lastMember = await _context.Members
                .Where(m => m.CompanyCode == companyCode && m.MemberNo.StartsWith(monthInitial))
                .OrderByDescending(m => m.MemberNo)
                .FirstOrDefaultAsync();

            if (lastMember != null && lastMember.MemberNo.Length >= 12)
            {
                // Extract the numeric part (last 11 digits)
                string lastNumericPart = lastMember.MemberNo.Substring(1, 11);
                if (long.TryParse(lastNumericPart, out long lastNumber))
                {
                    // Increment by 1
                    long newNumber = lastNumber + 1;
                    uniqueDigits = newNumber.ToString().PadLeft(11, '0');

                    // Ensure it doesn't exceed 11 digits
                    if (uniqueDigits.Length > 11)
                    {
                        uniqueDigits = uniqueDigits.Substring(0, 11);
                    }
                }
            }

            string memberNo = $"{monthInitial}{uniqueDigits}";

            // Ensure exact length
            if (memberNo.Length > 12)
            {
                memberNo = memberNo.Substring(0, 12);
            }
            else if (memberNo.Length < 12)
            {
                Random rand = new Random();
                memberNo = memberNo.PadRight(12, (char)('0' + rand.Next(0, 9)));
            }

            // Check if this preview number already exists (unlikely but possible)
            var existing = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (existing != null)
            {
                // If exists, generate a random one for preview using long for 11 digits
                Random random = new Random();
                // Generate 11-digit number (10,000,000,000 to 99,999,999,999)
                long randomDigitsLong = (long)(random.NextDouble() * 90000000000) + 10000000000;
                string randomDigits = randomDigitsLong.ToString(); // 11 digits
                memberNo = $"{monthInitial}{randomDigits}";
            }

            _logger.LogInformation($"Generated preview member number: {memberNo}");
            return memberNo;
        }


        public async Task<IActionResult> Edit(string memberNo)
        {
            if (string.IsNullOrEmpty(memberNo))
            {
                return NotFound();
            }

            try
            {
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                if (member == null)
                {
                    return NotFound();
                }

                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

                // Get CIGs for dropdown
                var cigs = await _context.CIGs
                    .Where(c => c.CompanyCode == currentCompanyCode && c.Status == "Active")
                    .OrderBy(c => c.GigName)
                    .ToListAsync();

                // Get Counties for dropdown
                var counties = await _context.Counties
                    .Where(c => c.Status == "Active")
                    .OrderBy(c => c.CountyName)
                    .ToListAsync();

                ViewBag.CIGs = cigs;
                ViewBag.Counties = counties;
                ViewBag.MemberNo = member.MemberNo;
                ViewBag.FullName = $"{member.Surname} {member.OtherNames}".Trim();

                var updateDto = new MemberUpdateDTO
                {
                    // Editable fields
                    IdNo = member.Idno,  // Now editable
                    RegistrationDate = member.ApplicDate ?? DateTime.Now,  // Now editable

                    // Other fields
                    Surname = member.Surname,
                    OtherNames = member.OtherNames,
                    PhoneNo = member.PhoneNo,
                    LandLine = member.HomeTelNo,
                    Email = member.Email,
                    Gender = member.Sex,
                    DateOfBirth = member.Dob,
                    Age = member.Age?.ToString(),
                    Station = member.Station,
                    Department = member.Dept,
                    PresentAddress = member.PresentAddr,
                    Cigcode = member.Cigcode,
                    MembershipType = member.MembershipType,
                    RegistrationType = member.MemberDescription,
                    MaritalStatus = member.Mstatus == true ? "Married" : member.Mstatus == false ? "Single" : null,
                    Status = member.Status switch
                    {
                        1 => "Active",
                        2 => "Withdrawn",
                        3 => "Deceased",
                        4 => "Dormant",
                        5 => "Suspended",
                        _ => "Active"
                    },
                    CompanyCode = member.CompanyCode,
                    CreatedBy = member.AuditId
                };

                return View(updateDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading edit form for member {memberNo}");
                TempData["ErrorMessage"] = "Error loading member data";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string memberNo, MemberUpdateDTO model)
        {
            try
            {
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

                // Get the existing member
                var existingMember = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == currentCompanyCode);

                if (existingMember == null)
                {
                    TempData["ErrorMessage"] = "Member not found";
                    return RedirectToAction("Index");
                }

                // Clear ModelState for custom validation
                ModelState.Clear();

                // Validate required fields
                if (string.IsNullOrEmpty(model.Surname))
                {
                    ModelState.AddModelError("Surname", "Surname is required");
                }

                if (string.IsNullOrEmpty(model.OtherNames))
                {
                    ModelState.AddModelError("OtherNames", "Other Names are required");
                }

                // Check for duplicate ID Number (excluding current member)
                if (!string.IsNullOrEmpty(model.IdNo))
                {
                    var existingIdNo = await _context.Members
                        .AnyAsync(m => m.Idno == model.IdNo &&
                                      m.CompanyCode == currentCompanyCode &&
                                      m.MemberNo != memberNo);

                    if (existingIdNo)
                    {
                        ModelState.AddModelError("IdNo", $"ID Number '{model.IdNo}' is already registered to another member");
                    }
                }

                // Check for duplicate Phone Number (excluding current member)
                if (!string.IsNullOrEmpty(model.PhoneNo))
                {
                    var existingPhone = await _context.Members
                        .AnyAsync(m => m.PhoneNo == model.PhoneNo &&
                                      m.CompanyCode == currentCompanyCode &&
                                      m.MemberNo != memberNo);

                    if (existingPhone)
                    {
                        ModelState.AddModelError("PhoneNo", $"Phone Number '{model.PhoneNo}' is already registered to another member");
                    }
                }

                // Check for duplicate Email (if provided, excluding current member)
                if (!string.IsNullOrEmpty(model.Email))
                {
                    var existingEmail = await _context.Members
                        .AnyAsync(m => m.Email == model.Email &&
                                      m.CompanyCode == currentCompanyCode &&
                                      m.MemberNo != memberNo);

                    if (existingEmail)
                    {
                        ModelState.AddModelError("Email", $"Email '{model.Email}' is already registered to another member");
                    }
                }

                // If any validation errors, return to form with errors
                if (!ModelState.IsValid)
                {
                    // Reload dropdown data
                    var cigs = await _context.CIGs
                        .Where(c => c.CompanyCode == currentCompanyCode && c.Status == "Active")
                        .OrderBy(c => c.GigName)
                        .ToListAsync();

                    var counties = await _context.Counties
                        .Where(c => c.Status == "Active")
                        .OrderBy(c => c.CountyName)
                        .ToListAsync();

                    ViewBag.CIGs = cigs;
                    ViewBag.Counties = counties;
                    ViewBag.MemberNo = memberNo;
                    ViewBag.FullName = $"{existingMember.Surname} {existingMember.OtherNames}";

                    return View(model);
                }

                // Update member
                await _memberService.UpdateMemberAsync(memberNo, model);

                TempData["SuccessMessage"] = $"Member updated successfully!";
                return RedirectToAction("Index");
                //return RedirectToAction("Details", new { memberNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating member");
                TempData["ErrorMessage"] = $"Error updating member: {ex.Message}";

                // Reload dropdown data
                var cigs = await _context.CIGs
                    .Where(c => c.CompanyCode == _companyContextService.GetCurrentCompanyCode() && c.Status == "Active")
                    .OrderBy(c => c.GigName)
                    .ToListAsync();

                var counties = await _context.Counties
                    .Where(c => c.Status == "Active")
                    .OrderBy(c => c.CountyName)
                    .ToListAsync();

                ViewBag.CIGs = cigs;
                ViewBag.Counties = counties;
                ViewBag.MemberNo = memberNo;

                return View(model);
            }
        }
        public async Task<IActionResult> Details(string memberNo)
        {
            try
            {
                var member = await _memberService.GetMemberByMemberNoAsync(memberNo);
                if (member == null)
                {
                    return NotFound();
                }
                return View(member);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading member details");
                return View("Error");
            }
        }

        // GET: /MemberMvc/Search
        public IActionResult Search()
        {
            return View();
        }

        [HttpGet("SearchMembers")]
        public async Task<IActionResult> SearchMembersAjax(string searchTerm)
        {
            try
            {
                var members = await _memberService.SearchMembersAsync(searchTerm);
                return Ok(new { success = true, data = members });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching members");
                return Ok(new { success = false, message = "Error searching members" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchMember(string memberNo, string idNo, string fullName)
        {
            try
            {
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                var query = _context.Members.AsQueryable();

                query = query.Where(m => m.CompanyCode == currentCompanyCode);

                // Status check - assuming Active status is 1 or 0
                // Check your Member model to see what values represent "Active"
                // Common: 1 = Active, 0 = Inactive, or use m.Withdrawn == false
                query = query.Where(m => m.Withdrawn != true && m.Archived != true);

                if (!string.IsNullOrEmpty(memberNo))
                {
                    query = query.Where(m => m.MemberNo.Contains(memberNo));
                }
                else if (!string.IsNullOrEmpty(idNo))
                {
                    query = query.Where(m => m.Idno.Contains(idNo));
                }
                else if (!string.IsNullOrEmpty(fullName))
                {
                    query = query.Where(m => (m.Surname + " " + m.OtherNames).Contains(fullName));
                }
                else
                {
                    return Json(new { success = false, message = "Please provide a search value" });
                }

                var member = await query.Select(m => new
                {
                    m.MemberNo,
                    FullName = m.Surname + " " + m.OtherNames,
                    m.Idno,
                    m.PhoneNo,
                    m.Email,
                    Status = m.Withdrawn == true ? "Withdrawn" : (m.Archived == true ? "Archived" : "Active")
                }).FirstOrDefaultAsync();

                if (member == null)
                {
                    return Json(new { success = false, message = "Member not found" });
                }

                return Json(new { success = true, member });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching member");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}