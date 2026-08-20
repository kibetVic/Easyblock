using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;

namespace SACCOBlockChainSystem.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IDashboardService _dashboardService;
        private readonly IBlockchainService _blockchainService;
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public HomeController(
            IDashboardService dashboardService,
            IBlockchainService blockchainService,
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment)
        {
            _dashboardService = dashboardService;
            _blockchainService = blockchainService;
            _logger = logger;
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index(string? companyCode)
        {
            try
            {
                // Get the logged-in user's role and company code from claims
                var userRole = User.FindFirstValue(ClaimTypes.Role);
                var isSuperAdmin = userRole == "Super Admin" || userRole == "SuperAdmin";
                var userCompanyCode = User.FindFirst("CompanyCode")?.Value ??
                                      User.FindFirst("SaccoCode")?.Value ??
                                      User.FindFirst("Company")?.Value;

                // Determine the effective company code for filtering
                string effectiveCompanyCode = null;

                if (isSuperAdmin)
                {
                    // Super Admin: Use selected company code if provided, otherwise null (show all)
                    effectiveCompanyCode = string.IsNullOrEmpty(companyCode) ? null : companyCode;
                    ViewBag.ShowCompanyFilter = true;
                }
                else
                {
                    // Non-SuperAdmin: Always limited to their own company
                    effectiveCompanyCode = userCompanyCode;
                    companyCode = userCompanyCode; // Override any passed company code
                    ViewBag.ShowCompanyFilter = false;
                }

                var userCompanyName = User.FindFirst("CompanyName")?.Value ??
                                      User.FindFirst("SaccoName")?.Value;

                DashboardVM dashboard = await GetUniversalDashboardDataAsync(effectiveCompanyCode, isSuperAdmin);

                var cutoffDate = DateTime.Now.AddMonths(-6);

                // Build member query with role-based filtering
                var membersQuery = _context.Members.AsQueryable();

                // Apply filtering based on role and effective company code
                if (!isSuperAdmin && !string.IsNullOrEmpty(effectiveCompanyCode))
                {
                    // Non-SuperAdmin: Filter by their company
                    membersQuery = membersQuery.Where(m => m.CompanyCode == effectiveCompanyCode);
                    dashboard.SelectedCompanyCode = effectiveCompanyCode;
                    dashboard.SelectedCompanyName = userCompanyName ?? effectiveCompanyCode;
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(effectiveCompanyCode))
                {
                    // SuperAdmin: Filter by selected company
                    membersQuery = membersQuery.Where(m => m.CompanyCode == effectiveCompanyCode);
                    dashboard.SelectedCompanyCode = effectiveCompanyCode;
                    dashboard.SelectedCompanyName = effectiveCompanyCode;
                }
                else if (isSuperAdmin && string.IsNullOrEmpty(effectiveCompanyCode))
                {
                    // SuperAdmin: No company filter - show ALL companies
                    dashboard.SelectedCompanyName = "All Companies";
                    dashboard.SelectedCompanyCode = "ALL";
                    // Don't apply any company filter to membersQuery
                }

                // Get all members (filtered appropriately)
                var members = await membersQuery
                    .Where(m => m.Dob.HasValue || m.Status.HasValue)
                    .Select(m => new
                    {
                        m.MemberNo,
                        m.Sex,
                        m.Dob,
                        m.Status,
                        m.EffectDate,
                        m.Withdrawn,
                        m.Dormant,
                        m.CompanyCode
                    })
                    .ToListAsync();

                // ==========================
                // MEMBER STATISTICS
                // ==========================
                dashboard.TotalWomen = members.Count(m =>
                    !string.IsNullOrEmpty(m.Sex) && m.Sex.ToUpper() == "FEMALE");

                dashboard.TotalMen = members.Count(m =>
                    !string.IsNullOrEmpty(m.Sex) && m.Sex.ToUpper() == "MALE");

                dashboard.TotalOthers = members.Count(m =>
                    string.IsNullOrEmpty(m.Sex) ||
                    (m.Sex.ToUpper() != "MALE" && m.Sex.ToUpper() != "FEMALE"));

                dashboard.TotalMembers = members.Count;

                // ACTIVE & DORMANT MEMBERS
                var activeMemberNos = await GetActiveMemberNumbersAsync(cutoffDate, effectiveCompanyCode, isSuperAdmin);
                var activeFromStatus = members.Where(m => m.Status == 1).Select(m => m.MemberNo).ToHashSet();

                var allActiveMembers = new HashSet<string>(activeMemberNos);
                allActiveMembers.UnionWith(activeFromStatus);

                dashboard.ActiveMembers = allActiveMembers.Count;
                dashboard.DormantMembers = dashboard.TotalMembers - dashboard.ActiveMembers;

                // ACTIVE/DORMANT BY GENDER
                dashboard.ActiveWomen = members.Count(m =>
                    !string.IsNullOrEmpty(m.Sex) &&
                    m.Sex.ToUpper() == "FEMALE" &&
                    allActiveMembers.Contains(m.MemberNo));

                dashboard.DormantWomen = dashboard.TotalWomen - dashboard.ActiveWomen;

                dashboard.ActiveMen = members.Count(m =>
                    !string.IsNullOrEmpty(m.Sex) &&
                    m.Sex.ToUpper() == "MALE" &&
                    allActiveMembers.Contains(m.MemberNo));

                dashboard.DormantMen = dashboard.TotalMen - dashboard.ActiveMen;

                // YOUTH CALCULATION (<= 35 years)
                var membersWithAge = members.Where(m => m.Dob.HasValue).ToList();

                dashboard.YouthTotal = membersWithAge.Count(m =>
                {
                    var age = CalculateAgeSafe(m.Dob.Value);
                    return age <= 35;
                });

                dashboard.YouthMale = membersWithAge.Count(m =>
                {
                    var age = CalculateAgeSafe(m.Dob.Value);
                    return age <= 35 && !string.IsNullOrEmpty(m.Sex) && m.Sex.ToUpper() == "MALE";
                });

                dashboard.YouthFemale = membersWithAge.Count(m =>
                {
                    var age = CalculateAgeSafe(m.Dob.Value);
                    return age <= 35 && !string.IsNullOrEmpty(m.Sex) && m.Sex.ToUpper() == "FEMALE";
                });

                // ==========================
                // FINANCIAL DATA FROM TABLES
                // ==========================

                // Get Member Contributions from ContribShares table
                var contributionsData = await GetContributionsDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalContributions = contributionsData.Total;
                dashboard.WomenContributions = contributionsData.Women;
                dashboard.MenContributions = contributionsData.Men;
                dashboard.OthersContributions = contributionsData.Others;

                // Get Share Capital from Shares table
                var shareCapitalData = await GetShareCapitalDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalShareCapital = shareCapitalData.Total;
                dashboard.WomenShareCapital = shareCapitalData.Women;
                dashboard.MenShareCapital = shareCapitalData.Men;
                dashboard.OthersShareCapital = shareCapitalData.Others;

                // Get Non-Withdrawable Deposits from ContribShares (DepositsAmount)
                var depositsData = await GetDepositsDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalDeposits = depositsData.Total;
                dashboard.WomenDeposits = depositsData.Women;
                dashboard.MenDeposits = depositsData.Men;
                dashboard.OthersDeposits = depositsData.Others;

                // Get Registration Fees from Members table (RegFee)
                var registrationData = await GetRegistrationFeesDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalRegistrationFees = registrationData.Total;
                dashboard.WomenRegistrationFees = registrationData.Women;
                dashboard.MenRegistrationFees = registrationData.Men;
                dashboard.OthersRegistrationFees = registrationData.Others;

                // Get Loans Taken from Loans table
                var loansTakenData = await GetLoansTakenDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalLoansTaken = loansTakenData.Total;
                dashboard.WomenLoansTaken = loansTakenData.Women;
                dashboard.MenLoansTaken = loansTakenData.Men;
                dashboard.OthersLoansTaken = loansTakenData.Others;

                // Get Loan Balances from Loans table (outstanding)
                var loanBalancesData = await GetLoanBalancesDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalLoanBalances = loanBalancesData.Total;
                dashboard.WomenLoanBalances = loanBalancesData.Women;
                dashboard.MenLoanBalances = loanBalancesData.Men;
                dashboard.OthersLoanBalances = loanBalancesData.Others;

                // Get Loans Paid from Loanbals table (Cleared)
                var loansPaidData = await GetLoansPaidDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalLoansPaid = loansPaidData.Total;
                dashboard.WomenLoansPaid = loansPaidData.Women;
                dashboard.MenLoansPaid = loansPaidData.Men;
                dashboard.OthersLoansPaid = loansPaidData.Others;

                // Get Total Loanees from Loans table (distinct members with loans)
                var loaneesData = await GetLoaneesDataAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalLoanees = loaneesData.Total;
                dashboard.WomenLoanees = loaneesData.Women;
                dashboard.MenLoanees = loaneesData.Men;
                dashboard.OthersLoanees = loaneesData.Others;

                dashboard.RepaymentRate = await CalculateRepaymentRateAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.PARPercent = await CalculatePARPercentAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.AmountPastDueRate = dashboard.PARPercent;
                dashboard.OutstandingLoanPortfolio = await CalculateOutstandingLoanPortfolioAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.ArrearsBalance = await CalculateArrearsBalanceAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.TotalArrears = dashboard.ArrearsBalance;
                dashboard.WomenParticipationRate = await CalculateWomenParticipationRateAsync(effectiveCompanyCode, isSuperAdmin);
                dashboard.LoanPortfolioHealth = GetLoanPortfolioHealth(dashboard.PARPercent);

                // Get grants data - SuperAdmin sees all, others see filtered
                dashboard.InclusionGrantTotal = await GetGrantTotalAsync("inclusion grant", effectiveCompanyCode, isSuperAdmin);
                dashboard.MatchingGrantTotal = await GetGrantTotalAsync("matching grant", effectiveCompanyCode, isSuperAdmin);

                // Load chart data
                dashboard.MonthlyTransactions = await GetMonthlyTransactionsDataAsync(6, effectiveCompanyCode, isSuperAdmin);
                dashboard.MemberGrowth = await GetMemberGrowthDataAsync(12, effectiveCompanyCode, isSuperAdmin);

                // Get companies for filter dropdown (only for Super Admin)
                if (isSuperAdmin)
                {
                    dashboard.Companies = await _context.Companies
                        .Where(c => c.Project == true)
                        .Select(c => new CompanyInfo
                        {
                            Code = c.CompanyCode,
                            Name = c.CompanyName ?? c.CompanyCode
                        })
                        .OrderBy(c => c.Name)
                        .ToListAsync();
                }
                else
                {
                    dashboard.Companies = new List<CompanyInfo>();
                }

                // Get user info
                dashboard.UserGroup = GetUserGroup();
                dashboard.UserRoles = User.Claims
                    .Where(c => c.Type == ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToList();

                ViewData["Title"] = $"{dashboard.UserGroup} Dashboard";
                ViewData["Subtitle"] = $"SACCO Blockchain System - {dashboard.SelectedCompanyName}";

                return View(dashboard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard: {Message}", ex.Message);
                _logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);

                if (_webHostEnvironment.IsDevelopment())
                {
                    return Content($"Error: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
                }

                return View("Error");
            }
        }


        #region Financial Data Methods

        private static string NormalizeGender(string? gender)
        {
            if (string.IsNullOrEmpty(gender))
                return "OTHERS";

            var normalized = gender.ToUpper().Trim();

            if (normalized == "M" || normalized == "MALE")
                return "MALE";

            if (normalized == "F" || normalized == "FEMALE")
                return "FEMALE";

            return "OTHERS";
        }

        // Helper method to get filtered member numbers based on role and company
        private async Task<List<string>> GetFilteredMemberNosAsync(string? companyCode, bool isSuperAdmin)
        {
            var membersQuery = _context.Members.AsQueryable();

            // Only apply company filter if:
            // 1. Not SuperAdmin (they have a company assigned), OR
            // 2. SuperAdmin with a specific company selected
            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
            }
            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
            }
            // If SuperAdmin and companyCode is null, don't filter - return all members

            return await membersQuery.Select(m => m.MemberNo).ToListAsync();
        }

        // CONTRIBUTIONS - From ContribShare table (ShareCapitalAmount + DepositsAmount)
        private async Task<(decimal Total, decimal Women, decimal Men, decimal Others)> GetContributionsDataAsync(string? companyCode, bool isSuperAdmin)
        {
            var query = _context.ContribShares.AsQueryable();

            // Apply company filter based on role
            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(c => memberNos.Contains(c.MemberNo));
            }
            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(c => memberNos.Contains(c.MemberNo));
            }
            // If SuperAdmin and no companyCode, include ALL contributions

            var contributions = await query.ToListAsync();

            // Get member genders for all members in the result
            var memberGenders = await _context.Members
                .Where(m => contributions.Select(c => c.MemberNo).Contains(m.MemberNo))
                .Select(m => new { m.MemberNo, m.Sex })
                .ToDictionaryAsync(m => m.MemberNo, m => m.Sex);

            decimal womenContributions = 0;
            decimal menContributions = 0;
            decimal othersContributions = 0;

            foreach (var c in contributions)
            {
                var rawGender = memberGenders.ContainsKey(c.MemberNo) ? memberGenders[c.MemberNo] : null;
                var gender = NormalizeGender(rawGender);
                var amount = (c.ShareCapitalAmount ?? 0) + (c.DepositsAmount ?? 0);

                if (gender == "FEMALE")
                    womenContributions += amount;
                else if (gender == "MALE")
                    menContributions += amount;
                else
                    othersContributions += amount;
            }

            var total = womenContributions + menContributions + othersContributions;
            return (total, womenContributions, menContributions, othersContributions);
        }

        // SHARE CAPITAL - From Shares table (TotalShares)
        private async Task<(decimal Total, decimal Women, decimal Men, decimal Others)> GetShareCapitalDataAsync(string? companyCode, bool isSuperAdmin)
        {
            var query = _context.Shares.AsQueryable();

            // Apply company filter based on role
            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(s => memberNos.Contains(s.MemberNo));
            }
            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(s => memberNos.Contains(s.MemberNo));
            }
            // If SuperAdmin and no companyCode, include ALL shares

            var shares = await query.ToListAsync();

            var memberGenders = await _context.Members
                .Where(m => shares.Select(s => s.MemberNo).Contains(m.MemberNo))
                .Select(m => new { m.MemberNo, m.Sex })
                .ToDictionaryAsync(m => m.MemberNo, m => m.Sex);

            decimal womenShares = 0;
            decimal menShares = 0;
            decimal othersShares = 0;

            foreach (var s in shares)
            {
                var rawGender = memberGenders.ContainsKey(s.MemberNo) ? memberGenders[s.MemberNo] : null;
                var gender = NormalizeGender(rawGender);
                var amount = s.TotalShares ?? 0;

                if (gender == "FEMALE")
                    womenShares += amount;
                else if (gender == "MALE")
                    menShares += amount;
                else
                    othersShares += amount;
            }

            var total = womenShares + menShares + othersShares;
            return (total, womenShares, menShares, othersShares);
        }

        // NON-WITHDRAWABLE DEPOSITS - From ContribShare table (DepositsAmount)
        private async Task<(decimal Total, decimal Women, decimal Men, decimal Others)> GetDepositsDataAsync(string? companyCode, bool isSuperAdmin)
        {
            var query = _context.ContribShares.AsQueryable();

            // Apply company filter based on role
            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(c => memberNos.Contains(c.MemberNo));
            }
            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(c => memberNos.Contains(c.MemberNo));
            }
            // If SuperAdmin and no companyCode, include ALL deposits

            var deposits = await query.ToListAsync();

            var memberGenders = await _context.Members
                .Where(m => deposits.Select(d => d.MemberNo).Contains(m.MemberNo))
                .Select(m => new { m.MemberNo, NormalizedGender = NormalizeGender(m.Sex) })
                .ToDictionaryAsync(m => m.MemberNo, m => m.NormalizedGender);

            decimal womenDeposits = 0;
            decimal menDeposits = 0;
            decimal othersDeposits = 0;

            foreach (var d in deposits)
            {
                var gender = memberGenders.ContainsKey(d.MemberNo) ? memberGenders[d.MemberNo] : "OTHERS";
                var amount = d.DepositsAmount ?? 0;

                if (gender == "FEMALE")
                    womenDeposits += amount;
                else if (gender == "MALE")
                    menDeposits += amount;
                else
                    othersDeposits += amount;
            }

            var total = womenDeposits + menDeposits + othersDeposits;
            return (total, womenDeposits, menDeposits, othersDeposits);
        }

        // REGISTRATION FEES - From ContribShare table (RegFeeAmount)
        private async Task<(decimal Total, decimal Women, decimal Men, decimal Others)> GetRegistrationFeesDataAsync(string? companyCode, bool isSuperAdmin)
        {
            var query = _context.ContribShares.AsQueryable();

            // Apply company filter based on role
            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(c => memberNos.Contains(c.MemberNo));
            }
            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                var memberNos = await _context.Members
                    .Where(m => m.CompanyCode == companyCode)
                    .Select(m => m.MemberNo)
                    .ToListAsync();
                query = query.Where(c => memberNos.Contains(c.MemberNo));
            }
            // If SuperAdmin and no companyCode, include ALL fees

            var fees = await query.ToListAsync();

            var memberGenders = await _context.Members
                .Where(m => fees.Select(f => f.MemberNo).Contains(m.MemberNo))
                .Select(m => new { m.MemberNo, NormalizedGender = NormalizeGender(m.Sex) })
                .ToDictionaryAsync(m => m.MemberNo, m => m.NormalizedGender);

            decimal womenFees = 0;
            decimal menFees = 0;
            decimal othersFees = 0;

            foreach (var f in fees)
            {
                var gender = memberGenders.ContainsKey(f.MemberNo) ? memberGenders[f.MemberNo] : "OTHERS";
                var amount = f.RegFeeAmount ?? 0;

                if (gender == "FEMALE")
                    womenFees += amount;
                else if (gender == "MALE")
                    menFees += amount;
                else
                    othersFees += amount;
            }

            var total = womenFees + menFees + othersFees;
            return (total, womenFees, menFees, othersFees);
        }

        // LOANS TAKEN - From Cheques table
        private async Task<(decimal Total, decimal Women, decimal Men, decimal Others)> GetLoansTakenDataAsync(string? companyCode, bool isSuperAdmin)
        {
            var query = from c in _context.Cheques
                        join m in _context.Members on c.MemberNo equals m.MemberNo
                        select new
                        {
                            Amount = c.Amount ?? 0,
                            MemberNo = m.MemberNo,
                            Sex = m.Sex,
                            CompanyCode = c.CompanyCode
                        };

            // Apply company filter based on role
            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                query = query.Where(x => x.CompanyCode == companyCode);
            }
            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                query = query.Where(x => x.CompanyCode == companyCode);
            }
            // If SuperAdmin and no companyCode, include ALL loans

            var data = await query.ToListAsync();

            decimal women = 0;
            decimal men = 0;
            decimal others = 0;

            foreach (var item in data)
            {
                var gender = NormalizeGender(item.Sex);

                if (gender == "FEMALE")
                    women += item.Amount;
                else if (gender == "MALE")
                    men += item.Amount;
                else
                    others += item.Amount;
            }

            var total = women + men + others;
            return (total, women, men, others);
        }

        // LOAN BALANCES - From Loanbal table
        private async Task<(decimal Total, decimal Women, decimal Men, decimal Others)> GetLoanBalancesDataAsync(string? companyCode, bool isSuperAdmin)
        {
            var query = from lb in _context.Loanbal
                        join m in _context.Members on lb.MemberNo equals m.MemberNo
                        select new
                        {
                            lb.Balance,
                            MemberNo = lb.MemberNo,
                            Sex = m.Sex,
                            CompanyCode = lb.Companycode
                        };

            // Apply company filter based on role
            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                query = query.Where(x => x.CompanyCode == companyCode);
            }
            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
            {
                query = query.Where(x => x.CompanyCode == companyCode);
            }
            // If SuperAdmin and no companyCode, include ALL loan balances

            var loanBalances = await query.ToListAsync();

            decimal womenBalances = 0;
            decimal menBalances = 0;
            decimal othersBalances = 0;

            foreach (var lb in loanBalances)
            {
                var gender = NormalizeGender(lb.Sex);

                if (gender == "FEMALE")
                    womenBalances += lb.Balance;
                else if (gender == "MALE")
                    menBalances += lb.Balance;
                else
                    othersBalances += lb.Balance;
            }

            var total = womenBalances + menBalances + othersBalances;
            return (total, womenBalances, menBalances, othersBalances);
        }

        // LOANS PAID - From Repay table
        private async Task<(decimal Total, decimal Women, decimal Men, decimal Others)> GetLoansPaidDataAsync(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var query = from r in _context.Repay
                            join m in _context.Members on r.MemberNo equals m.MemberNo
                            where r.Principal > 0 && r.Principal != null
                            select new
                            {
                                Principal = r.Principal ?? 0,
                                MemberNo = r.MemberNo,
                                Sex = m.Sex,
                                CompanyCode = m.CompanyCode
                            };

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(x => x.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(x => x.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL repayments

                var repayments = await query.ToListAsync();

                decimal womenPaid = 0;
                decimal menPaid = 0;
                decimal othersPaid = 0;

                foreach (var r in repayments)
                {
                    var gender = NormalizeGender(r.Sex);

                    if (gender == "FEMALE")
                        womenPaid += r.Principal;
                    else if (gender == "MALE")
                        menPaid += r.Principal;
                    else
                        othersPaid += r.Principal;
                }

                var total = womenPaid + menPaid + othersPaid;

                _logger.LogInformation($"Loans Paid - Total: {total}, Women: {womenPaid}, Men: {menPaid}");

                return (total, womenPaid, menPaid, othersPaid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loans paid data: {Message}", ex.Message);
                return (0, 0, 0, 0);
            }
        }

        // TOTAL LOANEES - Distinct members with loans
        private async Task<(int Total, int Women, int Men, int Others)> GetLoaneesDataAsync(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var query = from l in _context.Loans
                            join m in _context.Members on l.MemberNo equals m.MemberNo
                            select new
                            {
                                l.MemberNo,
                                Sex = m.Sex,
                                CompanyCode = m.CompanyCode
                            };

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(x => x.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(x => x.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL loanees

                var borrowers = await query
                    .GroupBy(x => new { x.MemberNo, x.Sex })
                    .Select(g => new { g.Key.MemberNo, g.Key.Sex })
                    .ToListAsync();

                int womenLoanees = 0;
                int menLoanees = 0;
                int othersLoanees = 0;

                foreach (var b in borrowers)
                {
                    var gender = NormalizeGender(b.Sex);

                    if (gender == "FEMALE")
                        womenLoanees++;
                    else if (gender == "MALE")
                        menLoanees++;
                    else
                        othersLoanees++;
                }

                var total = womenLoanees + menLoanees + othersLoanees;

                return (total, womenLoanees, menLoanees, othersLoanees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loanees data: {Message}", ex.Message);
                return (0, 0, 0, 0);
            }
        }

        // Helper method to get grant totals with role-based filtering
        private async Task<decimal> GetGrantTotalAsync(string keyword, string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var query = _context.Journals
                    .Where(j =>
                        j.NARATION != null &&
                        j.TRANSTYPE == "CR" &&
                        j.NARATION.ToLower().Contains(keyword));

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(j => j.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(j => j.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL grants

                return await query.SumAsync(j => (decimal?)j.AMOUNT) ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting grant total for {keyword}");
                return 0;
            }
        }

        #endregion


        // Calculate Repayment Rate (Current Month)
        private async Task<decimal> CalculateRepaymentRateAsync(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;

                // Get active loans that should be making payments this month
                var activeLoansQuery = _context.Loans
                    .Where(l => l.ApplicDate <= DateTime.Now) // Loan was taken before or on today
                    .Where(l => l.LoanAmt > 0 && l.RepayPeriod > 0);

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    activeLoansQuery = activeLoansQuery.Where(l => l.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    activeLoansQuery = activeLoansQuery.Where(l => l.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL loans

                var activeLoans = await activeLoansQuery.ToListAsync();

                // Calculate expected payments for current month (only for loans that are still active)
                decimal expectedPayments = 0;
                foreach (var loan in activeLoans)
                {
                    // Calculate how many months the loan has been active
                    var monthsSinceStart = ((DateTime.Now.Year - loan.ApplicDate.Year) * 12) +
                                           (DateTime.Now.Month - loan.ApplicDate.Month);

                    var totalRepayPeriod = loan.RepayPeriod ?? 1;

                    // Only include if still within repayment period
                    if (monthsSinceStart < totalRepayPeriod)
                    {
                        var monthlyPayment = (loan.LoanAmt ?? 0) / totalRepayPeriod;
                        expectedPayments += monthlyPayment;
                    }
                }

                // Get actual payments for current month
                var repayQuery = from r in _context.Repay
                                 join l in _context.Loans on r.LoanNo equals l.LoanNo
                                 where r.DateReceived.HasValue &&
                                       r.DateReceived.Value.Month == currentMonth &&
                                       r.DateReceived.Value.Year == currentYear
                                 select new { r.Amount, l.CompanyCode };

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    repayQuery = repayQuery.Where(x => x.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    repayQuery = repayQuery.Where(x => x.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL repayments

                var actualPayments = await repayQuery.SumAsync(x => x.Amount ?? 0);

                // Repayment rate should never exceed 100%
                var rate = expectedPayments > 0 ? (actualPayments / expectedPayments) * 100 : 0;
                return Math.Min(rate, 100); // Cap at 100%
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating repayment rate");
                return 0;
            }
        }

        // Calculate PAR > 30 Days (Portfolio at Risk)
        private async Task<decimal> CalculatePARPercentAsync(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var thirtyDaysAgo = DateTime.Now.AddDays(-30);

                // Get loans that are overdue by more than 30 days
                var loansQuery = _context.Loans.AsQueryable();

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    loansQuery = loansQuery.Where(l => l.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    loansQuery = loansQuery.Where(l => l.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL loans

                var loans = await loansQuery.ToListAsync();

                // Calculate overdue amount (loans where last payment > 30 days ago or past maturity date)
                decimal overdueAmount = 0;
                decimal totalOutstanding = 0;

                foreach (var loan in loans)
                {
                    // Get last repayment date for this loan
                    var lastRepayment = await _context.Repay
                        .Where(r => r.LoanNo == loan.LoanNo && r.DateReceived.HasValue)
                        .OrderByDescending(r => r.DateReceived)
                        .FirstOrDefaultAsync();

                    var isOverdue = false;

                    if (lastRepayment != null && lastRepayment.DateReceived.HasValue)
                    {
                        // If last payment was more than 30 days ago and loan not fully paid
                        if (lastRepayment.DateReceived.Value < thirtyDaysAgo && (loan.LoanAmt > 0))
                        {
                            isOverdue = true;
                        }
                    }
                    else if (loan.ApplicDate < thirtyDaysAgo && loan.Status != (int)Status.Closed)
                    {
                        // No payments made and loan is old
                        isOverdue = true;
                    }

                    if (isOverdue)
                    {
                        overdueAmount += loan.LoanAmt ?? 0;
                    }

                    totalOutstanding += loan.LoanAmt ?? 0;
                }

                return totalOutstanding > 0 ? (overdueAmount / totalOutstanding) * 100 : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating PAR percentage");
                return 0;
            }
        }

        // Calculate Outstanding Loan Portfolio
        private async Task<decimal> CalculateOutstandingLoanPortfolioAsync(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var loansQuery = _context.Loans.AsQueryable();

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    loansQuery = loansQuery.Where(l => l.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    loansQuery = loansQuery.Where(l => l.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL loans

                // Get total loan amount minus what has been repaid
                var loans = await loansQuery.ToListAsync();
                decimal totalOutstanding = 0;

                foreach (var loan in loans)
                {
                    var totalRepaid = await _context.Repay
                        .Where(r => r.LoanNo == loan.LoanNo)
                        .SumAsync(r => r.Amount ?? 0);

                    var outstanding = (loan.LoanAmt ?? 0) - totalRepaid;
                    if (outstanding > 0)
                    {
                        totalOutstanding += outstanding;
                    }
                }

                return totalOutstanding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating outstanding loan portfolio");
                return 0;
            }
        }

        // Calculate Arrears Balance (>30 Days)
        private async Task<decimal> CalculateArrearsBalanceAsync(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var thirtyDaysAgo = DateTime.Now.AddDays(-30);

                var loansQuery = _context.Loans.AsQueryable();

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    loansQuery = loansQuery.Where(l => l.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    loansQuery = loansQuery.Where(l => l.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL loans

                var loans = await loansQuery.ToListAsync();
                decimal arrearsBalance = 0;

                foreach (var loan in loans)
                {
                    var lastRepayment = await _context.Repay
                        .Where(r => r.LoanNo == loan.LoanNo && r.DateReceived.HasValue)
                        .OrderByDescending(r => r.DateReceived)
                        .FirstOrDefaultAsync();

                    if (lastRepayment != null && lastRepayment.DateReceived.HasValue)
                    {
                        if (lastRepayment.DateReceived.Value < thirtyDaysAgo && loan.Status != (int)Status.Closed)
                        {
                            var totalRepaid = await _context.Repay
                                .Where(r => r.LoanNo == loan.LoanNo)
                                .SumAsync(r => r.Amount ?? 0);

                            var outstanding = (loan.LoanAmt ?? 0) - totalRepaid;
                            arrearsBalance += outstanding > 0 ? outstanding : 0;
                        }
                    }
                    else if (loan.ApplicDate < thirtyDaysAgo && loan.Status != (int)Status.Closed)
                    {
                        arrearsBalance += loan.LoanAmt ?? 0;
                    }
                }

                return arrearsBalance;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating arrears balance");
                return 0;
            }
        }

        // Calculate Women Participation Rate
        private async Task<decimal> CalculateWomenParticipationRateAsync(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var membersQuery = _context.Members.AsQueryable();

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL members

                var totalMembers = await membersQuery.CountAsync();
                var womenMembers = await membersQuery.CountAsync(m => m.Sex == "FEMALE");

                return totalMembers > 0 ? (womenMembers * 100m / totalMembers) : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating women participation rate");
                return 0;
            }
        }

        // Get Loan Portfolio Health Status
        private string GetLoanPortfolioHealth(decimal parPercent)
        {
            if (parPercent < 5) return "Excellent";
            if (parPercent < 10) return "Good";
            if (parPercent < 20) return "Fair";
            return "At Risk";
        }

        private BlockchainDashboardData GetBlockchainData()
        {
            return new BlockchainDashboardData();
        }

        private List<WalletInfo> GetWalletData()
        {
            return new List<WalletInfo>();
        }

        private List<Models.Block> GetRecentBlocks(int count)
        {
            return new List<Models.Block>();
        }

        private List<BlockchainChain> GetBlockchainChains()
        {
            return new List<BlockchainChain>();
        }

        private async Task<HashSet<string>> GetActiveMemberNumbersAsync(DateTime cutoffDate, string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var activeFromTransactionsQuery = _context.Transactions2
                    .Where(t => t.Status == "COMPLETED" && t.ContributionDate >= cutoffDate)
                    .Select(t => t.MemberNo);

                var activeFromRepaysQuery = _context.Repay
                    .Where(r => r.DateReceived.HasValue && r.DateReceived.Value >= cutoffDate)
                    .Select(r => r.MemberNo);

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        activeFromTransactionsQuery = activeFromTransactionsQuery
                            .Where(t => membersInCompany.Contains(t));
                        activeFromRepaysQuery = activeFromRepaysQuery
                            .Where(r => membersInCompany.Contains(r));
                    }
                    else
                    {
                        return new HashSet<string>();
                    }
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        activeFromTransactionsQuery = activeFromTransactionsQuery
                            .Where(t => membersInCompany.Contains(t));
                        activeFromRepaysQuery = activeFromRepaysQuery
                            .Where(r => membersInCompany.Contains(r));
                    }
                    else
                    {
                        return new HashSet<string>();
                    }
                }
                // If SuperAdmin and no companyCode, include ALL members (no filtering)

                var activeTransactionMembers = await activeFromTransactionsQuery.Distinct().ToListAsync();
                var activeRepayMembers = await activeFromRepaysQuery.Distinct().ToListAsync();

                var activeSet = new HashSet<string>(activeTransactionMembers);
                activeSet.UnionWith(activeRepayMembers);

                return activeSet;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active member numbers");
                return new HashSet<string>();
            }
        }

        private async Task<DashboardVM> GetUniversalDashboardDataAsync(string? companyCode, bool isSuperAdmin)
        {
            var dashboard = new DashboardVM();

            try
            {
                var membersQuery = _context.Members.AsQueryable();

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                }
                // If SuperAdmin and no companyCode, include ALL members

                dashboard.TotalMembers = await membersQuery.CountAsync();
                dashboard.ActiveMembersByStatus = await membersQuery.CountAsync(m => m.Status == 1);

                // Blockchain stats are global (not filtered by company)
                dashboard.TotalBlockchainTransactions = await _context.BlockchainTransactions.CountAsync();
                dashboard.BlocksCreatedToday = await _context.Blocks
                    .Where(b => b.Timestamp.Date == DateTime.Today)
                    .CountAsync();
                dashboard.PendingBlockchainTransactions = await _context.BlockchainTransactions
                    .CountAsync(t => t.Status == "PENDING");

                var memberNo = User.FindFirst("MemberNo")?.Value;
                if (!string.IsNullOrEmpty(memberNo))
                {
                    var member = await _context.Members.FirstOrDefaultAsync(m => m.MemberNo == memberNo);
                    if (member != null)
                    {
                        dashboard.MemberShareBalance = await _context.Shares
                            .Where(s => s.MemberNo == memberNo)
                            .SumAsync(s => s.TotalShares ?? 0);

                        dashboard.MemberTotalLoans = await _context.Loans
                            .Where(l => l.MemberNo == memberNo && l.Status == 1)
                            .SumAsync(l => l.LoanAmt ?? 0);

                        dashboard.MemberRecentTransactionCount = await _context.Transactions2
                            .CountAsync(t => t.MemberNo == memberNo && t.ContributionDate.Date == DateTime.Today);
                    }
                }

                dashboard.RecentTransactions = await GetRecentTransactions(companyCode, isSuperAdmin);
                dashboard.QuickStats = await GetQuickStats(companyCode, isSuperAdmin);

                return dashboard;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting universal dashboard data");
                return dashboard;
            }
        }

        private async Task<List<RecentTransaction>> GetRecentTransactions(string? companyCode, bool isSuperAdmin)
        {
            try
            {
                var transactionsQuery = _context.Transactions2
                    .Where(t => t.Status == "COMPLETED")
                    .OrderByDescending(t => t.ContributionDate)
                    .Take(10);

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                    }
                    else
                    {
                        return new List<RecentTransaction>();
                    }
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                    }
                    else
                    {
                        return new List<RecentTransaction>();
                    }
                }
                // If SuperAdmin and no companyCode, include ALL transactions

                var recentTransactions = await transactionsQuery.ToListAsync();
                var result = new List<RecentTransaction>();

                foreach (var tx in recentTransactions)
                {
                    var member = await _context.Members
                        .FirstOrDefaultAsync(m => m.MemberNo == tx.MemberNo);

                    result.Add(new RecentTransaction
                    {
                        TransactionId = tx.TransactionNo,
                        MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "Unknown",
                        Type = tx.TransactionType,
                        Amount = tx.Amount,
                        Date = tx.ContributionDate,
                        Status = tx.Status,
                        BlockchainTxId = tx.BlockchainTxId ?? "Pending"
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent transactions");
                return new List<RecentTransaction>();
            }
        }

        private async Task<DashboardQuickStats> GetQuickStats(string? companyCode, bool isSuperAdmin)
        {
            var today = DateTime.Today;
            var stats = new DashboardQuickStats();

            try
            {
                var transactionsQuery = _context.Transactions2
                    .Where(t => t.ContributionDate.Date == today && t.Status == "COMPLETED");

                var membersQuery = _context.Members.AsQueryable();
                var depositsQuery = _context.Transactions2
                    .Where(t => t.TransactionType == "DEPOSIT" && t.Status == "COMPLETED");
                var loansQuery = _context.Loans.Where(l => l.Status == 1);

                // Apply company filter based on role
                if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                        membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                        depositsQuery = depositsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                        loansQuery = loansQuery.Where(l => membersInCompany.Contains(l.MemberNo));
                    }
                    else
                    {
                        return stats;
                    }
                }
                else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                        membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                        depositsQuery = depositsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                        loansQuery = loansQuery.Where(l => membersInCompany.Contains(l.MemberNo));
                    }
                    else
                    {
                        return stats;
                    }
                }
                // If SuperAdmin and no companyCode, include ALL data

                stats.TransactionsToday = await transactionsQuery.CountAsync();
                stats.NewMembersToday = await membersQuery
                    .CountAsync(m => m.EffectDate.HasValue && m.EffectDate.Value.Date == today);
                stats.AverageDeposit = await depositsQuery.AverageAsync(t => t.Amount);
                stats.AverageLoan = await loansQuery.AverageAsync(l => l.LoanAmt ?? 0);
                stats.BlockchainUptime = 99.9m;

                var totalLoans = await loansQuery.CountAsync();
                var approvedLoans = await loansQuery.CountAsync(l => l.Status == 1);
                stats.LoanApprovalRate = totalLoans > 0 ? (approvedLoans * 100m / totalLoans) : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting quick stats");
            }

            return stats;
        }
        private async Task<List<MonthlyTransactionData>> GetMonthlyTransactionsDataAsync(int months, string? companyCode, bool isSuperAdmin)
        {
            var data = new List<MonthlyTransactionData>();
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-months + 1);
            startDate = new DateTime(startDate.Year, startDate.Month, 1);

            try
            {
                for (int i = 0; i < months; i++)
                {
                    var monthDate = startDate.AddMonths(i);
                    var monthName = monthDate.ToString("MMM yyyy");

                    var startOfMonth = new DateTime(monthDate.Year, monthDate.Month, 1);
                    var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

                    var transactionsQuery = _context.Transactions2
                        .Where(t => t.Status == "COMPLETED" &&
                                   t.ContributionDate >= startOfMonth &&
                                   t.ContributionDate <= endOfMonth);

                    // Apply company filter based on role
                    if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                    {
                        var memberNos = await _context.Members
                            .Where(m => m.CompanyCode == companyCode)
                            .Select(m => m.MemberNo)
                            .ToListAsync();
                        if (memberNos.Any())
                        {
                            transactionsQuery = transactionsQuery.Where(t => memberNos.Contains(t.MemberNo));
                        }
                        else
                        {
                            transactionsQuery = transactionsQuery.Where(t => false);
                        }
                    }
                    else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                    {
                        var memberNos = await _context.Members
                            .Where(m => m.CompanyCode == companyCode)
                            .Select(m => m.MemberNo)
                            .ToListAsync();
                        if (memberNos.Any())
                        {
                            transactionsQuery = transactionsQuery.Where(t => memberNos.Contains(t.MemberNo));
                        }
                        else
                        {
                            transactionsQuery = transactionsQuery.Where(t => false);
                        }
                    }

                    var deposits = await transactionsQuery
                        .Where(t => t.TransactionType == "DEPOSIT" || t.TransactionType == "CONTRIBUTION")
                        .SumAsync(t => (decimal?)t.Amount) ?? 0;

                    var withdrawals = await transactionsQuery
                        .Where(t => t.TransactionType == "WITHDRAWAL")
                        .SumAsync(t => (decimal?)t.Amount) ?? 0;

                    // Get loan repayments from Repay table
                    decimal loanRepayments = 0;
                    if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                    {
                        var memberNos = await _context.Members
                            .Where(m => m.CompanyCode == companyCode)
                            .Select(m => m.MemberNo)
                            .ToListAsync();

                        if (memberNos.Any())
                        {
                            loanRepayments = await _context.Repay
                                .Where(r => r.DateReceived.HasValue &&
                                           r.DateReceived.Value >= startOfMonth &&
                                           r.DateReceived.Value <= endOfMonth &&
                                           memberNos.Contains(r.MemberNo))
                                .SumAsync(r => (decimal?)r.Amount) ?? 0;
                        }
                    }
                    else
                    {
                        loanRepayments = await _context.Repay
                            .Where(r => r.DateReceived.HasValue &&
                                       r.DateReceived.Value >= startOfMonth &&
                                       r.DateReceived.Value <= endOfMonth)
                            .SumAsync(r => (decimal?)r.Amount) ?? 0;
                    }

                    data.Add(new MonthlyTransactionData
                    {
                        Month = monthName,
                        Deposits = deposits,
                        Withdrawals = withdrawals,
                        LoanRepayments = loanRepayments
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetMonthlyTransactionsDataAsync");
            }

            return data;
        }

        //private async Task<List<MonthlyTransactionData>> GetMonthlyTransactionsDataAsync(int months, string? companyCode, bool isSuperAdmin)
        //{
        //    var data = new List<MonthlyTransactionData>();
        //    var endDate = DateTime.Now;
        //    var startDate = endDate.AddMonths(-months);

        //    try
        //    {
        //        for (int i = 0; i < months; i++)
        //        {
        //            var monthDate = startDate.AddMonths(i);
        //            var monthName = monthDate.ToString("MMM yyyy");

        //            var transactionsQuery = _context.Transactions2
        //                .Where(t => t.Status == "COMPLETED" &&
        //                           t.ContributionDate.Month == monthDate.Month &&
        //                           t.ContributionDate.Year == monthDate.Year);

        //            // Apply company filter based on role
        //            if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
        //            {
        //                var membersInCompany = await _context.Members
        //                    .Where(m => m.CompanyCode == companyCode)
        //                    .Select(m => m.MemberNo)
        //                    .ToListAsync();
        //                if (membersInCompany.Any())
        //                {
        //                    transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
        //                }
        //                else
        //                {
        //                    transactionsQuery = transactionsQuery.Where(t => false);
        //                }
        //            }
        //            else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
        //            {
        //                var membersInCompany = await _context.Members
        //                    .Where(m => m.CompanyCode == companyCode)
        //                    .Select(m => m.MemberNo)
        //                    .ToListAsync();
        //                if (membersInCompany.Any())
        //                {
        //                    transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
        //                }
        //                else
        //                {
        //                    transactionsQuery = transactionsQuery.Where(t => false);
        //                }
        //            }
        //            // If SuperAdmin and no companyCode, include ALL transactions

        //            var deposits = await transactionsQuery
        //                .Where(t => t.TransactionType == "DEPOSIT")
        //                .SumAsync(t => (decimal?)t.Amount) ?? 0;

        //            var withdrawals = await transactionsQuery
        //                .Where(t => t.TransactionType == "WITHDRAWAL")
        //                .SumAsync(t => (decimal?)t.Amount) ?? 0;

        //            data.Add(new MonthlyTransactionData
        //            {
        //                Month = monthName,
        //                Deposits = deposits,
        //                Withdrawals = withdrawals,
        //                LoanRepayments = 0
        //            });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error in GetMonthlyTransactionsDataAsync");
        //    }

        //    return data;
        //}

        private async Task<List<MemberGrowthData>> GetMemberGrowthDataAsync(int months, string? companyCode, bool isSuperAdmin)
        {
            var data = new List<MemberGrowthData>();
            var endDate = DateTime.Now;
            var startDate = endDate.AddMonths(-months);

            try
            {
                for (int i = 0; i < months; i++)
                {
                    var monthDate = startDate.AddMonths(i);
                    var monthName = monthDate.ToString("MMM yyyy");

                    var membersQuery = _context.Members.AsQueryable();

                    // Apply company filter based on role
                    if (!isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                    {
                        membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                    }
                    else if (isSuperAdmin && !string.IsNullOrEmpty(companyCode))
                    {
                        membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                    }
                    // If SuperAdmin and no companyCode, include ALL members

                    var newMembers = await membersQuery
                        .CountAsync(m => m.EffectDate.HasValue &&
                                        m.EffectDate.Value.Month == monthDate.Month &&
                                        m.EffectDate.Value.Year == monthDate.Year);

                    var totalMembers = await membersQuery
                        .CountAsync(m => m.EffectDate.HasValue &&
                                        m.EffectDate.Value <= monthDate);

                    data.Add(new MemberGrowthData
                    {
                        Period = monthName,
                        NewMembers = newMembers,
                        TotalMembers = totalMembers
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetMemberGrowthDataAsync");
            }

            return data;
        }

        private int CalculateAgeSafe(DateTime birthDate)
        {
            try
            {
                var today = DateTime.Today;
                var age = today.Year - birthDate.Year;
                if (birthDate.Date > today.AddYears(-age)) age--;
                return age;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<List<RecentTransaction>> GetRecentTransactions(string? companyCode = null)
        {
            try
            {
                var transactionsQuery = _context.Transactions2
                    .Where(t => t.Status == "COMPLETED")
                    .OrderByDescending(t => t.ContributionDate)
                    .Take(10);

                if (!string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                    }
                    else
                    {
                        return new List<RecentTransaction>();
                    }
                }

                var recentTransactions = await transactionsQuery.ToListAsync();
                var result = new List<RecentTransaction>();

                foreach (var tx in recentTransactions)
                {
                    var member = await _context.Members
                        .FirstOrDefaultAsync(m => m.MemberNo == tx.MemberNo);

                    result.Add(new RecentTransaction
                    {
                        TransactionId = tx.TransactionNo,
                        MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : "Unknown",
                        Type = tx.TransactionType,
                        Amount = tx.Amount,
                        Date = tx.ContributionDate,
                        Status = tx.Status,
                        BlockchainTxId = tx.BlockchainTxId ?? "Pending"
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent transactions");
                return new List<RecentTransaction>();
            }
        }

        private async Task<DashboardQuickStats> GetQuickStats(string? companyCode = null)
        {
            var today = DateTime.Today;
            var stats = new DashboardQuickStats();

            try
            {
                var transactionsQuery = _context.Transactions2
                    .Where(t => t.ContributionDate.Date == today && t.Status == "COMPLETED");

                var membersQuery = _context.Members.AsQueryable();
                var depositsQuery = _context.Transactions2
                    .Where(t => t.TransactionType == "DEPOSIT" && t.Status == "COMPLETED");
                var loansQuery = _context.Loans.Where(l => l.Status == 1);

                if (!string.IsNullOrEmpty(companyCode))
                {
                    var membersInCompany = await _context.Members
                        .Where(m => m.CompanyCode == companyCode)
                        .Select(m => m.MemberNo)
                        .ToListAsync();

                    if (membersInCompany.Any())
                    {
                        transactionsQuery = transactionsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                        membersQuery = membersQuery.Where(m => m.CompanyCode == companyCode);
                        depositsQuery = depositsQuery.Where(t => membersInCompany.Contains(t.MemberNo));
                        loansQuery = loansQuery.Where(l => membersInCompany.Contains(l.MemberNo));
                    }
                    else
                    {
                        return stats;
                    }
                }

                stats.TransactionsToday = await transactionsQuery.CountAsync();
                stats.NewMembersToday = await membersQuery
                    .CountAsync(m => m.EffectDate.HasValue && m.EffectDate.Value.Date == today);
                stats.AverageDeposit = await depositsQuery.AverageAsync(t => t.Amount);
                stats.AverageLoan = await loansQuery.AverageAsync(l => l.LoanAmt ?? 0);
                stats.BlockchainUptime = 99.9m;

                var totalLoans = await loansQuery.CountAsync();
                var approvedLoans = await loansQuery.CountAsync(l => l.Status == 1);
                stats.LoanApprovalRate = totalLoans > 0 ? (approvedLoans * 100m / totalLoans) : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting quick stats");
            }

            return stats;
        }

        // method to get chart data via AJAX
        [HttpGet]
        public async Task<IActionResult> GetChartData(string range = "6m", string? companyCode = null)
        {
            try
            {
                // Get user role for filtering
                var userRole = User.FindFirstValue(ClaimTypes.Role);
                var isSuperAdmin = userRole == "Super Admin" || userRole == "SuperAdmin";
                var userCompanyCode = User.FindFirst("CompanyCode")?.Value;

                // Determine effective company code
                string effectiveCompanyCode = null;
                if (isSuperAdmin)
                {
                    effectiveCompanyCode = string.IsNullOrEmpty(companyCode) ? null : companyCode;
                }
                else
                {
                    effectiveCompanyCode = userCompanyCode;
                }

                int months = range == "1y" ? 12 : 6;

                var monthlyTransactions = await GetMonthlyTransactionsDataAsync(months, effectiveCompanyCode, isSuperAdmin);
                var memberGrowth = await GetMemberGrowthDataAsync(months, effectiveCompanyCode, isSuperAdmin);

                return Json(new
                {
                    success = true,
                    monthlyTransactions = monthlyTransactions.Select(m => new
                    {
                        month = m.Month,
                        deposits = m.Deposits,
                        withdrawals = m.Withdrawals,
                        loanRepayments = m.LoanRepayments
                    }),
                    memberGrowth = memberGrowth.Select(m => new
                    {
                        period = m.Period,
                        newMembers = m.NewMembers,
                        totalMembers = m.TotalMembers
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chart data");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GetUserGroup()
        {
            if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")) return "Admin";
            if (User.IsInRole("Teller")) return "Teller";
            if (User.IsInRole("LoanOfficer")) return "Loan Officer";
            if (User.IsInRole("Auditor")) return "Auditor";
            if (User.IsInRole("BoardMember")) return "Board Member";

            var memberNo = User.FindFirst("MemberNo")?.Value;
            return !string.IsNullOrEmpty(memberNo) ? "Member" : "Guest";
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardStats(string? companyCode = null)
        {
            try
            {
                // Get the logged-in user's role and company code from claims
                var userRole = User.FindFirstValue(ClaimTypes.Role);
                var isSuperAdmin = userRole == "Super Admin" || userRole == "SuperAdmin";
                var userCompanyCode = User.FindFirst("CompanyCode")?.Value ??
                                      User.FindFirst("SaccoCode")?.Value ??
                                      User.FindFirst("Company")?.Value;

                // Determine the effective company code for filtering
                string effectiveCompanyCode = null;

                if (isSuperAdmin)
                {
                    // Super Admin: Use selected company code if provided, otherwise null (show all)
                    effectiveCompanyCode = string.IsNullOrEmpty(companyCode) ? null : companyCode;
                }
                else
                {
                    // Non-SuperAdmin: Always limited to their own company
                    effectiveCompanyCode = userCompanyCode;
                }

                // Get dashboard data with role-based filtering
                var dashboard = await GetUniversalDashboardDataAsync(effectiveCompanyCode, isSuperAdmin);

                // Get additional financial data for the dashboard stats
                var contributionsData = await GetContributionsDataAsync(effectiveCompanyCode, isSuperAdmin);
                var shareCapitalData = await GetShareCapitalDataAsync(effectiveCompanyCode, isSuperAdmin);
                var loansTakenData = await GetLoansTakenDataAsync(effectiveCompanyCode, isSuperAdmin);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        totalMembers = dashboard.TotalMembers,
                        totalShareCapital = shareCapitalData.Total,
                        totalContributions = contributionsData.Total,
                        totalLoans = loansTakenData.Total,
                        blockchainTransactions = dashboard.TotalBlockchainTransactions,
                        activeMembers = dashboard.ActiveMembers,
                        totalWomen = dashboard.TotalWomen,
                        totalMen = dashboard.TotalMen,
                        youthTotal = dashboard.YouthTotal,
                        quickStats = dashboard.QuickStats,
                        // Include company filter info for UI
                        isSuperAdmin = isSuperAdmin,
                        selectedCompanyCode = effectiveCompanyCode ?? (isSuperAdmin ? "ALL" : userCompanyCode),
                        selectedCompanyName = isSuperAdmin && string.IsNullOrEmpty(effectiveCompanyCode) ? "All Companies" : dashboard.SelectedCompanyName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard stats");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var errorViewModel = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };

            return View(errorViewModel);
        }
    }
}


