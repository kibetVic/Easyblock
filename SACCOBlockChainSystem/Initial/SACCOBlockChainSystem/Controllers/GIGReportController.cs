using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using SACCOBlockChainSystem.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Controllers
{
	[Authorize]
	public class GIGReportController : Controller
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<GIGReportController> _logger;
		private readonly ICompanyContextService _companyContextService;

		public GIGReportController(
			ApplicationDbContext context,
			ILogger<GIGReportController> logger,
			ICompanyContextService companyContextService)
		{
			_context = context;
			_logger = logger;
			_companyContextService = companyContextService;
		}

		[HttpGet]
		public IActionResult Index()
		{
			var companyCode = _companyContextService.GetCurrentCompanyCode();
			// FIXED: Get company name from the Companies table using the company code
			var companyName = GetCompanyNameFromCode(companyCode) ?? "";
			var endDate = DateTime.Now;
			var startDate = endDate.AddMonths(-1);

			var viewModel = new GIGReportIndexViewModel
			{
				GIGs = new List<GIGReportViewModel>(),
				StartDate = startDate,
				EndDate = endDate,
				HasData = false,
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				TotalGIGs = 0,
				TotalGIGMembers = 0,
				TotalMaleMembers = 0,
				TotalFemaleMembers = 0,
				TotalYouthMembers = 0,
				TotalShareCapitalAllGIGs = 0,
				TotalShareDepositsAllGIGs = 0,
				TotalRegFeeAllGIGs = 0,
				TotalLoansAllGIGs = 0
			};

			ViewBag.StartDate = startDate;
			ViewBag.EndDate = endDate;
			ViewBag.CompanyName = companyName;

			return View("~/Views/Reports/GIGReport.cshtml", viewModel);
		}

        [HttpPost]
        public async Task<IActionResult> GenerateReport(DateTime startDate, DateTime endDate)
        {
            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var companyName = GetCompanyNameFromCode(companyCode) ?? "";
                var endDateAdjusted = endDate.Date.AddDays(1).AddSeconds(-1);

                var gigs = await _context.CIGs
                    .Where(g => g.CompanyCode == companyCode && g.Status == "Active")
                    .OrderBy(g => g.GigName)
                    .ToListAsync();

                var gigReports = new List<GIGReportViewModel>();
                int totalGIGs = 0, totalGIGMembers = 0, totalMaleMembers = 0, totalFemaleMembers = 0, totalYouthMembers = 0;
                decimal totalShareCapitalAllGIGs = 0, totalShareDepositsAllGIGs = 0, totalRegFeeAllGIGs = 0, totalLoansAllGIGs = 0;

                foreach (var gig in gigs)
                {
                    var members = await _context.Members
                        .Where(m => m.CompanyCode == companyCode && m.Cigcode == gig.GigCode
                            && (m.Withdrawn == false || m.Withdrawn == null)
                            && (m.Archived == false || m.Archived == null))
                        .ToListAsync();

                    if (!members.Any()) continue;

                    var memberNos = members.Select(m => m.MemberNo.Trim()).ToList();

                    // Get contributions from ContribShare table
                    var contributions = await _context.ContribShares
                        .Where(c => memberNos.Contains(c.MemberNo.Trim())
                            && c.CompanyCode == companyCode
                            && c.ContrDate >= startDate
                            && c.ContrDate <= endDateAdjusted)
                        .GroupBy(c => c.MemberNo.Trim())
                        .Select(g => new
                        {
                            MemberNo = g.Key,
                            TotalShareCapital = g.Sum(c => c.ShareCapitalAmount ?? 0),
                            TotalDeposits = g.Sum(c => c.DepositsAmount ?? 0),
                            TotalRegFee = g.Sum(c => c.RegFeeAmount ?? 0)
                        })
                        .ToDictionaryAsync(c => c.MemberNo, c => new
                        {
                            c.TotalShareCapital,
                            c.TotalDeposits,
                            c.TotalRegFee
                        });

                    // IMPORTANT FIX: Get SUM of AmtRecommended from Appraisal for each member
                    // This handles multiple loans per member by summing all AmtRecommended
                    var recommendedLoans = await _context.Appraisal
                        .Where(a => memberNos.Contains(a.MemberNo.Trim())
                            && a.CompanyCode == companyCode
                            && a.AmtRecommended.HasValue
                            && a.AmtRecommended > 0)
                        .GroupBy(a => a.MemberNo.Trim())
                        .Select(g => new
                        {
                            MemberNo = g.Key,
                            TotalAmtRecommended = g.Sum(a => a.AmtRecommended ?? 0)
                        })
                        .ToDictionaryAsync(a => a.MemberNo, a => a.TotalAmtRecommended);

                    var memberDetails = new List<GIGReportMemberDetail>();
                    int maleCount = 0, femaleCount = 0, youthCount = 0;
                    decimal totalShareCapital = 0, totalShareDepositAmount = 0, totalRegFeeAmount = 0, totalLoanAmount = 0;

                    foreach (var member in members)
                    {
                        string trimmedMemberNo = member.MemberNo?.Trim() ?? "";

                        string fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
                        if (string.IsNullOrWhiteSpace(fullName)) fullName = "N/A";

                        int? age = null;
                        if (member.Dob.HasValue)
                        {
                            age = DateTime.Today.Year - member.Dob.Value.Year;
                            if (member.Dob.Value.Date > DateTime.Today.AddYears(-age.Value)) age--;
                            if (age >= 18 && age <= 35) youthCount++;
                        }

                        if (member.Sex?.ToUpper() == "MALE" || member.Sex?.ToUpper() == "M") maleCount++;
                        else if (member.Sex?.ToUpper() == "FEMALE" || member.Sex?.ToUpper() == "F") femaleCount++;

                        // Get values from ContribShare
                        decimal shareCapital = contributions.ContainsKey(trimmedMemberNo)
                            ? contributions[trimmedMemberNo].TotalShareCapital : (member.ShareCap ?? 0);

                        decimal shareDeposit = contributions.ContainsKey(trimmedMemberNo)
                            ? contributions[trimmedMemberNo].TotalDeposits : 0;

                        decimal regFee = contributions.ContainsKey(trimmedMemberNo)
                            ? contributions[trimmedMemberNo].TotalRegFee : (member.RegFee ?? 0);

                        // IMPORTANT FIX: Get SUM of AmtRecommended from Appraisal
                        decimal recommendedLoanAmt = recommendedLoans.ContainsKey(trimmedMemberNo)
                            ? recommendedLoans[trimmedMemberNo] : 0;

                        // Debug logging to see what's happening
                        if (recommendedLoanAmt > 0)
                        {
                            _logger.LogInformation($"Member {trimmedMemberNo} has recommended loan amount: {recommendedLoanAmt}");
                        }
                        else
                        {
                            // Check if member has appraisals but maybe AmtRecommended is null
                            var hasAppraisal = await _context.Appraisal
                                .AnyAsync(a => a.MemberNo.Trim() == trimmedMemberNo && a.CompanyCode == companyCode);
                            if (hasAppraisal)
                            {
                                _logger.LogWarning($"Member {trimmedMemberNo} has appraisal record but AmtRecommended is null or zero");
                            }
                        }

                        totalShareCapital += shareCapital;
                        totalShareDepositAmount += shareDeposit;
                        totalRegFeeAmount += regFee;
                        totalLoanAmount += recommendedLoanAmt; // Using recommended loan amount for totals

                        memberDetails.Add(new GIGReportMemberDetail
                        {
                            MemberNo = member.MemberNo,
                            Names = fullName,
                            Sex = member.Sex ?? "Not Specified",
                            PhoneNo = member.PhoneNo ?? member.MobileNo ?? "-",
                            IDNo = member.Idno ?? "-",
                            Age = age,
                            CIGCode = gig.GigCode,
                            CIGName = gig.GigName,
                            ShareCapital = shareCapital,
                            ShareDeposits = shareDeposit,
                            RegFee = regFee,
                            LoanAmt = recommendedLoanAmt // This is the AmtRecommended sum
                        });
                    }

                    totalGIGs++;
                    totalGIGMembers += memberDetails.Count;
                    totalMaleMembers += maleCount;
                    totalFemaleMembers += femaleCount;
                    totalYouthMembers += youthCount;
                    totalShareCapitalAllGIGs += totalShareCapital;
                    totalShareDepositsAllGIGs += totalShareDepositAmount;
                    totalRegFeeAllGIGs += totalRegFeeAmount;
                    totalLoansAllGIGs += totalLoanAmount;

                    gigReports.Add(new GIGReportViewModel
                    {
                        CIGCode = gig.GigCode,
                        CIGName = gig.GigName,
                        TotalMembers = memberDetails.Count,
                        MaleCount = maleCount,
                        FemaleCount = femaleCount,
                        YouthCount = youthCount,
                        TotalShareCapital = totalShareCapital,
                        TotalShareDeposits = totalShareDepositAmount,
                        TotalRegFee = totalRegFeeAmount,
                        TotalLoans = totalLoanAmount, // This is the sum of recommended loan amounts
                        Members = memberDetails,
                        CompanyCode = companyCode,
                        CompanyName = companyName
                    });
                }

                var viewModel = new GIGReportIndexViewModel
                {
                    GIGs = gigReports,
                    StartDate = startDate,
                    EndDate = endDate,
                    HasData = gigReports.Any(),
                    UserCompanyCode = companyCode,
                    CompanyName = companyName,
                    TotalGIGs = totalGIGs,
                    TotalGIGMembers = totalGIGMembers,
                    TotalMaleMembers = totalMaleMembers,
                    TotalFemaleMembers = totalFemaleMembers,
                    TotalYouthMembers = totalYouthMembers,
                    TotalShareCapitalAllGIGs = totalShareCapitalAllGIGs,
                    TotalShareDepositsAllGIGs = totalShareDepositsAllGIGs,
                    TotalRegFeeAllGIGs = totalRegFeeAllGIGs,
                    TotalLoansAllGIGs = totalLoansAllGIGs
                };

                return View("~/Views/Reports/GIGReport.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating GIG report");
                TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        //      [HttpPost]
        //public async Task<IActionResult> GenerateReport(DateTime startDate, DateTime endDate)
        //{
        //	try
        //	{
        //		var companyCode = _companyContextService.GetCurrentCompanyCode();
        //		var companyName = GetCompanyNameFromCode(companyCode) ?? "";
        //		var endDateAdjusted = endDate.Date.AddDays(1).AddSeconds(-1);

        //		var gigs = await _context.CIGs
        //			.Where(g => g.CompanyCode == companyCode && g.Status == "Active")
        //			.OrderBy(g => g.GigName)
        //			.ToListAsync();

        //		var gigReports = new List<GIGReportViewModel>();
        //		int totalGIGs = 0, totalGIGMembers = 0, totalMaleMembers = 0, totalFemaleMembers = 0, totalYouthMembers = 0;
        //		decimal totalShareCapitalAllGIGs = 0, totalShareDepositsAllGIGs = 0, totalRegFeeAllGIGs = 0, totalLoansAllGIGs = 0;

        //		foreach (var gig in gigs)
        //		{
        //			var members = await _context.Members
        //				.Where(m => m.CompanyCode == companyCode && m.Cigcode == gig.GigCode
        //					&& (m.Withdrawn == false || m.Withdrawn == null)
        //					&& (m.Archived == false || m.Archived == null))
        //				.ToListAsync();

        //			if (!members.Any()) continue;

        //			var memberNos = members.Select(m => m.MemberNo).ToList();

        //			// Get contributions from ContribShare table (summarized by member)
        //			var contributions = await _context.ContribShares
        //				.Where(c => memberNos.Contains(c.MemberNo)
        //					&& c.CompanyCode == companyCode
        //					&& c.ContrDate >= startDate
        //					&& c.ContrDate <= endDateAdjusted)
        //				.GroupBy(c => c.MemberNo)
        //				.Select(g => new
        //				{
        //					MemberNo = g.Key,
        //					TotalShareCapital = g.Sum(c => c.ShareCapitalAmount ?? 0),
        //					TotalDeposits = g.Sum(c => c.DepositsAmount ?? 0),
        //					TotalRegFee = g.Sum(c => c.RegFeeAmount ?? 0)
        //				})
        //				.ToDictionaryAsync(c => c.MemberNo, c => new
        //				{
        //					c.TotalShareCapital,
        //					c.TotalDeposits,
        //					c.TotalRegFee
        //				});

        //			// Get approved loan amounts from ENDMAIN table (sum all approved loans for each member)
        //			var approvedLoans = await _context.Endmain
        //				.Where(el => memberNos.Contains(el.LoanNo)
        //					&& el.CompanyCode == companyCode
        //					&& el.Accepted == "Yes"  // Only approved loans
        //					&& el.AmtApproved > 0)
        //				.GroupBy(el => el.LoanNo)  // LoanNo is the member number
        //				.Select(g => new { MemberNo = g.Key, TotalApproved = g.Sum(el => el.AmtApproved) })
        //				.ToDictionaryAsync(el => el.MemberNo, el => el.TotalApproved);

        //			var memberDetails = new List<GIGReportMemberDetail>();
        //			int maleCount = 0, femaleCount = 0, youthCount = 0;
        //			decimal totalShareCapital = 0, totalShareDepositAmount = 0, totalRegFeeAmount = 0, totalLoanAmount = 0;

        //                  // Get the most recent appraisal AmtRecommended for each member
        //                  var recommendedLoans = await _context.Appraisal
        //                      .Where(a => memberNos.Contains(a.MemberNo)
        //                          && a.CompanyCode == companyCode)
        //                      .GroupBy(a => a.MemberNo)
        //                      .Select(g => new {
        //                          MemberNo = g.Key,
        //                          AmtRecommended = g.OrderByDescending(a => a.AppraisDate).FirstOrDefault().AmtRecommended ?? 0
        //                      })
        //                      .ToDictionaryAsync(a => a.MemberNo, a => a.AmtRecommended);

        //                  foreach (var member in members)
        //			{
        //				string fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
        //				if (string.IsNullOrWhiteSpace(fullName)) fullName = "N/A";

        //				int? age = null;
        //				if (member.Dob.HasValue)
        //				{
        //					age = DateTime.Today.Year - member.Dob.Value.Year;
        //					if (member.Dob.Value.Date > DateTime.Today.AddYears(-age.Value)) age--;
        //					if (age >= 18 && age <= 35) youthCount++;
        //				}

        //				if (member.Sex?.ToUpper() == "MALE" || member.Sex?.ToUpper() == "M") maleCount++;
        //				else if (member.Sex?.ToUpper() == "FEMALE" || member.Sex?.ToUpper() == "F") femaleCount++;

        //				// Get values from ContribShare or fallback to Member table
        //				decimal shareCapital = contributions.ContainsKey(member.MemberNo)
        //					? contributions[member.MemberNo].TotalShareCapital : (member.ShareCap ?? 0);

        //				decimal shareDeposit = contributions.ContainsKey(member.MemberNo)
        //					? contributions[member.MemberNo].TotalDeposits : 0;

        //				decimal regFee = contributions.ContainsKey(member.MemberNo)
        //					? contributions[member.MemberNo].TotalRegFee : (member.RegFee ?? 0);

        //				// Get total approved loans for this member (sum of all approved loan amounts)
        //				decimal totalApprovedLoans = approvedLoans.ContainsKey(member.MemberNo)
        //					? approvedLoans[member.MemberNo] : 0;

        //                      decimal recommendedLoanAmt = recommendedLoans.ContainsKey(member.MemberNo)
        //                          ? recommendedLoans[member.MemberNo] : 0;

        //                      totalShareCapital += shareCapital;
        //				totalShareDepositAmount += shareDeposit;
        //				totalRegFeeAmount += regFee;
        //                      //totalLoanAmount += totalApprovedLoans;
        //                      totalLoanAmount += recommendedLoanAmt;

        //                      memberDetails.Add(new GIGReportMemberDetail
        //				{
        //					MemberNo = member.MemberNo,
        //					Names = fullName,
        //					Sex = member.Sex ?? "Not Specified",
        //					PhoneNo = member.PhoneNo ?? member.MobileNo ?? "-",
        //					IDNo = member.Idno ?? "-",
        //					Age = age,
        //					CIGCode = gig.GigCode,
        //					CIGName = gig.GigName,
        //					ShareCapital = shareCapital,
        //					ShareDeposits = shareDeposit,
        //					RegFee = regFee,
        //					LoanAmt = totalApprovedLoans,
        //                          RecommendedLoanAmt = recommendedLoanAmt
        //                      });
        //			}

        //			totalGIGs++;
        //			totalGIGMembers += memberDetails.Count;
        //			totalMaleMembers += maleCount;
        //			totalFemaleMembers += femaleCount;
        //			totalYouthMembers += youthCount;
        //			totalShareCapitalAllGIGs += totalShareCapital;
        //			totalShareDepositsAllGIGs += totalShareDepositAmount;
        //			totalRegFeeAllGIGs += totalRegFeeAmount;
        //			totalLoansAllGIGs += totalLoanAmount;

        //			gigReports.Add(new GIGReportViewModel
        //			{
        //				CIGCode = gig.GigCode,
        //				CIGName = gig.GigName,
        //				TotalMembers = memberDetails.Count,
        //				MaleCount = maleCount,
        //				FemaleCount = femaleCount,
        //				YouthCount = youthCount,
        //				TotalShareCapital = totalShareCapital,
        //				TotalShareDeposits = totalShareDepositAmount,
        //				TotalRegFee = totalRegFeeAmount,
        //				TotalLoans = totalLoanAmount,
        //				Members = memberDetails,
        //				CompanyCode = companyCode,
        //				CompanyName = companyName
        //			});
        //		}

        //		var viewModel = new GIGReportIndexViewModel
        //		{
        //			GIGs = gigReports,
        //			StartDate = startDate,
        //			EndDate = endDate,
        //			HasData = gigReports.Any(),
        //			UserCompanyCode = companyCode,
        //			CompanyName = companyName,
        //			TotalGIGs = totalGIGs,
        //			TotalGIGMembers = totalGIGMembers,
        //			TotalMaleMembers = totalMaleMembers,
        //			TotalFemaleMembers = totalFemaleMembers,
        //			TotalYouthMembers = totalYouthMembers,
        //			TotalShareCapitalAllGIGs = totalShareCapitalAllGIGs,
        //			TotalShareDepositsAllGIGs = totalShareDepositsAllGIGs,
        //			TotalRegFeeAllGIGs = totalRegFeeAllGIGs,
        //			TotalLoansAllGIGs = totalLoansAllGIGs
        //		};

        //		return View("~/Views/Reports/GIGReport.cshtml", viewModel);
        //	}
        //	catch (Exception ex)
        //	{
        //		_logger.LogError(ex, "Error generating GIG report");
        //		TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
        //		return RedirectToAction("Index");
        //	}
        //}

        // helper method that works with your actual Share and Sharetype models
        private string DetermineContributionTypeFromShareAndType(Sharetype shareType, Share share)
		{
			// Get searchable text from ShareType
			var shareTypeName = (shareType.SharesType ?? shareType.SharesCode ?? "").ToLower();
			var shareTypeCode = (shareType.SharesCode ?? "").ToLower();

			// Check for DEPOSIT/SAVINGS keywords
			string[] depositKeywords = { "deposit", "savings", "saving", "deposits", "share deposit", "voluntary", "welfare" };
			foreach (var keyword in depositKeywords)
			{
				if (shareTypeName.Contains(keyword))
					return "DEPOSIT";
			}

			// Check for REGISTRATION FEE keywords
			string[] regFeeKeywords = { "reg fee", "fee", "registration", "registration fee", "entry fee", "joining fee", "membership fee" };
			foreach (var keyword in regFeeKeywords)
			{
				if (shareTypeName.Contains(keyword))
					return "REGISTRATION_FEE";
			}

			// Check for SHARE CAPITAL keywords
			string[] shareCapitalKeywords = { "share capital", "share", "shares", "capital", "main shares", "equity" };
			foreach (var keyword in shareCapitalKeywords)
			{
				if (shareTypeName.Contains(keyword))
					return "SHARE_CAPITAL";
			}

			// Check ShareType code as fallback
			foreach (var keyword in depositKeywords)
			{
				if (shareTypeCode.Contains(keyword))
					return "DEPOSIT";
			}

			foreach (var keyword in regFeeKeywords)
			{
				if (shareTypeCode.Contains(keyword))
					return "REGISTRATION_FEE";
			}

			// Check boolean flags
			if (shareType.IsMainShares == true || shareType.Issharecapital == 1)
				return "SHARE_CAPITAL";

			if (shareType.Withdrawable == true && (shareType.UsedToGuarantee == true || shareType.UsedToOffset == true))
				return "DEPOSIT";

			if (shareType.Issharecapital == 0 && shareType.UsedToGuarantee == false &&
				shareType.UsedToOffset == false && shareType.Withdrawable == false)
				return "REGISTRATION_FEE";

			// Default
			return "SHARE_CAPITAL";
		}


		[HttpPost]
		public async Task<IActionResult> ExportToExcel(DateTime startDate, DateTime endDate)
		{
			try
			{
				var companyCode = _companyContextService.GetCurrentCompanyCode();
				// FIXED: Get company name from the Companies table
				var companyName = GetCompanyNameFromCode(companyCode) ?? "";
				var printedBy = User.Identity?.Name ?? "System";
				var endDateAdjusted = endDate.Date.AddDays(1).AddSeconds(-1);

				// Get all active GIGs for this company
				var gigs = await _context.CIGs
					.Where(g => g.CompanyCode == companyCode && g.Status == "Active")
					.OrderBy(g => g.GigName)
					.ToListAsync();

				using var workbook = new XLWorkbook();

				// Summary Sheet
				var summarySheet = workbook.Worksheets.Add("Summary");
				int summaryRow = 1;

				summarySheet.Cell(summaryRow, 1).Value = companyName.ToUpper();
				summarySheet.Range(summaryRow, 1, summaryRow, 10).Merge();
				summarySheet.Cell(summaryRow, 1).Style.Font.SetBold().Font.SetFontSize(18);
				summarySheet.Cell(summaryRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
				summaryRow += 2;

				summarySheet.Cell(summaryRow, 1).Value = $"Printed By: {printedBy} On: {DateTime.Now:dd-MMM-yyyy HH:mm}";
				summarySheet.Range(summaryRow, 1, summaryRow, 10).Merge();
				summarySheet.Cell(summaryRow, 1).Style.Font.SetItalic();
				summaryRow += 2;

				summarySheet.Cell(summaryRow, 1).Value = $"GIGs REPORT - {startDate:dd/MM/yyyy} to {endDate:dd/MM/yyyy}";
				summarySheet.Range(summaryRow, 1, summaryRow, 10).Merge();
				summarySheet.Cell(summaryRow, 1).Style.Font.SetBold().Font.SetFontSize(14);
				summarySheet.Cell(summaryRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
				summaryRow += 2;

				string[] summaryHeaders = { "GIG Code", "GIG Name", "Total Members", "Male", "Female", "Youth", "Share Capital", "Savings/Deposits", "Reg Fee", "Loans" };
				for (int i = 0; i < summaryHeaders.Length; i++)
				{
					summarySheet.Cell(summaryRow, i + 1).Value = summaryHeaders[i];
					summarySheet.Cell(summaryRow, i + 1).Style.Font.SetBold();
					summarySheet.Cell(summaryRow, i + 1).Style.Fill.SetBackgroundColor(XLColor.LightGray);
					summarySheet.Cell(summaryRow, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
				}
				summaryRow++;

				decimal grandTotalShareCapital = 0;
				decimal grandTotalSavings = 0;
				decimal grandTotalRegFee = 0;
				decimal grandTotalLoans = 0;
				int grandTotalMembers = 0;

				foreach (var gig in gigs)
				{
					// Get members belonging to this GIG
					var members = await _context.Members
						.Where(m => m.CompanyCode == companyCode
							&& m.Cigcode == gig.GigCode
							&& (m.Withdrawn == false || m.Withdrawn == null)
							&& (m.Archived == false || m.Archived == null))
						.ToListAsync();

					if (!members.Any()) continue;

					var memberNos = members.Select(m => m.MemberNo).ToList();

					// Get contributions from ContribShare table
					var contributions = await _context.ContribShares
						.Where(c => memberNos.Contains(c.MemberNo)
							&& c.CompanyCode == companyCode
							&& c.ContrDate >= startDate
							&& c.ContrDate <= endDateAdjusted)
						.GroupBy(c => c.MemberNo)
						.Select(g => new
						{
							MemberNo = g.Key,
							TotalShareCapital = g.Sum(c => c.ShareCapitalAmount ?? 0),
							TotalDeposits = g.Sum(c => c.DepositsAmount ?? 0),
							TotalRegFee = g.Sum(c => c.RegFeeAmount ?? 0),
							TotalLoanAmount = g.Sum(c => c.LoanAmount ?? 0)
						})
						.ToListAsync();

					int maleCount = members.Count(m => m.Sex?.ToUpper() == "MALE" || m.Sex?.ToUpper() == "M");
					int femaleCount = members.Count(m => m.Sex?.ToUpper() == "FEMALE" || m.Sex?.ToUpper() == "F");
					int youthCount = members.Count(m => m.Dob.HasValue && CalculateAge(m.Dob.Value) >= 18 && CalculateAge(m.Dob.Value) <= 35);

					decimal totalShareCapital = contributions.Sum(c => c.TotalShareCapital);
					decimal totalSavings = contributions.Sum(c => c.TotalDeposits);
					decimal totalRegFee = contributions.Sum(c => c.TotalRegFee);
					decimal totalLoans = contributions.Sum(c => c.TotalLoanAmount);

					grandTotalShareCapital += totalShareCapital;
					grandTotalSavings += totalSavings;
					grandTotalRegFee += totalRegFee;
					grandTotalLoans += totalLoans;
					grandTotalMembers += members.Count;

					summarySheet.Cell(summaryRow, 1).Value = gig.GigCode;
					summarySheet.Cell(summaryRow, 2).Value = gig.GigName;
					summarySheet.Cell(summaryRow, 3).Value = members.Count;
					summarySheet.Cell(summaryRow, 4).Value = maleCount;
					summarySheet.Cell(summaryRow, 5).Value = femaleCount;
					summarySheet.Cell(summaryRow, 6).Value = youthCount;
					summarySheet.Cell(summaryRow, 7).Value = totalShareCapital;
					summarySheet.Cell(summaryRow, 7).Style.NumberFormat.Format = "#,##0.00";
					summarySheet.Cell(summaryRow, 8).Value = totalSavings;
					summarySheet.Cell(summaryRow, 8).Style.NumberFormat.Format = "#,##0.00";
					summarySheet.Cell(summaryRow, 9).Value = totalRegFee;
					summarySheet.Cell(summaryRow, 9).Style.NumberFormat.Format = "#,##0.00";
					summarySheet.Cell(summaryRow, 10).Value = totalLoans;
					summarySheet.Cell(summaryRow, 10).Style.NumberFormat.Format = "#,##0.00";
					summaryRow++;
				}

				summaryRow++;
				summarySheet.Cell(summaryRow, 2).Value = "GRAND TOTAL:";
				summarySheet.Cell(summaryRow, 2).Style.Font.SetBold();
				summarySheet.Cell(summaryRow, 3).Value = grandTotalMembers;
				summarySheet.Cell(summaryRow, 3).Style.Font.SetBold();
				summarySheet.Cell(summaryRow, 7).Value = grandTotalShareCapital;
				summarySheet.Cell(summaryRow, 7).Style.Font.SetBold();
				summarySheet.Cell(summaryRow, 7).Style.NumberFormat.Format = "#,##0.00";
				summarySheet.Cell(summaryRow, 8).Value = grandTotalSavings;
				summarySheet.Cell(summaryRow, 8).Style.Font.SetBold();
				summarySheet.Cell(summaryRow, 8).Style.NumberFormat.Format = "#,##0.00";
				summarySheet.Cell(summaryRow, 9).Value = grandTotalRegFee;
				summarySheet.Cell(summaryRow, 9).Style.Font.SetBold();
				summarySheet.Cell(summaryRow, 9).Style.NumberFormat.Format = "#,##0.00";
				summarySheet.Cell(summaryRow, 10).Value = grandTotalLoans;
				summarySheet.Cell(summaryRow, 10).Style.Font.SetBold();
				summarySheet.Cell(summaryRow, 10).Style.NumberFormat.Format = "#,##0.00";

				summarySheet.Columns().AdjustToContents();

				// Individual GIG Sheets
				foreach (var gig in gigs)
				{
					var members = await _context.Members
						.Where(m => m.CompanyCode == companyCode
							&& m.Cigcode == gig.GigCode
							&& (m.Withdrawn == false || m.Withdrawn == null)
							&& (m.Archived == false || m.Archived == null))
						.ToListAsync();

					if (!members.Any()) continue;

					string sheetName = gig.GigName.Length > 31 ? gig.GigName.Substring(0, 31) : gig.GigName;
					sheetName = new string(sheetName.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
					if (string.IsNullOrWhiteSpace(sheetName)) sheetName = gig.GigCode;

					var worksheet = workbook.Worksheets.Add(sheetName);
					int currentRow = 1;

					worksheet.Cell(currentRow, 1).Value = companyName.ToUpper();
					worksheet.Range(currentRow, 1, currentRow, 11).Merge();
					worksheet.Cell(currentRow, 1).Style.Font.SetBold().Font.SetFontSize(14);
					worksheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
					currentRow += 2;

					worksheet.Cell(currentRow, 1).Value = $"GIG: {gig.GigName} ({gig.GigCode})";
					worksheet.Range(currentRow, 1, currentRow, 11).Merge();
					worksheet.Cell(currentRow, 1).Style.Font.SetBold().Font.SetFontSize(12);
					worksheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
					currentRow += 2;

					worksheet.Cell(currentRow, 1).Value = $"Period: {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}";
					worksheet.Range(currentRow, 1, currentRow, 11).Merge();
					worksheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
					currentRow += 2;

					string[] memberHeaders = { "No.", "MemberNo", "Names", "Sex", "Phone", "ID No", "Age", "Share Capital", "Savings/Deposits", "Reg Fee", "Loans" };
					for (int i = 0; i < memberHeaders.Length; i++)
					{
						worksheet.Cell(currentRow, i + 1).Value = memberHeaders[i];
						worksheet.Cell(currentRow, i + 1).Style.Font.SetBold();
						worksheet.Cell(currentRow, i + 1).Style.Fill.SetBackgroundColor(XLColor.LightGray);
						worksheet.Cell(currentRow, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
					}
					currentRow++;

					var memberNos = members.Select(m => m.MemberNo).ToList();

					var contributions = await _context.ContribShares
						.Where(c => memberNos.Contains(c.MemberNo)
							&& c.CompanyCode == companyCode
							&& c.ContrDate >= startDate
							&& c.ContrDate <= endDateAdjusted)
						.GroupBy(c => c.MemberNo)
						.Select(g => new
						{
							MemberNo = g.Key,
							TotalShareCapital = g.Sum(c => c.ShareCapitalAmount ?? 0),
							TotalDeposits = g.Sum(c => c.DepositsAmount ?? 0),
							TotalRegFee = g.Sum(c => c.RegFeeAmount ?? 0),
							TotalLoanAmount = g.Sum(c => c.LoanAmount ?? 0)
						})
						.ToDictionaryAsync(c => c.MemberNo, c => new
						{
							c.TotalShareCapital,
							c.TotalDeposits,
							c.TotalRegFee,
							c.TotalLoanAmount
						});

					int serialNo = 1;
					decimal totalShareCapital = 0;
					decimal totalSavings = 0;
					decimal totalRegFee = 0;
					decimal totalLoans = 0;

					foreach (var member in members)
					{
						string fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
						if (string.IsNullOrWhiteSpace(fullName)) fullName = "N/A";

						decimal shareCapital = 0;
						decimal savings = 0;
						decimal regFee = 0;
						decimal loanAmt = 0;

						if (contributions.ContainsKey(member.MemberNo))
						{
							var contrib = contributions[member.MemberNo];
							shareCapital = contrib.TotalShareCapital;
							savings = contrib.TotalDeposits;
							regFee = contrib.TotalRegFee;
							loanAmt = contrib.TotalLoanAmount;
						}

						totalShareCapital += shareCapital;
						totalSavings += savings;
						totalRegFee += regFee;
						totalLoans += loanAmt;

						worksheet.Cell(currentRow, 1).Value = serialNo++;
						worksheet.Cell(currentRow, 2).Value = member.MemberNo;
						worksheet.Cell(currentRow, 3).Value = fullName;
						worksheet.Cell(currentRow, 4).Value = member.Sex ?? "-";
						worksheet.Cell(currentRow, 5).Value = member.PhoneNo ?? member.MobileNo ?? "-";
						worksheet.Cell(currentRow, 6).Value = member.Idno ?? "-";
						worksheet.Cell(currentRow, 7).Value = member.Dob.HasValue ? CalculateAge(member.Dob.Value) : (int?)null;
						worksheet.Cell(currentRow, 8).Value = shareCapital;
						worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0.00";
						worksheet.Cell(currentRow, 9).Value = savings;
						worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "#,##0.00";
						worksheet.Cell(currentRow, 10).Value = regFee;
						worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "#,##0.00";
						worksheet.Cell(currentRow, 11).Value = loanAmt;
						worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "#,##0.00";
						currentRow++;
					}

					currentRow++;
					worksheet.Cell(currentRow, 7).Value = "TOTALS:";
					worksheet.Cell(currentRow, 7).Style.Font.SetBold();
					worksheet.Cell(currentRow, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
					worksheet.Cell(currentRow, 8).Value = totalShareCapital;
					worksheet.Cell(currentRow, 8).Style.Font.SetBold();
					worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 9).Value = totalSavings;
					worksheet.Cell(currentRow, 9).Style.Font.SetBold();
					worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 10).Value = totalRegFee;
					worksheet.Cell(currentRow, 10).Style.Font.SetBold();
					worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 11).Value = totalLoans;
					worksheet.Cell(currentRow, 11).Style.Font.SetBold();
					worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "#,##0.00";

					worksheet.Columns().AdjustToContents();
				}

				using var stream = new MemoryStream();
				workbook.SaveAs(stream);
				return File(stream.ToArray(),
					"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
					$"GIG_Report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error exporting GIG report to Excel");
				TempData["ErrorMessage"] = $"Error exporting to Excel: {ex.Message}";
				return RedirectToAction("Index");
			}
		}

		[HttpPost]
		public async Task<IActionResult> ExportToPdf(DateTime startDate, DateTime endDate)
		{
			try
			{
				var companyCode = _companyContextService.GetCurrentCompanyCode();
				// FIXED: Get company name from the Companies table
				var companyName = GetCompanyNameFromCode(companyCode) ?? "";
				var printedBy = User.Identity?.Name ?? "System";
				var endDateAdjusted = endDate.Date.AddDays(1).AddSeconds(-1);

				// Get all active GIGs for this company
				var gigs = await _context.CIGs
					.Where(g => g.CompanyCode == companyCode && g.Status == "Active")
					.OrderBy(g => g.GigName)
					.ToListAsync();

				var gigReports = new List<GIGReportViewModel>();

				foreach (var gig in gigs)
				{
					var members = await _context.Members
						.Where(m => m.CompanyCode == companyCode
							&& m.Cigcode == gig.GigCode
							&& (m.Withdrawn == false || m.Withdrawn == null)
							&& (m.Archived == false || m.Archived == null))
						.ToListAsync();

					if (!members.Any()) continue;

					var memberNos = members.Select(m => m.MemberNo).ToList();

					var contributions = await _context.ContribShares
						.Where(c => memberNos.Contains(c.MemberNo)
							&& c.CompanyCode == companyCode
							&& c.ContrDate >= startDate
							&& c.ContrDate <= endDateAdjusted)
						.GroupBy(c => c.MemberNo)
						.Select(g => new
						{
							MemberNo = g.Key,
							TotalShareCapital = g.Sum(c => c.ShareCapitalAmount ?? 0),
							TotalDeposits = g.Sum(c => c.DepositsAmount ?? 0),
							TotalRegFee = g.Sum(c => c.RegFeeAmount ?? 0),
							TotalLoanAmount = g.Sum(c => c.LoanAmount ?? 0)
						})
						.ToDictionaryAsync(c => c.MemberNo, c => new
						{
							c.TotalShareCapital,
							c.TotalDeposits,
							c.TotalRegFee,
							c.TotalLoanAmount
						});

					var memberDetails = new List<GIGReportMemberDetail>();
					int maleCount = 0, femaleCount = 0, otherCount = 0, youthCount = 0;
					decimal totalShareCapital = 0, totalShareDepositAmount = 0, totalRegFeeAmount = 0, totalLoanAmount = 0;

					foreach (var member in members)
					{
						string fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
						if (string.IsNullOrWhiteSpace(fullName)) fullName = "N/A";

						int? age = null;
						if (member.Dob.HasValue)
						{
							age = CalculateAge(member.Dob.Value);
							if (age >= 18 && age <= 35) youthCount++;
						}

						if (member.Sex?.ToUpper() == "MALE" || member.Sex?.ToUpper() == "M")
							maleCount++;
						else if (member.Sex?.ToUpper() == "FEMALE" || member.Sex?.ToUpper() == "F")
							femaleCount++;
						else
							otherCount++;

						decimal shareCapital = 0;
						decimal shareDeposit = 0;
						decimal regFee = 0;
						decimal loanAmt = 0;

						if (contributions.ContainsKey(member.MemberNo))
						{
							var contrib = contributions[member.MemberNo];
							shareCapital = contrib.TotalShareCapital;
							shareDeposit = contrib.TotalDeposits;
							regFee = contrib.TotalRegFee;
							loanAmt = contrib.TotalLoanAmount;
						}

						totalShareCapital += shareCapital;
						totalShareDepositAmount += shareDeposit;
						totalRegFeeAmount += regFee;
						totalLoanAmount += loanAmt;

						memberDetails.Add(new GIGReportMemberDetail
						{
							MemberNo = member.MemberNo,
							Names = fullName,
							Sex = member.Sex ?? "Not Specified",
							PhoneNo = member.PhoneNo ?? member.MobileNo ?? "-",
							IDNo = member.Idno ?? "-",
							Age = age,
							CIGCode = gig.GigCode,
							CIGName = gig.GigName,
							ShareCapital = shareCapital,
							ShareDeposits = shareDeposit,
							RegFee = regFee,
							LoanAmt = loanAmt
						});
					}

					gigReports.Add(new GIGReportViewModel
					{
						CIGCode = gig.GigCode,
						CIGName = gig.GigName,
						CompanyCode = companyCode,
						CompanyName = companyName,
						TotalMembers = memberDetails.Count,
						MaleCount = maleCount,
						FemaleCount = femaleCount,
						OtherCount = otherCount,
						YouthCount = youthCount,
						TotalShareCapital = totalShareCapital,
						TotalShareDeposits = totalShareDepositAmount,
						TotalRegFee = totalRegFeeAmount,
						TotalLoans = totalLoanAmount,
						Members = memberDetails
					});
				}

				using var stream = new MemoryStream();

				QuestPDF.Fluent.Document.Create(container =>
				{
					container.Page(page =>
					{
						page.Size(PageSizes.A4.Landscape());
						page.MarginTop(1.5f, Unit.Centimetre);
						page.MarginBottom(1.5f, Unit.Centimetre);
						page.MarginLeft(1.2f, Unit.Centimetre);
						page.MarginRight(1.2f, Unit.Centimetre);
						page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

						page.Header().Column(header =>
						{
							header.Item().AlignCenter().Text(companyName.ToUpper()).FontSize(16).Bold();
							header.Item().AlignCenter().Text($"Printed By: {printedBy} On: {DateTime.Now:dd-MMM-yyyy HH:mm}").FontSize(9).Italic();
							header.Item().PaddingTop(0.5f, Unit.Centimetre);
							header.Item().AlignCenter().Text($"GIGs REPORT - {startDate:dd/MM/yyyy} to {endDate:dd/MM/yyyy}").FontSize(12).Bold();
							header.Item().PaddingTop(0.3f, Unit.Centimetre).LineHorizontal(0.5f);
							header.Item().PaddingBottom(0.5f, Unit.Centimetre);
						});

						page.Content().Column(contentCol =>
						{
							foreach (var gig in gigReports)
							{
								contentCol.Item().PaddingTop(0.5f, Unit.Centimetre);
								contentCol.Item().Text($"{gig.CIGName} ({gig.CIGCode})").FontSize(11).Bold();

								contentCol.Item().Table(summaryTable =>
								{
									summaryTable.ColumnsDefinition(cols =>
									{
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
									});

									summaryTable.Cell().Border(0.2f).Padding(4).Text($"Total Members: {gig.TotalMembers}").Bold();
									summaryTable.Cell().Border(0.2f).Padding(4).Text($"Male: {gig.MaleCount}");
									summaryTable.Cell().Border(0.2f).Padding(4).Text($"Female: {gig.FemaleCount}");
									summaryTable.Cell().Border(0.2f).Padding(4).Text($"Youth: {gig.YouthCount}");
								});

								contentCol.Item().Table(financeTable =>
								{
									financeTable.ColumnsDefinition(cols =>
									{
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
									});

									financeTable.Cell().Border(0.2f).Padding(4).Text($"Share Capital: {gig.TotalShareCapital:N0}").Bold();
									financeTable.Cell().Border(0.2f).Padding(4).Text($"Savings: {gig.TotalShareDeposits:N0}").Bold();
									financeTable.Cell().Border(0.2f).Padding(4).Text($"Reg Fee: {gig.TotalRegFee:N0}").Bold();
									financeTable.Cell().Border(0.2f).Padding(4).Text($"Loans: {gig.TotalLoans:N0}").Bold();
								});

								contentCol.Item().Table(memberTable =>
								{
									memberTable.ColumnsDefinition(cols =>
									{
										cols.RelativeColumn(0.5f);
										cols.RelativeColumn(1.0f);
										cols.RelativeColumn(1.5f);
										cols.RelativeColumn(0.6f);
										cols.RelativeColumn(1.0f);
										cols.RelativeColumn(1.0f);
										cols.RelativeColumn(0.5f);
										cols.RelativeColumn(1.0f);
										cols.RelativeColumn(1.0f);
										cols.RelativeColumn(1.0f);
										cols.RelativeColumn(1.0f);
									});

									memberTable.Header(header =>
									{
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("No").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("MemberNo").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Names").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Sex").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Phone").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("ID No").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Age").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Share Cap").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Savings").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Reg Fee").Bold().FontSize(8);
										header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Loans").Bold().FontSize(8);
									});

									int seqNo = 1;
									foreach (var member in gig.Members)
									{
										memberTable.Cell().Border(0.2f).Padding(4).AlignCenter().Text(seqNo++.ToString()).FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).Text(member.MemberNo).FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).Text(member.Names).FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).AlignCenter().Text(member.Sex).FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).Text(member.PhoneNo).FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).Text(member.IDNo).FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).AlignCenter().Text(member.Age?.ToString() ?? "-").FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.ShareCapital:N0}").FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.ShareDeposits:N0}").FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.RegFee:N0}").FontSize(7);
										memberTable.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.LoanAmt:N0}").FontSize(7);
									}
								});

								contentCol.Item().PaddingBottom(0.5f, Unit.Centimetre);
							}
						});

						page.Footer()
							.AlignCenter()
							.Text(x =>
							{
								x.DefaultTextStyle(t => t.FontSize(8));
								x.Span("Page ");
								x.CurrentPageNumber();
								x.Span(" of ");
								x.TotalPages();
								x.Span($" | Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
							});
					});
				}).GeneratePdf(stream);

				var content = stream.ToArray();
				return File(content, "application/pdf", $"GIG_Report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.pdf");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error exporting GIG report to PDF");
				TempData["ErrorMessage"] = $"Error exporting to PDF: {ex.Message}";
				return RedirectToAction("Index");
			}
		}

		private int CalculateAge(DateTime birthDate)
		{
			var today = DateTime.Today;
			var age = today.Year - birthDate.Year;
			if (birthDate.Date > today.AddYears(-age)) age--;
			return age;
		}

		// Helper method to get company name from company code
		private string GetCompanyNameFromCode(string companyCode)
		{
			if (string.IsNullOrEmpty(companyCode))
				return null;

			var company = _context.Companies
				.FirstOrDefault(c => c.CompanyCode == companyCode);

			return company?.CompanyName;
		}
	}
}