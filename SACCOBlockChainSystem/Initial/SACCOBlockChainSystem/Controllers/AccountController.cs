using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SACCOBlockChainSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;
        private readonly IUserService _userService; 

        public AccountController(ApplicationDbContext context, IUserService userService, ILogger<AccountController> logger)
        {
            _context = context;
            _userService = userService; 
            _logger = logger;
        }


        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Blockchain");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVm model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Hash the password for comparison
                var hashedPassword = HashPassword(model.Password);

                // Find user by Username and password
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserName == model.Username && u.Password == hashedPassword);

                if (user == null)
                {
                    // Update failed attempts for the username
                    var failedUser = await _context.UserAccounts1
                        .FirstOrDefaultAsync(u => u.UserName == model.Username);

                    if (failedUser != null)
                    {
                        failedUser.FailedAttempts = (failedUser.FailedAttempts ?? 0) + 1;

                        // Lock account after 5 failed attempts
                        if (failedUser.FailedAttempts >= 5)
                        {
                            failedUser.IsLocked = true;
                            _logger.LogWarning($"Account locked for username: {model.Username}");
                        }

                        await _context.SaveChangesAsync();
                    }

                    ModelState.AddModelError(string.Empty, "Invalid username or password.");
                    return View(model);
                }

                // Check if account is locked
                if (user.IsLocked == true)
                {
                    ModelState.AddModelError(string.Empty, "Account is locked. Please contact administrator.");
                    return View(model);
                }

                // Check if account is active
                if (user.Status?.ToLower() != "active" && user.Userstatus?.ToLower() != "active")
                {
                    ModelState.AddModelError(string.Empty, "Account is not active. Please contact administrator.");
                    return View(model);
                }

                // Check if user has a company code
                if (string.IsNullOrEmpty(user.CompanyCode))
                {
                    ModelState.AddModelError(string.Empty, "User account is not associated with any company. Please contact administrator.");
                    return View(model);
                }

                // Get company name for the user's company code
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == user.CompanyCode);

                var companyName = company?.CompanyName ?? "Unknown Company";

                // Reset failed attempts on successful login
                user.FailedAttempts = 0;
                await _context.SaveChangesAsync();

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim("FullName", user.UserName ?? string.Empty),
                    new Claim("Email", user.Email ?? string.Empty),
                    new Claim("UserId", user.UserId.ToString()),
                    new Claim("CompanyCode", user.CompanyCode ?? "000"),
                    new Claim("CompanyName", companyName),
                    new Claim("UserLoginId", user.UserLoginId ?? string.Empty)
                };

                // Add UserGroup as a separate claim - THIS IS IMPORTANT
                if (!string.IsNullOrEmpty(user.UserGroup))
                {
                    claims.Add(new Claim("UserGroup", user.UserGroup));
                    claims.Add(new Claim(ClaimTypes.Role, user.UserGroup));
                }

                // Add additional user info claims
                if (!string.IsNullOrEmpty(user.Department))
                {
                    claims.Add(new Claim("Department", user.Department));
                }

                if (!string.IsNullOrEmpty(user.MemberNo))
                {
                    claims.Add(new Claim("MemberNo", user.MemberNo));
                }

                //CompanyCode claim
                if (!string.IsNullOrEmpty(user.CompanyCode))
                {
                    claims.Add(new Claim("CompanyCode", user.CompanyCode));
                }

                // Branch code Claims
                if (!string.IsNullOrEmpty(user.Branchcode))
                {
                    claims.Add(new Claim("BranchCode", user.Branchcode));
                }

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(2),
                    RedirectUri = returnUrl ?? "/Home/Index"
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                _logger.LogInformation($"User {user.UserName} (Company: {companyName} - {user.CompanyCode}) logged in successfully.");

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                ModelState.AddModelError(string.Empty, "An error occurred during login. Please try again.");
                return View(model);
            }
        }

        // GET: /Account/Signup
        [HttpGet]
        public async Task<IActionResult> Signup()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Blockchain");
            }

            await LoadCompanies();

            var model = new SignupVm
            {
                AvailableUserGroups = GetUserGroups()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Signup(SignupVm model)
        {
            // Debug: Log what's being received
            _logger.LogInformation("=== SIGNUP ATTEMPT ===");
            _logger.LogInformation($"Username: {model.UserName}");
            _logger.LogInformation($"Email: {model.Email}");
            _logger.LogInformation($"CompanyCode: {model.CompanyCode}");
            _logger.LogInformation($"ModelState.IsValid: {ModelState.IsValid}");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model validation failed!");
                foreach (var key in ModelState.Keys)
                {
                    var errors = ModelState[key].Errors;
                    if (errors.Any())
                    {
                        _logger.LogWarning($"  {key}: {string.Join(", ", errors.Select(e => e.ErrorMessage))}");
                    }
                }

                await LoadCompanies();
                return View(model);
            }

            try
            {
                // Validate company exists
                var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.CompanyCode == model.CompanyCode && c.Project == true);

                if (company == null)
                {
                    ModelState.AddModelError("CompanyCode", "Selected company is not available.");
                    await LoadCompanies();
                    return View(model);
                }

                // Check for duplicate username
                var existingUser = await _context.UserAccounts1
                .FirstOrDefaultAsync(u => u.UserName == model.UserName);

                if (existingUser != null)
                {
                    ModelState.AddModelError("UserName", "Username already exists.");
                    await LoadCompanies();
                    return View(model);
                }

                // Check for duplicate email
                if (!string.IsNullOrEmpty(model.Email))
                {
                    var existingEmail = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.Email == model.Email);

                    if (existingEmail != null)
                    {
                        ModelState.AddModelError("Email", "Email already registered.");
                        await LoadCompanies();
                        return View(model);
                    }
                }


                // Create the new user
                var user = new UserAccounts1
                {
                    UserName = model.UserName.Trim(),
                    UserLoginId = GenerateUserLoginId(model.UserName),
                    Password = HashPassword(model.Password),
                    Email = model.Email?.Trim(),
                    Phone = model.Phone?.Trim(),
                    PhoneNo = model.Phone?.Trim(),
                    //MemberNo = model.MemberNo?.Trim(),
                    Department = model.Department?.Trim(),
                    SubCounty = model.SubCounty?.Trim(),
                    Ward = model.Ward?.Trim(),
                    DateCreated = DateTime.Now,
                    Status = "Pending",
                    Userstatus = "Pending",
                    ApprovalStatus = "Pending",
                    FailedAttempts = 0,
                    IsLocked = false,
                    PasswordStatus = "Active",
                    PassExpire = "No",
                    UserGroup = string.IsNullOrEmpty(model.UserGroup) ? "Member" : model.UserGroup,
                    Cigcode = company.Cigcode,
                    CompanyCode = model.CompanyCode,
                    Branchcode = company.Cigcode ?? model.CompanyCode,
                    Superuser = 0,
                    Authorize = false,
                    Count = 0
                };

                // Save to database
                _context.UserAccounts1.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"SUCCESS: User '{user.UserName}' created with ID: {user.UserId}");

                TempData["SuccessMessage"] = $"Registration successful! Your account is pending approval for {company.CompanyName}. You will be notified once approved.";
                return RedirectToAction("Login");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error during signup");
                ModelState.AddModelError(string.Empty, "A database error occurred. Please try again.");

                if (dbEx.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {dbEx.InnerException.Message}");
                }

                await LoadCompanies();
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during signup");
                ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again.");
                await LoadCompanies();
                return View(model);
            }
        }


        //  distinct roles list (no duplicates within this list)
        private List<string> GetUserGroups()
        {
            return new List<string>
            {
                "Member",
                "Teller",
                "Administrator",         
                "System Admin",          
                "LoanOfficer",
                "Auditor",
                "Book Keeper",
                "Finance Officer",
                "BoardMember",
                "Staff"
            };
        }

        // Helper method to check if a role is a "System User" (privileged)
        private bool IsSystemUserRole(string userGroup)
        {
            var systemRoles = new List<string>
            {
                "Administrator",
                "System Admin",
                "Super Admin"  // Include if applicable
            };
            return systemRoles.Contains(userGroup);
        }

        // Helper method to count system users in a company
        private async Task<int> CountSystemUsersByCompanyAsync(string companyCode)
        {
            var users = await _context.UserAccounts1
                .Where(u => u.CompanyCode == companyCode)
                .ToListAsync();

            return users.Count(u => IsSystemUserRole(u.UserGroup));
        }

        // Helper method to check if a role is already taken in a company
        private async Task<bool> IsRoleAlreadyAssignedInCompanyAsync(string companyCode, string userGroup)
        {
            return await _context.UserAccounts1
                .AnyAsync(u => u.CompanyCode == companyCode && u.UserGroup == userGroup);
        }

        private async Task LoadCompanies()
        {
            try
            {
                var companies = await _context.Companies
                .Where(c => c.Project == true)
                .OrderBy(c => c.CompanyName)
                .Select(c => new
                {
                    c.CompanyCode,
                    DisplayText = $"{c.CompanyCode} - {c.CompanyName}"
                })
                .ToListAsync();

                ViewBag.Companies = companies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load companies");
                ViewBag.Companies = new List<dynamic>();
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = await _context.UserAccounts1
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound();
            }

            // Get company name
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.CompanyCode == user.CompanyCode);

            var profile = new ProfileVm
            {
                UserId = user.UserId,
                UserName = user.UserName,
                UserLoginId = user.UserLoginId,
                Email = user.Email,
                Phone = user.Phone,
                MemberNo = user.MemberNo,
                Department = user.Department,
                SubCounty = user.SubCounty,
                Ward = user.Ward,
                UserGroup = user.UserGroup,
                Status = user.Status,
                DateCreated = user.DateCreated
            };

            ViewBag.CompanyName = company?.CompanyName ?? "Unknown Company";
            ViewBag.CompanyCode = user.CompanyCode;

            return View(profile);
        }


        // GET: /Account/Logout
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            var userName = User.Identity?.Name;
            var companyName = User.FindFirstValue("CompanyName");

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation($"User {userName} (Company: {companyName}) logged out.");
            return RedirectToAction("Login", "Account");
        }

        // POST: /Account/Logout - For form submissions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogoutPost()
        {
            var userName = User.Identity?.Name;
            var companyName = User.FindFirstValue("CompanyName");

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation($"User {userName} (Company: {companyName}) logged out.");
            return RedirectToAction("Login", "Account");
        }


        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // GET: /Account/CompanySwitch
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> CompanySwitch(string searchTerm = null)
        {
            try
            {
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserGroup = User.FindFirstValue("UserGroup");

                // Allow Super Admin, Admin, and System Administrator
                bool canSwitchCompanies = currentUserRole == "Super Admin" ||
                                          currentUserRole == "Admin" ||
                                          currentUserRole == "System Administrator" ||
                                          currentUserGroup == "Super Admin" ||
                                          currentUserGroup == "Admin" ||
                                          currentUserGroup == "System Administrator";

                if (!canSwitchCompanies)
                {
                    TempData["ErrorMessage"] = "You do not have permission to switch companies. Only Super Admin, Admin, and System Administrator can switch companies.";
                    return RedirectToAction("Index", "Home");
                }

                ViewBag.SearchTerm = searchTerm;

                var companiesQuery = _context.Companies.Where(c => c.Project == true);

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    companiesQuery = companiesQuery.Where(c =>
                        c.CompanyCode.Contains(searchTerm) ||
                        c.CompanyName.Contains(searchTerm));
                }

                var companies = await companiesQuery
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new
                    {
                        c.CompanyCode,
                        c.CompanyName,
                        DisplayText = $"{c.CompanyCode} - {c.CompanyName}"
                    })
                    .ToListAsync();

                ViewBag.Companies = companies;
                ViewBag.TotalCompanies = companies.Count;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading company switch page");
                TempData["ErrorMessage"] = "Error loading companies list";
                return RedirectToAction("Index", "Home");
            }
        }

        // ✅ FIXED: Renamed POST method to "SwitchCompany" to avoid signature conflict
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> SwitchCompany(string companyCode)
        {
            try
            {
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserGroup = User.FindFirstValue("UserGroup");

                // Allow Super Admin, Admin, and System Administrator
                bool canSwitchCompanies = currentUserRole == "Super Admin" ||
                                          currentUserRole == "Admin" ||
                                          currentUserRole == "System Administrator" ||
                                          currentUserGroup == "Super Admin" ||
                                          currentUserGroup == "Admin" ||
                                          currentUserGroup == "System Administrator";

                if (!canSwitchCompanies)
                {
                    TempData["ErrorMessage"] = "You do not have permission to switch companies.";
                    return RedirectToAction("Index", "Home");
                }

                if (string.IsNullOrEmpty(companyCode))
                {
                    TempData["ErrorMessage"] = "Please select a company to switch to.";
                    return RedirectToAction("CompanySwitch");
                }

                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToAction("CompanySwitch");
                }

                // Validate new company
                var newCompany = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == companyCode && c.Project == true);

                if (newCompany == null)
                {
                    TempData["ErrorMessage"] = $"Company with code {companyCode} not found or inactive.";
                    return RedirectToAction("CompanySwitch");
                }

                // Don't switch to the same company
                if (user.CompanyCode == companyCode)
                {
                    TempData["ErrorMessage"] = $"You are already in company {newCompany.CompanyName}.";
                    return RedirectToAction("CompanySwitch");
                }

                // Update user's company code
                user.CompanyCode = companyCode;
                await _context.SaveChangesAsync();

                // Get updated user with new claims
                var updatedUser = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                // Create new claims
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, updatedUser.UserId.ToString()),
            new Claim(ClaimTypes.Name, updatedUser.UserName),
            new Claim("FullName", updatedUser.UserName ?? string.Empty),
            new Claim("Email", updatedUser.Email ?? string.Empty),
            new Claim("UserId", updatedUser.UserId.ToString()),
            new Claim("CompanyCode", updatedUser.CompanyCode ?? "000"),
            new Claim("CompanyName", newCompany.CompanyName ?? "Unknown Company"),
            new Claim("UserLoginId", updatedUser.UserLoginId ?? string.Empty)
        };

                if (!string.IsNullOrEmpty(updatedUser.UserGroup))
                {
                    claims.Add(new Claim("UserGroup", updatedUser.UserGroup));
                    claims.Add(new Claim(ClaimTypes.Role, updatedUser.UserGroup));
                }

                if (!string.IsNullOrEmpty(updatedUser.Department))
                {
                    claims.Add(new Claim("Department", updatedUser.Department));
                }

                if (!string.IsNullOrEmpty(updatedUser.MemberNo))
                {
                    claims.Add(new Claim("MemberNo", updatedUser.MemberNo));
                }

                if (!string.IsNullOrEmpty(updatedUser.Branchcode))
                {
                    claims.Add(new Claim("BranchCode", updatedUser.Branchcode));
                }

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity));

                _logger.LogInformation($"User {updatedUser.UserName} switched from {user.CompanyCode} to company: {newCompany.CompanyName} ({companyCode})");
                TempData["SuccessMessage"] = $"Successfully switched to company: {newCompany.CompanyName}";

                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error switching company to {companyCode}");
                TempData["ErrorMessage"] = $"Error switching company: {ex.Message}";
                return RedirectToAction("CompanySwitch");
            }
        }

        private string GenerateUserLoginId(string userName)
        {
            var prefix = userName.Length >= 3
            ? userName.Substring(0, 3).ToUpper()
            : userName.ToUpper();

            var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
            return $"{prefix}{timestamp}";
        }
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSubCountiesByCompany(string companyCode)
        {
            try
            {
                // Get company's CIG code or use company code to filter subcounties
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == companyCode);

                if (company == null)
                {
                    return Json(new List<object>());
                }

                // Get subcounties associated with this company's region
                // You may need to adjust this based on your actual relationship
                var subCounties = await _context.SubCounties
                    .Where(s => s.Status == "Active")
                    .OrderBy(s => s.SubCountyName)
                    .Select(s => new { s.Id, s.SubCountyName, s.SubCountyCode })
                    .ToListAsync();

                return Json(subCounties);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading subcounties");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetWardsBySubCounty(int subCountyId)
        {
            try
            {
                var wards = await _context.Wards
                    .Where(w => w.SubCountyId == subCountyId && w.Status == "Active")
                    .OrderBy(w => w.WardName)
                    .Select(w => new { w.Id, w.WardName, w.WardCode })
                    .ToListAsync();

                return Json(wards);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading wards");
                return Json(new List<object>());
            }
        }
        // GET: /Account/UserManagement
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> UserManagement(string searchTerm)
        {
            try
            {
                ViewBag.SearchTerm = searchTerm;

                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

                List<SACCOBlockChainSystem.Models.DTOs.UserListDTO> users;

                if (currentUserRole == "Super Admin")
                {
                    users = await _userService.GetAllUsersAsync(searchTerm);
                    ViewBag.Companies = await _userService.GetCompaniesForDropdownAsync();
                }
                else
                {
                    users = await _userService.GetUsersByCompanyAsync(currentUserCompanyCode, searchTerm);
                }

                // Convert DTO to ViewModel if needed, or use the same type
                var viewModel = new UserManagementViewModel
                {
                    Users = users.Select(u => new SACCOBlockChainSystem.Models.ViewModels.UserListDTO
                    {
                        UserId = u.UserId,
                        UserName = u.UserName,
                        UserLoginId = u.UserLoginId,
                        Email = u.Email,
                        Phone = u.Phone,
                        Department = u.Department,
                        UserGroup = u.UserGroup,
                        CompanyCode = u.CompanyCode,
                        CompanyName = u.CompanyName,
                        Status = u.Status,
                        IsLocked = u.IsLocked
                    }).ToList(),
                    SearchTerm = searchTerm,
                    IsEditMode = false
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user management page");
                TempData["ErrorMessage"] = "Error loading users list";
                return View(new UserManagementViewModel { Users = new List<SACCOBlockChainSystem.Models.ViewModels.UserListDTO>() });
            }
        }

        public async Task<IActionResult> Index(string searchTerm)
        {
            try
            {
                ViewBag.SearchTerm = searchTerm;

                // Get current user's company code and role
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);

                // Load users with company filtering
                List<SACCOBlockChainSystem.Models.DTOs.UserListDTO> users;

                if (currentUserRole == "Super Admin")
                {
                    // Super Admin can see all users
                    users = await _userService.GetAllUsersAsync(searchTerm);
                }
                else
                {
                    // Regular users only see users from their company
                    users = await _userService.GetUsersByCompanyAsync(currentUserCompanyCode, searchTerm);
                }

                // Load dropdown data for the form (filtered by company for non-super admins)
                ViewBag.UserGroups = await _userService.GetUserGroupsAsync();

                if (currentUserRole == "Super Admin")
                {
                    ViewBag.Companies = await _userService.GetCompaniesForDropdownAsync();
                }
                else
                {
                    // Only show current user's company for non-super admins
                    var company = await _context.Companies
                        .Where(c => c.CompanyCode == currentUserCompanyCode && c.Project == true)
                        .Select(c => new { c.CompanyCode, DisplayText = $"{c.CompanyCode} - {c.CompanyName}" })
                        .FirstOrDefaultAsync();

                    ViewBag.Companies = company != null ? new List<dynamic> { company } : new List<dynamic>();
                }

                // Load subcounties and wards for dropdowns
                ViewBag.SubCounties = await _context.SubCounties
                    .Where(s => s.Status == "Active")
                    .OrderBy(s => s.SubCountyName)
                    .Select(s => new { s.Id, s.SubCountyName })
                    .ToListAsync();

                ViewBag.Wards = await _context.Wards
                    .Where(w => w.Status == "Active")
                    .OrderBy(w => w.WardName)
                    .Select(w => new { w.Id, w.WardName, w.SubCountyId })
                    .ToListAsync();

                // Create the view model with the users list - Convert DTO to ViewModel
                var viewModel = new UserManagementViewModel
                {
                    Users = users.Select(u => new SACCOBlockChainSystem.Models.ViewModels.UserListDTO
                    {
                        UserId = u.UserId,
                        UserName = u.UserName,
                        UserLoginId = u.UserLoginId,
                        Email = u.Email,
                        Phone = u.Phone,
                        Department = u.Department,
                        UserGroup = u.UserGroup,
                        CompanyCode = u.CompanyCode,
                        CompanyName = u.CompanyName,
                        Status = u.Status,
                        IsLocked = u.IsLocked
                    }).ToList(),
                    SearchTerm = searchTerm,
                    IsEditMode = false
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading users list");
                TempData["ErrorMessage"] = "Error loading users list";

                var viewModel = new UserManagementViewModel
                {
                    Users = new List<SACCOBlockChainSystem.Models.ViewModels.UserListDTO>(),
                    SearchTerm = searchTerm,
                    IsEditMode = false
                };

                return View(viewModel);
            }
        }

        // Optional: Add a method to view company user statistics
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> CompanyUserStats(string companyCode)
        {
            if (string.IsNullOrEmpty(companyCode))
            {
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");
                companyCode = currentUserCompanyCode;
            }

            var allUsers = await _context.UserAccounts1
                .Where(u => u.CompanyCode == companyCode)
                .ToListAsync();

            var systemUsers = allUsers.Where(u => IsSystemUserRole(u.UserGroup)).ToList();
            var roleGroups = allUsers.GroupBy(u => u.UserGroup)
                .Select(g => new { Role = g.Key, Count = g.Count() })
                .ToList();

            var viewModel = new
            {
                CompanyCode = companyCode,
                TotalUsers = allUsers.Count,
                SystemUsersCount = systemUsers.Count,
                SystemUsersLimit = 5,
                SystemUsersRemaining = 5 - systemUsers.Count,
                RoleAssignments = roleGroups,
                HasDuplicateRoles = roleGroups.Any(r => r.Count > 1)
            };

            return Json(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SignupVm model)
        {
            // Debug: Log what's being received
            _logger.LogInformation("=== CREATE USER ATTEMPT ===");
            _logger.LogInformation($"Username: {model.UserName}");
            _logger.LogInformation($"Email: {model.Email}");
            _logger.LogInformation($"CompanyCode: {model.CompanyCode}");
            _logger.LogInformation($"SubCountyId: {model.SubCountyId}");
            _logger.LogInformation($"WardId: {model.WardId}");
            _logger.LogInformation($"ModelState.IsValid: {ModelState.IsValid}");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model validation failed!");
                foreach (var key in ModelState.Keys)
                {
                    var errors = ModelState[key].Errors;
                    if (errors.Any())
                    {
                        _logger.LogWarning($"  {key}: {string.Join(", ", errors.Select(e => e.ErrorMessage))}");
                    }
                }

                // Reload dropdown data
                await LoadCompanies();
                ViewBag.UserGroups = await _userService.GetUserGroupsAsync();

                TempData["ErrorMessage"] = "Please fix the validation errors.";
                return RedirectToAction("Index");
            }

            try
            {
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == model.CompanyCode && c.Project == true);

                if (company == null)
                {
                    TempData["ErrorMessage"] = "Selected company is not available.";
                    return RedirectToAction("Index");
                }

                // NEW VALIDATION 1: Check if role already exists in this company
                if (await IsRoleAlreadyAssignedInCompanyAsync(model.CompanyCode, model.UserGroup))
                {
                    TempData["ErrorMessage"] = $"The role '{model.UserGroup}' is already assigned to another user in this company. Each role must be unique per company.";
                    return RedirectToAction("Index");
                }

                // NEW VALIDATION 2: Check system user limit (max 5)
                if (IsSystemUserRole(model.UserGroup))
                {
                    var systemUserCount = await CountSystemUsersByCompanyAsync(model.CompanyCode);
                    if (systemUserCount >= 5)
                    {
                        TempData["ErrorMessage"] = $"Cannot create more than 5 system users (Administrator/System Admin) in this company. Current count: {systemUserCount}/5";
                        return RedirectToAction("Index");
                    }
                }

                // Check for duplicate username
                var existingUser = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserName == model.UserName);

                if (existingUser != null)
                {
                    TempData["ErrorMessage"] = "Username already exists.";
                    return RedirectToAction("Index");
                }

                // Check for duplicate email
                if (!string.IsNullOrEmpty(model.Email))
                {
                    var existingEmail = await _context.UserAccounts1
                        .FirstOrDefaultAsync(u => u.Email == model.Email);

                    if (existingEmail != null)
                    {
                        TempData["ErrorMessage"] = "Email already registered.";
                        return RedirectToAction("Index");
                    }
                }

                // Get SubCounty and Ward names from IDs if selected
                string subCountyName = model.SubCounty;
                string wardName = model.Ward;

                if (model.SubCountyId.HasValue && model.SubCountyId.Value > 0)
                {
                    var subCounty = await _context.SubCounties
                        .FirstOrDefaultAsync(s => s.Id == model.SubCountyId.Value);
                    if (subCounty != null)
                    {
                        subCountyName = subCounty.SubCountyName;
                    }
                }

                if (model.WardId.HasValue && model.WardId.Value > 0)
                {
                    var ward = await _context.Wards
                        .FirstOrDefaultAsync(w => w.Id == model.WardId.Value);
                    if (ward != null)
                    {
                        wardName = ward.WardName;
                    }
                }

                // Create the new user
                var user = new UserAccounts1
                {
                    UserName = model.UserName.Trim(),
                    UserLoginId = GenerateUserLoginId(model.UserName),
                    Password = HashPassword(model.Password),
                    Email = model.Email?.Trim(),
                    Phone = model.Phone?.Trim(),
                    PhoneNo = model.Phone?.Trim(),
                    Department = model.Department?.Trim(),
                    SubCounty = subCountyName,
                    Ward = wardName,
                    DateCreated = DateTime.Now,
                    Status = "Active", // Set to Active since admin creates
                    Userstatus = "Active",
                    ApprovalStatus = "Approved",
                    FailedAttempts = 0,
                    IsLocked = false,
                    PasswordStatus = "Active",
                    PassExpire = "No",
                    UserGroup = string.IsNullOrEmpty(model.UserGroup) ? "Member" : model.UserGroup,
                    Cigcode = company.Cigcode,
                    CompanyCode = model.CompanyCode,
                    Branchcode = company.Cigcode ?? model.CompanyCode,
                    Superuser = 0,
                    Authorize = false,
                    Count = 0
                };

                // Save to database
                _context.UserAccounts1.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"SUCCESS: User '{user.UserName}' created with ID: {user.UserId}");
                TempData["SuccessMessage"] = $"User '{user.UserName}' created successfully!";

                return RedirectToAction("Index");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error during user creation");
                TempData["ErrorMessage"] = "A database error occurred. Please try again.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during user creation");
                TempData["ErrorMessage"] = "An unexpected error occurred. Please try again.";
                return RedirectToAction("Index");
            }
        }

        // GET: /Account/Edit/5
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToAction("Index");
                }

                // Check permissions - only Super Admin or users from same company can edit
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

                if (currentUserRole != "Super Admin" && user.CompanyCode != currentUserCompanyCode)
                {
                    TempData["ErrorMessage"] = "You don't have permission to edit this user.";
                    return RedirectToAction("Index");
                }

                // Load dropdown data
                await LoadCompanies();
                ViewBag.UserGroups = await _userService.GetUserGroupsAsync();

                // ===== FIX: Load SubCounties for dropdown =====
                ViewBag.SubCounties = await _context.SubCounties
                    .Where(s => s.Status == "Active")
                    .OrderBy(s => s.SubCountyName)
                    .Select(s => new { s.Id, s.SubCountyName })
                    .ToListAsync();

                // Get SubCounty and Ward IDs for dropdown selection
                int? subCountyId = null;
                int? wardId = null;

                if (!string.IsNullOrEmpty(user.SubCounty))
                {
                    var subCounty = await _context.SubCounties
                        .FirstOrDefaultAsync(s => s.SubCountyName == user.SubCounty);
                    if (subCounty != null)
                    {
                        subCountyId = subCounty.Id;
                    }
                }

                if (!string.IsNullOrEmpty(user.Ward))
                {
                    var ward = await _context.Wards
                        .FirstOrDefaultAsync(w => w.WardName == user.Ward);
                    if (ward != null)
                    {
                        wardId = ward.Id;
                    }
                }

                // Create the EditUserVm
                var model = new EditUserVm
                {
                    UserId = user.UserId,
                    UserName = user.UserName,
                    UserLoginId = user.UserLoginId,
                    Email = user.Email,
                    Phone = user.Phone,
                    Department = user.Department,
                    SubCounty = user.SubCounty,
                    Ward = user.Ward,
                    SubCountyId = subCountyId,
                    WardId = wardId,
                    UserGroup = user.UserGroup,
                    CompanyCode = user.CompanyCode,
                    Status = user.Status,
                    IsLocked = user.IsLocked ?? false,
                    DateCreated = user.DateCreated,
                    AvailableStatuses = new List<string> { "Active", "Inactive", "Pending", "Locked" }
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in Edit GET for userId {id}");
                TempData["ErrorMessage"] = "An error occurred while loading the user data.";
                return RedirectToAction("Index");
            }
        }

        // POST: /Account/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Edit(int id, EditUserVm model)
        {
            if (id != model.UserId)
            {
                TempData["ErrorMessage"] = "User ID mismatch.";
                return RedirectToAction("Index");
            }

            if (!ModelState.IsValid)
            {
                // Reload dropdown data
                await LoadCompanies();
                ViewBag.UserGroups = await _userService.GetUserGroupsAsync();

                // ===== FIX: Reload SubCounties when validation fails =====
                ViewBag.SubCounties = await _context.SubCounties
                    .Where(s => s.Status == "Active")
                    .OrderBy(s => s.SubCountyName)
                    .Select(s => new { s.Id, s.SubCountyName })
                    .ToListAsync();

                return View(model);
            }

            try
            {
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToAction("Index");
                }

                // Check if role is being changed
                bool isRoleChanging = user.UserGroup != model.UserGroup;

                if (isRoleChanging)
                {
                    // VALIDATION 1: New role must not already exist in same company (excluding current user)
                    var roleExists = await _context.UserAccounts1
                        .AnyAsync(u => u.CompanyCode == user.CompanyCode &&
                                      u.UserGroup == model.UserGroup &&
                                      u.UserId != id);

                    if (roleExists)
                    {
                        TempData["ErrorMessage"] = $"The role '{model.UserGroup}' is already assigned to another user in this company. Each role must be unique per company.";
                        return RedirectToAction("Index");
                    }

                    // VALIDATION 2: Check system user limit if new role is system role
                    if (IsSystemUserRole(model.UserGroup) && !IsSystemUserRole(user.UserGroup))
                    {
                        var systemUserCount = await CountSystemUsersByCompanyAsync(user.CompanyCode);
                        if (systemUserCount >= 5)
                        {
                            TempData["ErrorMessage"] = $"Cannot add more system users. This company already has {systemUserCount}/5 system users.";
                            return RedirectToAction("Index");
                        }
                    }
                }

                // Check permissions
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

                if (currentUserRole != "Super Admin" && user.CompanyCode != currentUserCompanyCode)
                {
                    TempData["ErrorMessage"] = "You don't have permission to edit this user.";
                    return RedirectToAction("Index");
                }

                // Get SubCounty and Ward names from IDs if selected
                string subCountyName = model.SubCounty;
                string wardName = model.Ward;

                if (model.SubCountyId.HasValue && model.SubCountyId.Value > 0)
                {
                    var subCounty = await _context.SubCounties
                        .FirstOrDefaultAsync(s => s.Id == model.SubCountyId.Value);
                    if (subCounty != null)
                    {
                        subCountyName = subCounty.SubCountyName;
                    }
                }

                if (model.WardId.HasValue && model.WardId.Value > 0)
                {
                    var ward = await _context.Wards
                        .FirstOrDefaultAsync(w => w.Id == model.WardId.Value);
                    if (ward != null)
                    {
                        wardName = ward.WardName;
                    }
                }

                // Update user properties
                user.UserName = model.UserName?.Trim();
                user.Email = model.Email?.Trim();
                user.Phone = model.Phone?.Trim();
                user.PhoneNo = model.Phone?.Trim();
                user.Department = model.Department?.Trim();
                user.SubCounty = subCountyName;
                user.Ward = wardName;
                user.UserGroup = model.UserGroup;
                user.CompanyCode = model.CompanyCode;
                user.Status = model.Status;

                // If status is changed to Active, also update Userstatus
                if (model.Status == "Active")
                {
                    user.Userstatus = "Active";
                    if (model.Status == "Active" && user.IsLocked == true)
                    {
                        user.IsLocked = false;
                        user.FailedAttempts = 0;
                    }
                }
                else if (model.Status == "Locked")
                {
                    user.IsLocked = true;
                    user.Userstatus = "Locked";
                }
                else if (model.Status == "Pending")
                {
                    user.Userstatus = "Pending";
                    user.ApprovalStatus = "Pending";
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"User {user.UserName} (ID: {user.UserId}) updated successfully by {User.Identity?.Name}");
                TempData["SuccessMessage"] = $"User '{user.UserName}' updated successfully.";

                return RedirectToAction("Index");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, $"Database error in Edit POST for userId {id}");
                ModelState.AddModelError(string.Empty, "A database error occurred while updating the user.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in Edit POST for userId {id}");
                ModelState.AddModelError(string.Empty, "An unexpected error occurred while updating the user.");
            }

            // Reload dropdown data if there was an error
            await LoadCompanies();
            ViewBag.UserGroups = await _userService.GetUserGroupsAsync();

            // ===== FIX: Reload SubCounties when there's an error =====
            ViewBag.SubCounties = await _context.SubCounties
                .Where(s => s.Status == "Active")
                .OrderBy(s => s.SubCountyName)
                .Select(s => new { s.Id, s.SubCountyName })
                .ToListAsync();

            return View(model);
        }

        // GET: /Account/Details/5
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToAction("Index");
                }

                // Check permissions
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

                if (currentUserRole != "Super Admin" && user.CompanyCode != currentUserCompanyCode)
                {
                    TempData["ErrorMessage"] = "You don't have permission to view this user.";
                    return RedirectToAction("Index");
                }

                // Get company name
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == user.CompanyCode);

                var model = new UserDetailsVm
                {
                    UserId = user.UserId,
                    UserName = user.UserName,
                    UserLoginId = user.UserLoginId,
                    Email = user.Email,
                    Phone = user.Phone,
                    Department = user.Department,
                    SubCounty = user.SubCounty,
                    Ward = user.Ward,
                    UserGroup = user.UserGroup,
                    CompanyCode = user.CompanyCode,
                    CompanyName = company?.CompanyName ?? "Unknown",
                    Status = user.Status,
                    Userstatus = user.Userstatus,
                    ApprovalStatus = user.ApprovalStatus,
                    IsLocked = user.IsLocked ?? false,
                    FailedAttempts = user.FailedAttempts ?? 0,
                    DateCreated = user.DateCreated,
                    //DateModified = user.ModifiedAt,
                    //ModifiedBy = user.ModifiedBy,
                    PasswordStatus = user.PasswordStatus,
                    PassExpire = user.PassExpire
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in Details for userId {id}");
                TempData["ErrorMessage"] = "An error occurred while loading user details.";
                return RedirectToAction("Index");
            }
        }

        // POST: /Account/LockUser
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> LockUser(int userId)
        {
            try
            {
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Check permissions
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

                if (currentUserRole != "Super Admin" && user.CompanyCode != currentUserCompanyCode)
                {
                    return Json(new { success = false, message = "You don't have permission to lock this user" });
                }

                user.IsLocked = true;
                user.Status = "Locked";
                user.Userstatus = "Locked";
                await _context.SaveChangesAsync();

                _logger.LogInformation($"User {user.UserName} locked by {User.Identity?.Name}");
                return Json(new { success = true, message = "User locked successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error locking user {userId}");
                return Json(new { success = false, message = "An error occurred" });
            }
        }

        // POST: /Account/UnlockUser
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UnlockUser(int userId)
        {
            try
            {
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Check permissions
                var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                var currentUserCompanyCode = User.FindFirstValue("CompanyCode");

                if (currentUserRole != "Super Admin" && user.CompanyCode != currentUserCompanyCode)
                {
                    return Json(new { success = false, message = "You don't have permission to unlock this user" });
                }

                user.IsLocked = false;
                user.FailedAttempts = 0;
                if (user.Status == "Locked")
                {
                    user.Status = "Active";
                    user.Userstatus = "Active";
                }
                await _context.SaveChangesAsync();

                _logger.LogInformation($"User {user.UserName} unlocked by {User.Identity?.Name}");
                return Json(new { success = true, message = "User unlocked successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlocking user {userId}");
                return Json(new { success = false, message = "An error occurred" });
            }
        }



        // ============= FORGOT PASSWORD METHODS =============

        // GET: /Account/ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // POST: /Account/ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordVm model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Find user by username
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserName == model.Username);

                if (user == null)
                {
                    // Don't reveal that user doesn't exist for security
                    TempData["InfoMessage"] = "If the username exists, a verification code will be sent to your registered email.";
                    return RedirectToAction("ForgotPassword");
                }

                // Check if user has an email
                if (string.IsNullOrEmpty(user.Email))
                {
                    TempData["ErrorMessage"] = "No email address associated with this account. Please contact administrator.";
                    return RedirectToAction("ForgotPassword");
                }

                // Check if account is active
                if (user.Status?.ToLower() != "active" && user.Userstatus?.ToLower() != "active")
                {
                    TempData["ErrorMessage"] = "Your account is not active. Please contact administrator.";
                    return RedirectToAction("ForgotPassword");
                }

                // Generate 6-digit verification code
                var verificationCode = GenerateVerificationCode();

                // Store code with expiration (10 minutes from now)
                var codeExpiry = DateTime.Now.AddMinutes(10);

                // Store in TempData or Session (TempData is short-lived, use Session for better persistence)
                HttpContext.Session.SetString($"ResetCode_{model.Username}", verificationCode);
                HttpContext.Session.SetString($"ResetCodeExpiry_{model.Username}", codeExpiry.ToString("O"));

                // Store username for next steps
                TempData["ResetUsername"] = model.Username;

                // Send email with code
                var emailService = HttpContext.RequestServices.GetService<IEmailService>();
                if (emailService != null)
                {
                    var emailSent = await emailService.SendVerificationCodeAsync(user.Email, user.UserName, verificationCode);

                    if (emailSent)
                    {
                        TempData["SuccessMessage"] = $"Verification code sent to {MaskEmail(user.Email)}. Please check your email.";
                        return RedirectToAction("VerifyCode", new { username = model.Username });
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Failed to send verification email. Please try again later.";
                        return RedirectToAction("ForgotPassword");
                    }
                }
                else
                {
                    // For development/testing - show code on screen
                    _logger.LogWarning($"Email service not configured. Verification code for {model.Username}: {verificationCode}");
                    TempData["SuccessMessage"] = $"[DEV MODE] Verification code: {verificationCode}";
                    return RedirectToAction("VerifyCode", new { username = model.Username });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ForgotPassword");
                TempData["ErrorMessage"] = "An error occurred. Please try again.";
                return View(model);
            }
        }

        // GET: /Account/VerifyCode
        [HttpGet]
        public IActionResult VerifyCode(string username)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }

            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("ForgotPassword");
            }

            var model = new VerifyCodeVm
            {
                Username = username
            };

            return View(model);
        }

        // POST: /Account/VerifyCode
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyCode(VerifyCodeVm model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Retrieve stored code and expiry
                var storedCode = HttpContext.Session.GetString($"ResetCode_{model.Username}");
                var expiryStr = HttpContext.Session.GetString($"ResetCodeExpiry_{model.Username}");

                if (string.IsNullOrEmpty(storedCode) || string.IsNullOrEmpty(expiryStr))
                {
                    TempData["ErrorMessage"] = "No verification code found. Please request a new code.";
                    return RedirectToAction("ForgotPassword");
                }

                // Check if code has expired
                if (DateTime.TryParse(expiryStr, out var expiry) && expiry < DateTime.Now)
                {
                    // Clear expired code
                    HttpContext.Session.Remove($"ResetCode_{model.Username}");
                    HttpContext.Session.Remove($"ResetCodeExpiry_{model.Username}");
                    TempData["ErrorMessage"] = "Verification code has expired. Please request a new code.";
                    return RedirectToAction("ForgotPassword");
                }

                // Verify code
                if (storedCode != model.Code)
                {
                    ModelState.AddModelError("Code", "Invalid verification code. Please try again.");
                    return View(model);
                }

                // Code is valid - clear it from session
                HttpContext.Session.Remove($"ResetCode_{model.Username}");
                HttpContext.Session.Remove($"ResetCodeExpiry_{model.Username}");

                // Store that user is verified for password reset
                HttpContext.Session.SetString($"VerifiedForReset_{model.Username}", "true");

                TempData["SuccessMessage"] = "Code verified successfully. Please enter your new password.";
                return RedirectToAction("ResetPassword", new { username = model.Username });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in VerifyCode");
                TempData["ErrorMessage"] = "An error occurred. Please try again.";
                return View(model);
            }
        }

        // GET: /Account/ResetPassword
        [HttpGet]
        public IActionResult ResetPassword(string username)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }

            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("ForgotPassword");
            }

            // Check if user is verified
            var isVerified = HttpContext.Session.GetString($"VerifiedForReset_{username}");
            if (string.IsNullOrEmpty(isVerified) || isVerified != "true")
            {
                TempData["ErrorMessage"] = "Please verify your code first.";
                return RedirectToAction("ForgotPassword");
            }

            var model = new ResetPasswordVm
            {
                Username = username
            };

            return View(model);
        }

        // POST: /Account/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordVm model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Check if user is verified
                var isVerified = HttpContext.Session.GetString($"VerifiedForReset_{model.Username}");
                if (string.IsNullOrEmpty(isVerified) || isVerified != "true")
                {
                    TempData["ErrorMessage"] = "Please verify your code first.";
                    return RedirectToAction("ForgotPassword");
                }

                // Find user
                var user = await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserName == model.Username);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return RedirectToAction("ForgotPassword");
                }

                // Update only the password column
                user.Password = HashPassword(model.NewPassword);

                // Optional: Reset failed attempts and unlock if locked
                if (user.IsLocked == true)
                {
                    user.IsLocked = false;
                }
                user.FailedAttempts = 0;

                // Update password status
                user.PasswordStatus = "Active";
                user.PassExpire = "No";

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Password reset for user {user.UserName} (ID: {user.UserId})");

                // Clear verification session
                HttpContext.Session.Remove($"VerifiedForReset_{model.Username}");

                TempData["SuccessMessage"] = "Password reset successfully! Please login with your new password.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resetting password for {model.Username}");
                TempData["ErrorMessage"] = "An error occurred while resetting password. Please try again.";
                return View(model);
            }
        }

        // Helper method to generate 6-digit code
        private string GenerateVerificationCode()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        // Helper method to mask email for display
        private string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
                return email;

            var parts = email.Split('@');
            var username = parts[0];
            var domain = parts[1];

            if (username.Length <= 3)
                return $"{username[0]}***@{domain}";

            var maskedUsername = username.Substring(0, 3) + new string('*', username.Length - 3);
            return $"{maskedUsername}@{domain}";
        }
    }
}