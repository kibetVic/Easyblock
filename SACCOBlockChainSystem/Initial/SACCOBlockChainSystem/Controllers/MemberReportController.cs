using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using ClosedXML.Excel;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SACCOBlockChainSystem.Controllers
{
	[Authorize]
	public class MemberReportController : Controller
	{
		private readonly ApplicationDbContext _context;

		public MemberReportController(ApplicationDbContext context)
		{
			_context = context;
		}

		#region Active Members Report

		public IActionResult ActiveMembers()
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";
			var reportDate = DateTime.Now;

			var viewModel = new ActiveMembersIndexViewModel
			{
				Members = new List<MemberReportViewModel>(),
				ReportDate = reportDate,
				HasData = false,
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				TotalMembers = 0,
				MaleCount = 0,
				FemaleCount = 0,
				OtherCount = 0,
				TotalShareCapital = 0,
				TotalSavingsDeposits = 0,
				TotalRegFee = 0
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.HasData = false;

			return View("~/Views/Reports/ActiveMembers.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> ActiveMembers(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			// Get share type requirements
			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0; // Set your registration fee requirement here

			// Get all active members (not withdrawn, not archived, status active)
			var allActiveMembers = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == null || m.Withdrawn == false)
					&& (m.Archived == null || m.Archived == false)
					&& (m.Status == 1 || m.Status == null))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = allActiveMembers.Select(m => m.MemberNo).ToList();

			// Get contributions (shares, deposits, reg fees)
			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			// Get shares from Shares table as fallback
			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			// Get the date 3 months ago (to check regular contributions)
			var threeMonthsAgo = reportDate.AddMonths(-5);

			// Get contributions in the last 3 months to check regular activity
			var recentContributions = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode
					&& cs.ContrDate >= threeMonthsAgo
					&& cs.ContrDate <= reportDate)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					RecentShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					RecentDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					RecentRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0),
					ContributionCount = g.Count()
				})
				.ToListAsync();

			var reportData = new List<MemberReportViewModel>();

			foreach (var m in allActiveMembers)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == m.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == m.MemberNo);
				var recentContrib = recentContributions.FirstOrDefault(r => r.MemberNo == m.MemberNo);

				// Calculate total share capital
				decimal totalShareCapital = 0;
				if (memberContrib != null)
					totalShareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null)
					totalShareCapital = memberShare.TotalShares;
				else
					totalShareCapital = m.ShareCap ?? 0;

				decimal totalSavingsDeposits = memberContrib?.TotalDeposits ?? 0;
				decimal totalRegistrationFee = memberContrib?.TotalRegFee ?? m.RegFee ?? 0;

				// Check if member meets the active criteria:
				// 1. Has paid minimum share capital requirement
				// 2. Has paid registration fee requirement
				// 3. Has savings/deposits
				// 4. Has made regular contributions in the last 3 months (at least one contribution)

				bool hasMetShareRequirement = minimumShareRequirement == 0 ? true : totalShareCapital >= minimumShareRequirement;
				bool hasPaidRegistrationFee = registrationFeeRequirement == 0 ? true : totalRegistrationFee >= registrationFeeRequirement;
				bool hasSavingsDeposits = totalSavingsDeposits > 0;
				bool hasRegularContributions = recentContrib != null && recentContrib.ContributionCount > 0;

				// Only include if ALL criteria are met
				if (hasMetShareRequirement && hasPaidRegistrationFee && hasSavingsDeposits && hasRegularContributions)
				{
					int? age = null;
					if (m.Dob.HasValue)
					{
						age = DateTime.Now.Year - m.Dob.Value.Year;
						if (DateTime.Now < m.Dob.Value.AddYears(age.Value)) age--;
					}

					string fullName = "";
					if (m.FullName != null)
					{
						fullName = m.FullName.ToString();
					}
					else
					{
						fullName = $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();
						if (string.IsNullOrWhiteSpace(fullName))
							fullName = "N/A";
					}

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(m.Sex))
					{
						string sexUpper = m.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					reportData.Add(new MemberReportViewModel
					{
						MemberNo = m.MemberNo,
						FullName = fullName,
						IdNo = m.Idno ?? "-",
						Sex = sex,
						Age = age,
						MembershipType = m.MembershipType ?? "Individual",
						ApplicDate = m.ApplicDate,
						EffectDate = m.EffectDate,
						ShareCapital = totalShareCapital,
						SavingsDeposits = totalSavingsDeposits,
						RegFee = totalRegistrationFee,
						LoanBalance = m.LoanBalance ?? 0,
						PhoneNo = m.PhoneNo ?? m.MobileNo ?? "-",
						Email = m.Email ?? m.EmailAddress,
						Station = m.Station ?? "-",
						Status = "ACTIVE",
						// Add these if your view model has them
						// LastContributionDate = recentContrib?.LastContribDate,
						// TotalActiveContributions = recentContrib?.ContributionCount ?? 0
					});
				}
			}

			// Sort by member number
			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
			int otherCount = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE"
												&& !string.IsNullOrEmpty(m.Sex) && m.Sex != "NOT SPECIFIED");

			var viewModel = new ActiveMembersIndexViewModel
			{
				Members = reportData,
				TotalMembers = reportData.Count,
				MaleCount = maleCount,
				FemaleCount = femaleCount,
				OtherCount = otherCount,
				TotalShareCapital = reportData.Sum(m => m.ShareCapital ?? 0),
				TotalSavingsDeposits = reportData.Sum(m => m.SavingsDeposits ?? 0),
				TotalRegFee = reportData.Sum(m => m.RegFee ?? 0),
				ReportDate = reportDate,
				HasData = reportData.Any(),
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				// Add these if your view model has them
				// MinimumShareRequirement = minimumShareRequirement,
				// RegistrationFeeRequirement = registrationFeeRequirement,
				// ActiveContributionPeriodMonths = 3
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.TotalMembers = reportData.Count;
			ViewBag.TotalShareCapital = reportData.Sum(m => m.ShareCapital ?? 0);
			ViewBag.TotalSavingsDeposits = reportData.Sum(m => m.SavingsDeposits ?? 0);
			ViewBag.TotalRegFee = reportData.Sum(m => m.RegFee ?? 0);
			ViewBag.MaleCount = maleCount;
			ViewBag.FemaleCount = femaleCount;
			ViewBag.OtherCount = otherCount;
			ViewBag.HasData = reportData.Any();

			return View("~/Views/Reports/ActiveMembers.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> ExportActiveMembersToExcel(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn != true)
					&& (m.Archived != true)
					&& (m.Status == null || m.Status == 1))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribLookup = await _context.ContribShares
				.Where(c => memberNos.Contains(c.MemberNo) && c.CompanyCode == companyCode)
				.GroupBy(c => c.MemberNo)
				.Select(g => new
				{
					g.Key,
					Share = g.Sum(x => x.ShareCapitalAmount ?? 0),
					Deposits = g.Sum(x => x.DepositsAmount ?? 0),
					RegFee = g.Sum(x => x.RegFeeAmount ?? 0)
				})
				.ToDictionaryAsync(x => x.Key);

			var report = members.Select(m =>
			{
				contribLookup.TryGetValue(m.MemberNo, out var contrib);

				string name = !string.IsNullOrWhiteSpace(m.FullName?.ToString())
					? m.FullName.ToString()
					: $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();

				if (string.IsNullOrWhiteSpace(name)) name = "N/A";

				string sex = "NOT SPECIFIED";
				if (!string.IsNullOrEmpty(m.Sex))
				{
					var s = m.Sex.ToUpper();
					sex = (s == "M" || s == "MALE") ? "MALE"
						 : (s == "F" || s == "FEMALE") ? "FEMALE"
						 : s;
				}

				return new
				{
					m.MemberNo,
					Name = name,
					Sex = sex,
					Share = contrib?.Share ?? m.ShareCap ?? 0,
					Deposits = contrib?.Deposits ?? 0,
					RegFee = contrib?.RegFee ?? m.RegFee ?? 0
				};
			}).ToList();

			int male = report.Count(x => x.Sex == "MALE");
			int female = report.Count(x => x.Sex == "FEMALE");
			int other = report.Count(x => x.Sex != "MALE" && x.Sex != "FEMALE" && x.Sex != "NOT SPECIFIED");

			decimal totalShare = report.Sum(x => x.Share);
			decimal totalDeposits = report.Sum(x => x.Deposits);
			decimal totalReg = report.Sum(x => x.RegFee);

			using var wb = new XLWorkbook();
			var ws = wb.Worksheets.Add("Active Members");
			int r = 1;

			ws.Cell(r, 1).Value = companyName.ToUpper();
			ws.Range(r, 1, r, 6).Merge().Style.Font.SetBold().Font.SetFontSize(16)
				.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
			r += 2;

			ws.Cell(r, 1).Value = $"ACTIVE MEMBERS AS AT {reportDate:dd/MM/yyyy}";
			ws.Range(r, 1, r, 6).Merge().Style.Font.SetBold()
				.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
			r += 2;

			ws.Cell(r++, 1).Value = $"TOTAL: {report.Count}";
			ws.Cell(r++, 1).Value = $"MALE: {male}";
			ws.Cell(r++, 1).Value = $"FEMALE: {female}";
			ws.Cell(r++, 1).Value = $"OTHERS: {other}";
			r++;

			string[] headers = { "MemberNo", "Names", "Sex", "Share Capital", "Deposits", "Reg Fee" };

			for (int i = 0; i < headers.Length; i++)
			{
				ws.Cell(r, i + 1).Value = headers[i];
				ws.Cell(r, i + 1).Style.Font.SetBold();
			}

			r++;

			foreach (var m in report)
			{
				ws.Cell(r, 1).Value = m.MemberNo;
				ws.Cell(r, 2).Value = m.Name;
				ws.Cell(r, 3).Value = m.Sex;
				ws.Cell(r, 4).Value = m.Share;
				ws.Cell(r, 5).Value = m.Deposits;
				ws.Cell(r, 6).Value = m.RegFee;

				ws.Range(r, 4, r, 6).Style.NumberFormat.Format = "#,##0.00";
				r++;
			}

			r++;

			ws.Cell(r, 3).Value = "TOTAL:";
			ws.Cell(r, 3).Style.Font.SetBold();

			ws.Cell(r, 4).Value = totalShare;
			ws.Cell(r, 5).Value = totalDeposits;
			ws.Cell(r, 6).Value = totalReg;

			ws.Range(r, 4, r, 6).Style.Font.SetBold()
				.NumberFormat.SetFormat("#,##0.00");

			ws.Columns().AdjustToContents();

			using var stream = new MemoryStream();
			wb.SaveAs(stream);

			return File(stream.ToArray(),
				"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
				$"ActiveMembers_{reportDate:yyyyMMdd}.xlsx");
		}

		[HttpPost]
		public async Task<IActionResult> ExportActiveMembersToPdf(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn != true)
					&& (m.Archived != true)
					&& (m.Status == null || m.Status == 1))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribLookup = await _context.ContribShares
				.Where(c => memberNos.Contains(c.MemberNo) && c.CompanyCode == companyCode)
				.GroupBy(c => c.MemberNo)
				.Select(g => new
				{
					g.Key,
					Share = g.Sum(x => x.ShareCapitalAmount ?? 0),
					Deposits = g.Sum(x => x.DepositsAmount ?? 0),
					RegFee = g.Sum(x => x.RegFeeAmount ?? 0)
				})
				.ToDictionaryAsync(x => x.Key);

			var report = members.Select(m =>
			{
				contribLookup.TryGetValue(m.MemberNo, out var c);

				string name = !string.IsNullOrWhiteSpace(m.FullName?.ToString())
					? m.FullName.ToString()
					: $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();

				if (string.IsNullOrWhiteSpace(name)) name = "N/A";

				string sex = "NOT SPECIFIED";
				if (!string.IsNullOrEmpty(m.Sex))
				{
					var s = m.Sex.ToUpper();
					sex = (s == "M" || s == "MALE") ? "MALE"
						 : (s == "F" || s == "FEMALE") ? "FEMALE"
						 : s;
				}

				return new ActiveMemberPdfData
				{
					MemberNo = m.MemberNo,
					Name = name,
					Sex = sex,
					Share = c?.Share ?? m.ShareCap ?? 0,
					Deposits = c?.Deposits ?? 0,
					RegFee = c?.RegFee ?? m.RegFee ?? 0
				};
			}).ToList();

			int male = report.Count(x => x.Sex == "MALE");
			int female = report.Count(x => x.Sex == "FEMALE");

			using var stream = new MemoryStream();

			QuestPDF.Fluent.Document.Create(container =>
			{
				container.Page(page =>
				{
					page.Size(PageSizes.A4.Landscape());
					page.Margin(1.5f, Unit.Centimetre);
					page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

					page.Header()
						.AlignCenter()
						.Column(column =>
						{
							column.Item().Text(companyName.ToUpper()).FontSize(18).Bold();
							column.Item().Text($"ACTIVE MEMBERS AS AT {reportDate:dd/MM/yyyy}").FontSize(14).Bold();
						});

					page.Content()
						.PaddingVertical(1, Unit.Centimetre)
						.Column(column =>
						{
							column.Item().Table(statsTable =>
							{
								statsTable.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
								});

								statsTable.Cell().Element(c => c.Text($"TOTAL: {report.Count}").Bold());
								statsTable.Cell().Element(c => c.Text($"MALE: {male}").Bold());
								statsTable.Cell().Element(c => c.Text($"FEMALE: {female}").Bold());
								statsTable.Cell().Element(c => c.Text($"GENERATED: {DateTime.Now:dd/MM/yyyy}"));
							});

							column.Item().PaddingTop(1, Unit.Centimetre);

							column.Item().Table(table =>
							{
								table.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(1);
									cols.RelativeColumn(2);
									cols.RelativeColumn(0.8f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
								});

								table.Header(header =>
								{
									header.Cell().Element(c => c.Text("MemberNo").Bold());
									header.Cell().Element(c => c.Text("Names").Bold());
									header.Cell().Element(c => c.Text("Sex").Bold());
									header.Cell().Element(c => c.Text("Share").Bold());
									header.Cell().Element(c => c.Text("Deposits").Bold());
									header.Cell().Element(c => c.Text("Reg Fee").Bold());
								});

								foreach (var m in report)
								{
									table.Cell().Element(c => c.Text(m.MemberNo ?? ""));
									table.Cell().Element(c => c.Text(m.Name));
									table.Cell().Element(c => c.Text(m.Sex));
									table.Cell().Element(c => c.AlignRight().Text($"{m.Share:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{m.Deposits:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{m.RegFee:N0}"));
								}
							});
						});

					page.Footer()
						.AlignRight()
						.Text(x => x.Span($"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}"));
				});
			}).GeneratePdf(stream);

			return File(stream.ToArray(), "application/pdf", $"ActiveMembers_{reportDate:yyyyMMdd}.pdf");
		}

		#endregion

		#region Inactive Members Report

		public IActionResult InactiveMembers()
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";
			var reportDate = DateTime.Now;

			var viewModel = new InactiveMembersIndexViewModel
			{
				Members = new List<MemberReportViewModel>(),
				ReportDate = reportDate,
				HasData = false,
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				TotalMembers = 0,
				MaleCount = 0,
				FemaleCount = 0,
				OtherCount = 0,
				TotalShareCapital = 0,
				TotalSavingsDeposits = 0,
				TotalRegFee = 0
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.HasData = false;

			return View("~/Views/Reports/InactiveMembers.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> InactiveMembers(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			// Get share type requirements
			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0; // Set your registration fee requirement here

			// Get all members (including active ones that don't meet criteria)
			var allMembers = await _context.Members
				.Where(m => m.CompanyCode == companyCode)
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = allMembers.Select(m => m.MemberNo).ToList();

			// Get contributions (shares, deposits, reg fees)
			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			// Get shares from Shares table as fallback
			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			// Get the date 3 months ago (to check regular contributions)
			var threeMonthsAgo = reportDate.AddMonths(-5);

			// Get contributions in the last 3 months to check regular activity
			var recentContributions = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode
					&& cs.ContrDate >= threeMonthsAgo
					&& cs.ContrDate <= reportDate)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					RecentShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					RecentDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					RecentRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0),
					ContributionCount = g.Count(),
					LastContributionDate = g.Max(cs => cs.ContrDate)
				})
				.ToListAsync();

			var reportData = new List<MemberReportViewModel>();

			foreach (var m in allMembers)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == m.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == m.MemberNo);
				var recentContrib = recentContributions.FirstOrDefault(r => r.MemberNo == m.MemberNo);

				// Calculate total share capital
				decimal totalShareCapital = 0;
				if (memberContrib != null)
					totalShareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null)
					totalShareCapital = memberShare.TotalShares;
				else
					totalShareCapital = m.ShareCap ?? 0;

				decimal totalSavingsDeposits = memberContrib?.TotalDeposits ?? 0;
				decimal totalRegistrationFee = memberContrib?.TotalRegFee ?? m.RegFee ?? 0;

				// Check if member meets the active criteria:
				// 1. Has paid minimum share capital requirement
				// 2. Has paid registration fee requirement
				// 3. Has savings/deposits
				// 4. Has made regular contributions in the last 3 months

				bool hasMetShareRequirement = minimumShareRequirement == 0 ? true : totalShareCapital >= minimumShareRequirement;
				bool hasPaidRegistrationFee = registrationFeeRequirement == 0 ? true : totalRegistrationFee >= registrationFeeRequirement;
				bool hasSavingsDeposits = totalSavingsDeposits > 0;
				bool hasRegularContributions = recentContrib != null && recentContrib.ContributionCount > 0;

				// Determine why the member is inactive
				bool isFullyActive = hasMetShareRequirement && hasPaidRegistrationFee && hasSavingsDeposits && hasRegularContributions;

				// Only include if NOT fully active (inactive by our new definition)
				if (!isFullyActive)
				{
					int? age = null;
					if (m.Dob.HasValue)
					{
						age = DateTime.Now.Year - m.Dob.Value.Year;
						if (DateTime.Now < m.Dob.Value.AddYears(age.Value)) age--;
					}

					string fullName = "";
					if (m.FullName != null)
					{
						fullName = m.FullName.ToString();
					}
					else
					{
						fullName = $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();
						if (string.IsNullOrWhiteSpace(fullName))
							fullName = "N/A";
					}

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(m.Sex))
					{
						string sexUpper = m.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					// Determine specific status based on what's missing
					string inactiveReason = "";
					if (m.Withdrawn == true)
						inactiveReason = "WITHDRAWN";
					else if (m.Archived == true)
						inactiveReason = "ARCHIVED";
					else if (m.Status == 0)
						inactiveReason = "INACTIVE";
					else
					{
						// Determine the reason for inactivity
						var missingReasons = new List<string>();
						if (!hasMetShareRequirement && minimumShareRequirement > 0)
							missingReasons.Add($"Share Capital (Min: {minimumShareRequirement:N0})");
						if (!hasPaidRegistrationFee && registrationFeeRequirement > 0)
							missingReasons.Add("Registration Fee");
						if (!hasSavingsDeposits)
							missingReasons.Add("Savings/Deposits");
						if (!hasRegularContributions)
							missingReasons.Add($"No contributions in last 3 months");

						inactiveReason = string.Join(", ", missingReasons);
						if (string.IsNullOrEmpty(inactiveReason))
							inactiveReason = "INACTIVE";
					}

					reportData.Add(new MemberReportViewModel
					{
						MemberNo = m.MemberNo,
						FullName = fullName,
						IdNo = m.Idno ?? "-",
						Sex = sex,
						Age = age,
						MembershipType = m.MembershipType ?? "Individual",
						ApplicDate = m.ApplicDate,
						EffectDate = m.EffectDate,
						ShareCapital = totalShareCapital,
						SavingsDeposits = totalSavingsDeposits,
						RegFee = totalRegistrationFee,
						LoanBalance = m.LoanBalance ?? 0,
						PhoneNo = m.PhoneNo ?? m.MobileNo ?? "-",
						Email = m.Email ?? m.EmailAddress,
						Station = m.Station ?? "-",
						Status = inactiveReason,
						// Optional: Add these properties to your ViewModel if needed
						// HasMetShareRequirement = hasMetShareRequirement,
						// HasPaidRegistrationFee = hasPaidRegistrationFee,
						// HasSavingsDeposits = hasSavingsDeposits,
						// HasRegularContributions = hasRegularContributions,
						// LastContributionDate = recentContrib?.LastContributionDate,
						// MonthsSinceLastContribution = recentContrib != null && recentContrib.LastContributionDate.HasValue 
						//     ? (reportDate - recentContrib.LastContributionDate.Value).Days / 30 
						//     : null
					});
				}
			}

			// Sort by member number
			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			// Calculate statistics
			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
			int otherCount = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE"
												&& !string.IsNullOrEmpty(m.Sex) && m.Sex != "NOT SPECIFIED");

			// Count by inactivity reason
			int withdrawnCount = reportData.Count(m => m.Status == "WITHDRAWN");
			int archivedCount = reportData.Count(m => m.Status == "ARCHIVED");
			int shareCapitalMissingCount = reportData.Count(m => m.Status.Contains("Share Capital"));
			int regFeeMissingCount = reportData.Count(m => m.Status.Contains("Registration Fee"));
			int savingsMissingCount = reportData.Count(m => m.Status.Contains("Savings/Deposits"));
			int noContributionsCount = reportData.Count(m => m.Status.Contains("No contributions"));

			var viewModel = new InactiveMembersIndexViewModel
			{
				Members = reportData,
				TotalMembers = reportData.Count,
				MaleCount = maleCount,
				FemaleCount = femaleCount,
				OtherCount = otherCount,
				TotalShareCapital = reportData.Sum(m => m.ShareCapital ?? 0),
				TotalSavingsDeposits = reportData.Sum(m => m.SavingsDeposits ?? 0),
				TotalRegFee = reportData.Sum(m => m.RegFee ?? 0),
				ReportDate = reportDate,
				HasData = reportData.Any(),
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				// Add these properties to your ViewModel if needed
				// WithdrawnCount = withdrawnCount,
				// ArchivedCount = archivedCount,
				// ShareCapitalMissingCount = shareCapitalMissingCount,
				// RegistrationFeeMissingCount = regFeeMissingCount,
				// SavingsMissingCount = savingsMissingCount,
				// NoRegularContributionsCount = noContributionsCount,
				// MinimumShareRequirement = minimumShareRequirement,
				// RegistrationFeeRequirement = registrationFeeRequirement,
				// ActiveContributionPeriodMonths = 3
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.TotalMembers = reportData.Count;
			ViewBag.TotalShareCapital = reportData.Sum(m => m.ShareCapital ?? 0);
			ViewBag.TotalSavingsDeposits = reportData.Sum(m => m.SavingsDeposits ?? 0);
			ViewBag.TotalRegFee = reportData.Sum(m => m.RegFee ?? 0);
			ViewBag.MaleCount = maleCount;
			ViewBag.FemaleCount = femaleCount;
			ViewBag.OtherCount = otherCount;
			ViewBag.HasData = reportData.Any();
			ViewBag.WithdrawnCount = withdrawnCount;
			ViewBag.ArchivedCount = archivedCount;
			ViewBag.ShareCapitalMissingCount = shareCapitalMissingCount;
			ViewBag.RegistrationFeeMissingCount = regFeeMissingCount;
			ViewBag.SavingsMissingCount = savingsMissingCount;
			ViewBag.NoRegularContributionsCount = noContributionsCount;

			return View("~/Views/Reports/InactiveMembers.cshtml", viewModel);
		}
		[HttpPost]
		public async Task<IActionResult> ExportInactiveMembersToExcel(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& ((m.Withdrawn == true) || (m.Archived == true) || m.Status == 0))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			var reportData = new List<dynamic>();

			foreach (var m in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == m.MemberNo);

				string fullName = "";
				if (m.FullName != null)
				{
					fullName = m.FullName.ToString();
				}
				else
				{
					fullName = $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();
					if (string.IsNullOrWhiteSpace(fullName))
						fullName = "N/A";
				}

				string sex = "NOT SPECIFIED";
				if (!string.IsNullOrEmpty(m.Sex))
				{
					string sexUpper = m.Sex.ToUpper();
					if (sexUpper == "M" || sexUpper == "MALE")
						sex = "MALE";
					else if (sexUpper == "F" || sexUpper == "FEMALE")
						sex = "FEMALE";
					else
						sex = sexUpper;
				}

				string status = "INACTIVE";
				if (m.Withdrawn == true) status = "WITHDRAWN";
				if (m.Archived == true) status = "ARCHIVED";

				reportData.Add(new
				{
					m.MemberNo,
					FullName = fullName,
					Sex = sex,
					ShareCapital = memberContrib?.TotalShareCapital ?? m.ShareCap ?? 0,
					SavingsDeposits = memberContrib?.TotalDeposits ?? 0,
					RegFee = memberContrib?.TotalRegFee ?? m.RegFee ?? 0,
					Status = status
				});
			}

			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
			int otherCount = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE" && m.Sex != "NOT SPECIFIED");

			using (var workbook = new XLWorkbook())
			{
				var worksheet = workbook.Worksheets.Add("Inactive Members");
				var currentRow = 1;

				worksheet.Cell(currentRow, 1).Value = companyName.ToUpper();
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 1).Style.Font.FontSize = 16;
				worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				currentRow += 2;

				worksheet.Cell(currentRow, 1).Value = $"INACTIVE SACCO MEMBERS AS AT {reportDate:dd/MM/yyyy}";
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
				worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				currentRow += 2;

				worksheet.Cell(currentRow, 1).Value = "TOTAL MEMBERS:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count;
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
				currentRow++;

				worksheet.Cell(currentRow, 1).Value = "MALE:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = maleCount;
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
				currentRow++;

				worksheet.Cell(currentRow, 1).Value = "FEMALE:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = femaleCount;
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
				currentRow++;

				worksheet.Cell(currentRow, 1).Value = "OTHERS:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = otherCount;
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
				currentRow += 2;

				var headers = new[] { "MemberNo", "Names", "Sex", "Share Capital", "Savings/Deposits", "Reg Fee", "Status" };

				for (int i = 0; i < headers.Length; i++)
				{
					worksheet.Cell(currentRow, i + 1).Value = headers[i];
					worksheet.Cell(currentRow, i + 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
					worksheet.Cell(currentRow, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
					worksheet.Cell(currentRow, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				}
				currentRow++;

				foreach (var member in reportData)
				{
					worksheet.Cell(currentRow, 1).Value = member.MemberNo;
					worksheet.Cell(currentRow, 2).Value = member.FullName;
					worksheet.Cell(currentRow, 3).Value = member.Sex;
					worksheet.Cell(currentRow, 4).Value = member.ShareCapital;
					worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 5).Value = member.SavingsDeposits;
					worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 6).Value = member.RegFee;
					worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 7).Value = member.Status;

					worksheet.Range(currentRow, 1, currentRow, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
					currentRow++;
				}

				currentRow++;
				worksheet.Cell(currentRow, 3).Value = "GRAND TOTAL:";
				worksheet.Cell(currentRow, 3).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

				worksheet.Cell(currentRow, 4).Value = reportData.Sum(m => (decimal)m.ShareCapital);
				worksheet.Cell(currentRow, 4).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0.00";

				worksheet.Cell(currentRow, 5).Value = reportData.Sum(m => (decimal)m.SavingsDeposits);
				worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";

				worksheet.Cell(currentRow, 6).Value = reportData.Sum(m => (decimal)m.RegFee);
				worksheet.Cell(currentRow, 6).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";

				currentRow += 3;
				worksheet.Cell(currentRow, 1).Value = $"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Italic = true;

				worksheet.Columns().AdjustToContents();

				using (var stream = new MemoryStream())
				{
					workbook.SaveAs(stream);
					var content = stream.ToArray();
					return File(content,
						"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
						$"InactiveMembers_{reportDate:yyyyMMdd}.xlsx");
				}
			}
		}

		[HttpPost]
		public async Task<IActionResult> ExportInactiveMembersToPdf(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& ((m.Withdrawn == true) || (m.Archived == true) || m.Status == 0))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			var reportData = new List<InactiveMemberPdfData>();

			foreach (var m in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == m.MemberNo);

				string fullName = "";
				if (m.FullName != null)
				{
					fullName = m.FullName.ToString();
				}
				else
				{
					fullName = $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();
					if (string.IsNullOrWhiteSpace(fullName))
						fullName = "N/A";
				}

				string sex = "NOT SPECIFIED";
				if (!string.IsNullOrEmpty(m.Sex))
				{
					string sexUpper = m.Sex.ToUpper();
					if (sexUpper == "M" || sexUpper == "MALE")
						sex = "MALE";
					else if (sexUpper == "F" || sexUpper == "FEMALE")
						sex = "FEMALE";
					else
						sex = sexUpper;
				}

				string status = "INACTIVE";
				if (m.Withdrawn == true) status = "WITHDRAWN";
				if (m.Archived == true) status = "ARCHIVED";

				reportData.Add(new InactiveMemberPdfData
				{
					MemberNo = m.MemberNo,
					FullName = fullName,
					Sex = sex,
					ShareCapital = memberContrib?.TotalShareCapital ?? m.ShareCap ?? 0,
					SavingsDeposits = memberContrib?.TotalDeposits ?? 0,
					RegFee = memberContrib?.TotalRegFee ?? m.RegFee ?? 0,
					Status = status
				});
			}

			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");

			using var stream = new MemoryStream();

			QuestPDF.Fluent.Document.Create(container =>
			{
				container.Page(page =>
				{
					page.Size(PageSizes.A4.Landscape());
					page.Margin(1.5f, Unit.Centimetre);
					page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

					page.Header()
						.AlignCenter()
						.Column(column =>
						{
							column.Item().Text(companyName.ToUpper()).FontSize(18).Bold();
							column.Item().Text($"INACTIVE MEMBERS AS AT {reportDate:dd/MM/yyyy}").FontSize(14).Bold();
						});

					page.Content()
						.PaddingVertical(1, Unit.Centimetre)
						.Column(column =>
						{
							column.Item().Table(statsTable =>
							{
								statsTable.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
								});

								statsTable.Cell().Element(c => c.Text("TOTAL MEMBERS:").Bold());
								statsTable.Cell().Element(c => c.Text(reportData.Count.ToString()).Bold());
								statsTable.Cell().Element(c => c.Text("MALE:").Bold());
								statsTable.Cell().Element(c => c.Text(maleCount.ToString()).Bold());
								statsTable.Cell().Element(c => c.Text("FEMALE:").Bold());
								statsTable.Cell().Element(c => c.Text(femaleCount.ToString()).Bold());
							});

							column.Item().PaddingTop(1, Unit.Centimetre);

							column.Item().Table(table =>
							{
								table.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(1);
									cols.RelativeColumn(2);
									cols.RelativeColumn(0.8f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1);
								});

								table.Header(header =>
								{
									header.Cell().Element(c => c.Text("MemberNo").Bold());
									header.Cell().Element(c => c.Text("Names").Bold());
									header.Cell().Element(c => c.Text("Sex").Bold());
									header.Cell().Element(c => c.Text("Share Capital").Bold());
									header.Cell().Element(c => c.Text("Savings/Deposits").Bold());
									header.Cell().Element(c => c.Text("Reg Fee").Bold());
									header.Cell().Element(c => c.Text("Status").Bold());
								});

								foreach (var member in reportData)
								{
									table.Cell().Element(c => c.Text(member.MemberNo ?? ""));
									table.Cell().Element(c => c.Text(member.FullName ?? ""));
									table.Cell().Element(c => c.Text(member.Sex ?? ""));
									table.Cell().Element(c => c.AlignRight().Text($"{member.ShareCapital:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{member.SavingsDeposits:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{member.RegFee:N0}"));
									table.Cell().Element(c => c.Text(member.Status ?? ""));
								}
							});
						});

					page.Footer()
						.AlignRight()
						.Text(x => x.Span($"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}"));
				});
			}).GeneratePdf(stream);

			var content = stream.ToArray();
			return File(content, "application/pdf", $"InactiveMembers_{reportDate:yyyyMMdd}.pdf");
		}

		#endregion

		#region Members Per SACCO Report

		public IActionResult MembersPerSacco()
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";
			var reportDate = DateTime.Now;

			var viewModel = new MembersPerSaccoIndexViewModel
			{
				Members = new List<MemberPerSaccoReportVM>(),
				ReportDate = reportDate,
				SaccoName = companyName,
				HasData = false,
				UserCompanyCode = companyCode,
				TotalMembers = 0,
				MaleCount = 0,
				FemaleCount = 0,
				YouthCount = 0
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.SaccoName = companyName;
			ViewBag.HasData = false;

			return View("~/Views/Reports/MembersPerSacco.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> MembersPerSacco(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode)
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var reportData = new List<MemberPerSaccoReportVM>();

			foreach (var m in members)
			{
				string fullName = "";
				if (m.FullName != null)
				{
					fullName = m.FullName.ToString();
				}
				else
				{
					fullName = $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();
					if (string.IsNullOrWhiteSpace(fullName))
						fullName = "N/A";
				}

				string sex = "NOT SPECIFIED";
				if (!string.IsNullOrEmpty(m.Sex))
				{
					string sexUpper = m.Sex.ToUpper();
					if (sexUpper == "M" || sexUpper == "MALE")
						sex = "MALE";
					else if (sexUpper == "F" || sexUpper == "FEMALE")
						sex = "FEMALE";
					else
						sex = sexUpper;
				}

				int? age = null;
				if (m.Dob.HasValue)
				{
					age = DateTime.Now.Year - m.Dob.Value.Year;
					if (DateTime.Now < m.Dob.Value.AddYears(age.Value)) age--;
				}

				string status = "ACTIVE";
				if (m.Withdrawn == true) status = "WITHDRAWN";
				else if (m.Archived == true) status = "ARCHIVED";
				else if (m.Status == 0) status = "INACTIVE";

				reportData.Add(new MemberPerSaccoReportVM
				{
					MemberNo = m.MemberNo,
					FullName = fullName,
					Sex = sex,
					PhoneNo = m.PhoneNo ?? m.MobileNo ?? "-",
					IDNo = m.Idno ?? "-",
					ApplicDate = m.ApplicDate,
					EffectDate = m.EffectDate,
					MembershipType = m.MembershipType ?? "Individual",
					Station = m.Station ?? "-",
					Age = age,
					Status = status,
					SaccoName = companyName
				});
			}

			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
			int youthCount = reportData.Count(m => m.Age.HasValue && m.Age >= 18 && m.Age <= 35);

			var viewModel = new MembersPerSaccoIndexViewModel
			{
				Members = reportData,
				TotalMembers = reportData.Count,
				MaleCount = maleCount,
				FemaleCount = femaleCount,
				YouthCount = youthCount,
				SaccoName = companyName,
				ReportDate = reportDate,
				HasData = reportData.Any(),
				UserCompanyCode = companyCode
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.SaccoName = companyName;
			ViewBag.TotalMembers = reportData.Count;
			ViewBag.MaleCount = maleCount;
			ViewBag.FemaleCount = femaleCount;
			ViewBag.YouthCount = youthCount;
			ViewBag.HasData = reportData.Any();

			return View("~/Views/Reports/MembersPerSacco.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> ExportMembersPerSaccoToExcel(DateTime reportDate)
		{
			try
			{
				var companyCode = User.FindFirstValue("CompanyCode");
				var companyName = User.FindFirstValue("CompanyName") ?? "";

				var members = await _context.Members
					.Where(m => m.CompanyCode == companyCode)
					.OrderBy(m => m.MemberNo)
					.ToListAsync();

				var reportData = new List<dynamic>();

				foreach (var m in members)
				{
					string fullName = "";
					if (m.FullName != null)
					{
						fullName = m.FullName.ToString();
					}
					else
					{
						fullName = $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();
						if (string.IsNullOrWhiteSpace(fullName))
							fullName = "N/A";
					}

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(m.Sex))
					{
						string sexUpper = m.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					int? age = null;
					if (m.Dob.HasValue)
					{
						age = DateTime.Now.Year - m.Dob.Value.Year;
						if (DateTime.Now < m.Dob.Value.AddYears(age.Value)) age--;
					}

					string status = "ACTIVE";
					if (m.Withdrawn == true) status = "WITHDRAWN";
					else if (m.Archived == true) status = "ARCHIVED";
					else if (m.Status == 0) status = "INACTIVE";

					reportData.Add(new
					{
						m.MemberNo,
						FullName = fullName,
						Sex = sex,
						PhoneNo = m.PhoneNo ?? m.MobileNo ?? "-",
						IDNo = m.Idno ?? "-",
						ApplicDate = m.ApplicDate,
						Age = age,
						Status = status
					});
				}

				int maleCount = reportData.Count(m => m.Sex == "MALE");
				int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
				int youthCount = reportData.Count(m => m.Age != null && m.Age >= 18 && m.Age <= 35);

				using (var workbook = new XLWorkbook())
				{
					var worksheet = workbook.Worksheets.Add("Members Per SACCO");
					var currentRow = 1;

					worksheet.Cell(currentRow, 1).Value = companyName.ToUpper();
					worksheet.Range(currentRow, 1, currentRow, 7).Merge();
					worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, 1).Style.Font.FontSize = 18;
					worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
					currentRow += 2;

					worksheet.Cell(currentRow, 1).Value = $"AS AT {reportDate:dd/MM/yyyy}";
					worksheet.Range(currentRow, 1, currentRow, 7).Merge();
					worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
					worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
					currentRow += 2;

					worksheet.Cell(currentRow, 1).Value = "TOTAL MEMBERS:";
					worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, 2).Value = reportData.Count;
					worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
					currentRow++;

					worksheet.Cell(currentRow, 1).Value = "MALE:";
					worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, 2).Value = maleCount;
					worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
					currentRow++;

					worksheet.Cell(currentRow, 1).Value = "FEMALE:";
					worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, 2).Value = femaleCount;
					worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
					currentRow++;

					worksheet.Cell(currentRow, 1).Value = "YOUTH:";
					worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, 2).Value = youthCount;
					worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
					currentRow += 2;

					var headers = new[] { "MemberNo", "Names", "Sex", "PhoneNo", "IDNo", "ApplicDate", "Status" };

					for (int i = 0; i < headers.Length; i++)
					{
						worksheet.Cell(currentRow, i + 1).Value = headers[i];
						worksheet.Cell(currentRow, i + 1).Style.Font.Bold = true;
						worksheet.Cell(currentRow, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
						worksheet.Cell(currentRow, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
						worksheet.Cell(currentRow, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
					}
					currentRow++;

					foreach (var member in reportData)
					{
						worksheet.Cell(currentRow, 1).Value = member.MemberNo;
						worksheet.Cell(currentRow, 2).Value = member.FullName;
						worksheet.Cell(currentRow, 3).Value = member.Sex;
						worksheet.Cell(currentRow, 4).Value = member.PhoneNo;
						worksheet.Cell(currentRow, 5).Value = member.IDNo;
						worksheet.Cell(currentRow, 6).Value = member.ApplicDate?.ToString("dd/MM/yyyy");
						worksheet.Cell(currentRow, 7).Value = member.Status;

						worksheet.Range(currentRow, 1, currentRow, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
						currentRow++;
					}

					currentRow += 2;
					worksheet.Cell(currentRow, 1).Value = "GRAND TOTAL MEMBERS:";
					worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, 2).Value = reportData.Count;
					worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

					currentRow += 2;
					worksheet.Cell(currentRow, 1).Value = $"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
					worksheet.Range(currentRow, 1, currentRow, 7).Merge();
					worksheet.Cell(currentRow, 1).Style.Font.Italic = true;

					worksheet.Columns().AdjustToContents();

					using (var stream = new MemoryStream())
					{
						workbook.SaveAs(stream);
						var content = stream.ToArray();
						return File(
							content,
							"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
							$"MembersPerSacco_{reportDate:yyyyMMdd}.xlsx"
						);
					}
				}
			}
			catch (Exception ex)
			{
				TempData["Error"] = "Failed to export Excel: " + ex.Message;
				return RedirectToAction("MembersPerSacco", new { reportDate });
			}
		}

		[HttpPost]
		public async Task<IActionResult> ExportMembersPerSaccoToPdf(DateTime reportDate)
		{
			try
			{
				var companyCode = User.FindFirstValue("CompanyCode");
				var companyName = User.FindFirstValue("CompanyName") ?? "";

				var members = await _context.Members
					.Where(m => m.CompanyCode == companyCode)
					.OrderBy(m => m.MemberNo)
					.ToListAsync();

				var reportData = new List<MemberPerSaccoPdfData>();

				foreach (var m in members)
				{
					string fullName = !string.IsNullOrWhiteSpace(m.FullName?.ToString())
						? m.FullName.ToString()
						: $"{m.Surname ?? ""} {m.OtherNames ?? ""}".Trim();

					if (string.IsNullOrWhiteSpace(fullName))
						fullName = "N/A";

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(m.Sex))
					{
						var s = m.Sex.ToUpper();
						sex = (s == "M" || s == "MALE") ? "MALE"
							 : (s == "F" || s == "FEMALE") ? "FEMALE"
							 : s;
					}

					int? age = null;
					if (m.Dob.HasValue)
					{
						age = DateTime.Now.Year - m.Dob.Value.Year;
						if (DateTime.Now < m.Dob.Value.AddYears(age.Value)) age--;
					}

					string status = "ACTIVE";
					if (m.Withdrawn == true) status = "WITHDRAWN";
					else if (m.Archived == true) status = "ARCHIVED";
					else if (m.Status == 0) status = "INACTIVE";

					reportData.Add(new MemberPerSaccoPdfData
					{
						MemberNo = m.MemberNo,
						FullName = fullName,
						Sex = sex,
						PhoneNo = m.PhoneNo ?? m.MobileNo ?? "-",
						IDNo = m.Idno ?? "-",
						ApplicDate = m.ApplicDate,
						Status = status
					});
				}

				int maleCount = reportData.Count(x => x.Sex == "MALE");
				int femaleCount = reportData.Count(x => x.Sex == "FEMALE");
				int youthCount = reportData.Count(x => x.Age.HasValue && x.Age >= 18 && x.Age <= 35);

				using var stream = new MemoryStream();

				QuestPDF.Fluent.Document.Create(container =>
				{
					container.Page(page =>
					{
						page.Size(PageSizes.A4.Landscape());
						page.Margin(1.5f, Unit.Centimetre);
						page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

						page.Header()
							.AlignCenter()
							.Column(column =>
							{
								column.Item().Text(companyName.ToUpper()).FontSize(18).Bold();
								column.Item().Text($"MEMBERS REGISTER AS AT {reportDate:dd/MM/yyyy}").FontSize(14).Bold();
							});

						page.Content()
							.PaddingVertical(1, Unit.Centimetre)
							.Column(column =>
							{
								column.Item().Table(statsTable =>
								{
									statsTable.ColumnsDefinition(cols =>
									{
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
									});

									statsTable.Cell().Element(c => c.Text($"TOTAL: {reportData.Count}").Bold());
									statsTable.Cell().Element(c => c.Text($"MALE: {maleCount}").Bold());
									statsTable.Cell().Element(c => c.Text($"FEMALE: {femaleCount}").Bold());
									statsTable.Cell().Element(c => c.Text($"YOUTH: {youthCount}").Bold());
								});

								column.Item().PaddingTop(1, Unit.Centimetre);

								column.Item().Table(table =>
								{
									table.ColumnsDefinition(cols =>
									{
										cols.RelativeColumn(1);
										cols.RelativeColumn(2);
										cols.RelativeColumn(0.8f);
										cols.RelativeColumn(1.2f);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
										cols.RelativeColumn(1);
									});

									table.Header(header =>
									{
										header.Cell().Element(c => c.Text("MemberNo").Bold());
										header.Cell().Element(c => c.Text("Names").Bold());
										header.Cell().Element(c => c.Text("Sex").Bold());
										header.Cell().Element(c => c.Text("Phone").Bold());
										header.Cell().Element(c => c.Text("ID").Bold());
										header.Cell().Element(c => c.Text("Date").Bold());
										header.Cell().Element(c => c.Text("Status").Bold());
									});

									foreach (var m in reportData)
									{
										table.Cell().Element(c => c.Text(m.MemberNo ?? ""));
										table.Cell().Element(c => c.Text(m.FullName));
										table.Cell().Element(c => c.Text(m.Sex));
										table.Cell().Element(c => c.Text(m.PhoneNo));
										table.Cell().Element(c => c.Text(m.IDNo));
										table.Cell().Element(c => c.Text(m.ApplicDate?.ToString("dd/MM/yyyy") ?? ""));
										table.Cell().Element(c => c.Text(m.Status));
									}
								});
							});

						page.Footer()
							.AlignRight()
							.Text(x => x.Span($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}"));
					});
				}).GeneratePdf(stream);

				return File(stream.ToArray(),
					"application/pdf",
					$"MembersPerSacco_{reportDate:yyyyMMdd}.pdf");
			}
			catch (Exception ex)
			{
				TempData["Error"] = "Failed to export PDF: " + ex.Message;
				return RedirectToAction("MembersPerSacco", new { reportDate });
			}
		}

		#endregion

		#region Fully Paid Shares Report

		public IActionResult FullyPaidShares()
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";
			var reportDate = DateTime.Now;

			var viewModel = new FullyPaidSharesIndexViewModel
			{
				Members = new List<FullyPaidSharesReportViewModel>(),
				ReportDate = reportDate,
				HasData = false,
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				TotalMembers = 0,
				MaleCount = 0,
				FemaleCount = 0,
				OtherCount = 0,
				TotalShareCapital = 0,
				TotalSavingsDeposits = 0,
				TotalRegistrationFee = 0,
				MinimumShareRequirement = 0,
				RegistrationFeeRequirement = 0
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.HasData = false;

			return View("~/Views/Reports/FullyPaidShares.cshtml", viewModel);
		}
		[HttpPost]
		public async Task<IActionResult> FullyPaidShares(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0;

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == null || m.Withdrawn == false)
					&& (m.Archived == null || m.Archived == false))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			// Get contributions from ContribShares table
			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			// Get shares from Shares table as fallback
			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			var reportData = new List<FullyPaidSharesReportViewModel>();
			int maleCount = 0, femaleCount = 0, otherCount = 0;
			decimal totalShareCapital = 0, totalSavingsDeposits = 0, totalRegistrationFee = 0;

			foreach (var member in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == member.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == member.MemberNo);

				// Get share capital from multiple sources
				decimal shareCapital = 0;
				if (memberContrib != null && memberContrib.TotalShareCapital > 0)
					shareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null && memberShare.TotalShares > 0)
					shareCapital = memberShare.TotalShares;
				else
					shareCapital = member.ShareCap ?? 0;

				// Get savings/deposits
				decimal savingsDeposits = memberContrib?.TotalDeposits ?? 0;

				// Get registration fee
				decimal registrationFee = memberContrib?.TotalRegFee ?? member.RegFee ?? 0;

				// Determine if member is fully paid
				bool isShareCapitalFullyPaid = shareCapital >= minimumShareRequirement;
				bool isRegistrationFeePaid = registrationFee >= registrationFeeRequirement;
				bool hasSavingsDeposits = savingsDeposits > 0;
				bool isFullyPaid = isShareCapitalFullyPaid && isRegistrationFeePaid && hasSavingsDeposits;

				// Build full name
				string fullName = "";
				if (!string.IsNullOrEmpty(member.FullName))
				{
					fullName = member.FullName;
				}
				else
				{
					fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
					if (string.IsNullOrWhiteSpace(fullName))
						fullName = "N/A";
				}

				// Format sex
				string sex = "NOT SPECIFIED";
				if (!string.IsNullOrEmpty(member.Sex))
				{
					string sexUpper = member.Sex.ToUpper();
					if (sexUpper == "M" || sexUpper == "MALE")
					{
						sex = "MALE";
						maleCount++;
					}
					else if (sexUpper == "F" || sexUpper == "FEMALE")
					{
						sex = "FEMALE";
						femaleCount++;
					}
					else
					{
						sex = sexUpper;
						otherCount++;
					}
				}
				else
				{
					otherCount++;
				}

				// Add to totals
				totalShareCapital += shareCapital;
				totalSavingsDeposits += savingsDeposits;
				totalRegistrationFee += registrationFee;

				reportData.Add(new FullyPaidSharesReportViewModel
				{
					MemberNo = member.MemberNo,
					FullName = fullName,
					Sex = sex,
					ShareCapital = shareCapital,
					SavingsDeposits = savingsDeposits,
					RegistrationFee = registrationFee,
					IsFullyPaid = isFullyPaid,
					MinimumShareRequirement = minimumShareRequirement,
					MinimumSavingsRequirement = 0,
					RegistrationFeeRequirement = registrationFeeRequirement
				});
			}

			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			var viewModel = new FullyPaidSharesIndexViewModel
			{
				Members = reportData,
				TotalMembers = reportData.Count,
				MaleCount = maleCount,
				FemaleCount = femaleCount,
				OtherCount = otherCount,
				TotalShareCapital = totalShareCapital,
				TotalSavingsDeposits = totalSavingsDeposits,
				TotalRegistrationFee = totalRegistrationFee,
				ReportDate = reportDate,
				HasData = reportData.Any(),
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				MinimumShareRequirement = minimumShareRequirement,
				RegistrationFeeRequirement = registrationFeeRequirement
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.TotalMembers = reportData.Count;
			ViewBag.TotalShareCapital = totalShareCapital;
			ViewBag.TotalSavingsDeposits = totalSavingsDeposits;
			ViewBag.TotalRegistrationFee = totalRegistrationFee;
			ViewBag.MaleCount = maleCount;
			ViewBag.FemaleCount = femaleCount;
			ViewBag.OtherCount = otherCount;
			ViewBag.MinimumShareRequirement = minimumShareRequirement;
			ViewBag.RegistrationFeeRequirement = registrationFeeRequirement;
			ViewBag.HasData = reportData.Any();

			return View("~/Views/Reports/FullyPaidShares.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> ExportFullyPaidSharesToExcel(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0;

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == null || m.Withdrawn == false)
					&& (m.Archived == null || m.Archived == false))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			var reportData = new List<dynamic>();

			foreach (var member in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == member.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == member.MemberNo);

				decimal shareCapital = 0;
				if (memberContrib != null)
					shareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null)
					shareCapital = memberShare.TotalShares;
				else
					shareCapital = member.ShareCap ?? 0;

				decimal savingsDeposits = memberContrib?.TotalDeposits ?? 0;
				decimal registrationFee = memberContrib?.TotalRegFee ?? member.RegFee ?? 0;

				if (shareCapital >= minimumShareRequirement && registrationFee >= registrationFeeRequirement && savingsDeposits > 0)
				{
					string fullName = "";
					if (member.FullName != null)
						fullName = member.FullName.ToString();
					else
						fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(member.Sex))
					{
						string sexUpper = member.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					reportData.Add(new
					{
						member.MemberNo,
						FullName = fullName,
						Sex = sex,
						ShareCapital = shareCapital,
						SavingsDeposits = savingsDeposits,
						RegistrationFee = registrationFee
					});
				}
			}

			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			using (var workbook = new XLWorkbook())
			{
				var worksheet = workbook.Worksheets.Add("Fully Paid Shares");
				var currentRow = 1;

				worksheet.Cell(currentRow, 1).Value = companyName.ToUpper();
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 1).Style.Font.FontSize = 18;
				worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				currentRow += 2;

				worksheet.Cell(currentRow, 1).Value = $"FULLY PAID SACCO SHARES AS AT {reportDate:dd/MM/yyyy}";
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
				worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				currentRow += 2;

				var headers = new[] { "#", "MEMBERNO", "NAMES", "SEX", "SHARE CAPITAL", "SAVINGS/DEPOSITS", "REGISTRATION FEE" };

				for (int i = 0; i < headers.Length; i++)
				{
					worksheet.Cell(currentRow, i + 1).Value = headers[i];
					worksheet.Cell(currentRow, i + 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
					worksheet.Cell(currentRow, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
					worksheet.Cell(currentRow, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				}
				currentRow++;

				int serialNo = 1;
				foreach (var member in reportData)
				{
					worksheet.Cell(currentRow, 1).Value = serialNo++;
					worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

					worksheet.Cell(currentRow, 2).Value = member.MemberNo;
					worksheet.Cell(currentRow, 3).Value = member.FullName;
					worksheet.Cell(currentRow, 4).Value = member.Sex;
					worksheet.Cell(currentRow, 5).Value = member.ShareCapital;
					worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 6).Value = member.SavingsDeposits;
					worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 7).Value = member.RegistrationFee;
					worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";

					worksheet.Range(currentRow, 1, currentRow, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
					currentRow++;
				}

				currentRow += 2;
				worksheet.Cell(currentRow, 4).Value = "GRAND TOTAL:";
				worksheet.Cell(currentRow, 4).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

				worksheet.Cell(currentRow, 5).Value = reportData.Sum(m => (decimal)m.ShareCapital);
				worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";

				worksheet.Cell(currentRow, 6).Value = reportData.Sum(m => (decimal)m.SavingsDeposits);
				worksheet.Cell(currentRow, 6).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";

				worksheet.Cell(currentRow, 7).Value = reportData.Sum(m => (decimal)m.RegistrationFee);
				worksheet.Cell(currentRow, 7).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";

				currentRow += 3;
				worksheet.Cell(currentRow, 1).Value = "TOTAL MEMBERS:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count;
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow++;
				worksheet.Cell(currentRow, 1).Value = "MALE:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count(m => m.Sex == "MALE");
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow++;
				worksheet.Cell(currentRow, 1).Value = "FEMALE:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count(m => m.Sex == "FEMALE");
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow++;
				worksheet.Cell(currentRow, 1).Value = "OTHERS:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE" && m.Sex != "NOT SPECIFIED");
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow += 2;
				worksheet.Cell(currentRow, 1).Value = $"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Italic = true;

				worksheet.Columns().AdjustToContents();

				using (var stream = new MemoryStream())
				{
					workbook.SaveAs(stream);
					var content = stream.ToArray();
					return File(content,
						"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
						$"FullyPaidShares_{reportDate:yyyyMMdd}.xlsx");
				}
			}
		}

		[HttpPost]
		public async Task<IActionResult> ExportFullyPaidSharesToPdf(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0;

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == null || m.Withdrawn == false)
					&& (m.Archived == null || m.Archived == false))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			var reportData = new List<FullyPaidMemberData>();

			foreach (var member in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == member.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == member.MemberNo);

				decimal shareCapital = 0;
				if (memberContrib != null)
					shareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null)
					shareCapital = memberShare.TotalShares;
				else
					shareCapital = member.ShareCap ?? 0;

				decimal savingsDeposits = memberContrib?.TotalDeposits ?? 0;
				decimal registrationFee = memberContrib?.TotalRegFee ?? member.RegFee ?? 0;

				if (shareCapital >= minimumShareRequirement && registrationFee >= registrationFeeRequirement && savingsDeposits > 0)
				{
					string fullName = "";
					if (member.FullName != null)
						fullName = member.FullName.ToString();
					else
						fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(member.Sex))
					{
						string sexUpper = member.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					reportData.Add(new FullyPaidMemberData
					{
						MemberNo = member.MemberNo,
						FullName = fullName,
						Sex = sex,
						ShareCapital = shareCapital,
						SavingsDeposits = savingsDeposits,
						RegistrationFee = registrationFee
					});
				}
			}

			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
			int otherCount = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE" && m.Sex != "NOT SPECIFIED");

			using var stream = new MemoryStream();

			QuestPDF.Fluent.Document.Create(container =>
			{
				container.Page(page =>
				{
					page.Size(PageSizes.A4.Landscape());
					page.Margin(1.5f, Unit.Centimetre);
					page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

					page.Header()
						.AlignCenter()
						.Column(column =>
						{
							column.Item().Text(companyName.ToUpper()).FontSize(18).Bold();
							column.Item().Text($"FULLY PAID SACCO SHARES AS AT {reportDate:dd/MM/yyyy}").FontSize(14).Bold();
						});

					page.Content()
						.PaddingVertical(1, Unit.Centimetre)
						.Column(column =>
						{
							column.Item().Table(table =>
							{
								table.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(0.5f);
									cols.RelativeColumn(1f);
									cols.RelativeColumn(2f);
									cols.RelativeColumn(0.8f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
								});

								table.Header(header =>
								{
									header.Cell().Element(c => c.Text("#").Bold());
									header.Cell().Element(c => c.Text("MEMBERNO").Bold());
									header.Cell().Element(c => c.Text("NAMES").Bold());
									header.Cell().Element(c => c.Text("SEX").Bold());
									header.Cell().Element(c => c.Text("SHARE CAPITAL").Bold());
									header.Cell().Element(c => c.Text("SAVINGS/DEPOSITS").Bold());
									header.Cell().Element(c => c.Text("REG FEE").Bold());
								});

								int serialNo = 1;
								foreach (var member in reportData)
								{
									table.Cell().Element(c => c.Text(serialNo++.ToString()));
									table.Cell().Element(c => c.Text(member.MemberNo ?? ""));
									table.Cell().Element(c => c.Text(member.FullName ?? ""));
									table.Cell().Element(c => c.Text(member.Sex ?? ""));
									table.Cell().Element(c => c.AlignRight().Text($"{member.ShareCapital:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{member.SavingsDeposits:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{member.RegistrationFee:N0}"));
								}
							});

							column.Item().PaddingTop(1, Unit.Centimetre);

							column.Item().Table(grandTable =>
							{
								grandTable.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(4);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
								});

								grandTable.Cell().ColumnSpan(1).Element(c => c.AlignRight().Text("GRAND TOTAL:").Bold());
								grandTable.Cell().Element(c => c.AlignRight().Text($"{reportData.Sum(m => m.ShareCapital):N0}").Bold());
								grandTable.Cell().Element(c => c.AlignRight().Text($"{reportData.Sum(m => m.SavingsDeposits):N0}").Bold());
								grandTable.Cell().Element(c => c.AlignRight().Text($"{reportData.Sum(m => m.RegistrationFee):N0}").Bold());
							});

							column.Item().PaddingTop(1, Unit.Centimetre);

							// Statistics - Use a container with width instead of Table.Width
							column.Item().Width(200, Unit.Point).Table(statsTable =>
							{
								statsTable.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
								});

								statsTable.Cell().Element(c => c.Text("TOTAL MEMBERS:").Bold());
								statsTable.Cell().Element(c => c.Text(reportData.Count.ToString()).Bold());

								statsTable.Cell().Element(c => c.Text("MALE:").Bold());
								statsTable.Cell().Element(c => c.Text(maleCount.ToString("N0")).Bold());

								statsTable.Cell().Element(c => c.Text("FEMALE:").Bold());
								statsTable.Cell().Element(c => c.Text(femaleCount.ToString("N0")).Bold());

								statsTable.Cell().Element(c => c.Text("OTHERS:").Bold());
								statsTable.Cell().Element(c => c.Text(otherCount.ToString("N0")).Bold());
							});
						});

					page.Footer()
						.AlignRight()
						.Text(x => x.Span($"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}"));
				});
			}).GeneratePdf(stream);

			var content = stream.ToArray();
			return File(content, "application/pdf", $"FullyPaidShares_{reportDate:yyyyMMdd}.pdf");
		}

		
		#endregion

		#region Partially Paid Shares Report

		public IActionResult PartiallyPaidShares()
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";
			var reportDate = DateTime.Now;

			var viewModel = new PartiallyPaidSharesIndexViewModel
			{
				Members = new List<PartiallyPaidSharesReportViewModel>(),
				ReportDate = reportDate,
				HasData = false,
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				TotalMembers = 0,
				MaleCount = 0,
				FemaleCount = 0,
				OtherCount = 0,
				TotalShareCapital = 0,
				TotalSavingsDeposits = 0,
				TotalRegistrationFee = 0,
				MembersMissingShareCapital = 0,
				MembersMissingRegistrationFee = 0,
				MembersMissingSavings = 0
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.HasData = false;

			return View("~/Views/Reports/PartiallyPaidShares.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> PartiallyPaidShares(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0;

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == null || m.Withdrawn == false)
					&& (m.Archived == null || m.Archived == false))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			var reportData = new List<PartiallyPaidSharesReportViewModel>();
			int missingShareCapitalCount = 0;
			int missingRegistrationFeeCount = 0;
			int missingSavingsCount = 0;

			foreach (var member in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == member.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == member.MemberNo);

				decimal shareCapital = 0;
				if (memberContrib != null)
					shareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null)
					shareCapital = memberShare.TotalShares;
				else
					shareCapital = member.ShareCap ?? 0;

				decimal savingsDeposits = memberContrib?.TotalDeposits ?? 0;
				decimal registrationFee = memberContrib?.TotalRegFee ?? member.RegFee ?? 0;

				bool hasPaidShareCapital = minimumShareRequirement == 0 ? true : shareCapital >= minimumShareRequirement;
				bool hasPaidRegistrationFee = registrationFeeRequirement == 0 ? true : registrationFee >= registrationFeeRequirement;
				bool hasSavingsDeposits = savingsDeposits > 0;

				bool missingShareCapital = !hasPaidShareCapital && minimumShareRequirement > 0;
				bool missingRegistrationFee = !hasPaidRegistrationFee && registrationFeeRequirement > 0;
				bool missingSavings = !hasSavingsDeposits;

				if (missingShareCapital) missingShareCapitalCount++;
				if (missingRegistrationFee) missingRegistrationFeeCount++;
				if (missingSavings) missingSavingsCount++;

				if (!hasPaidShareCapital || !hasPaidRegistrationFee || !hasSavingsDeposits)
				{
					string fullName = "";
					if (member.FullName != null)
					{
						fullName = member.FullName.ToString();
					}
					else
					{
						fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
						if (string.IsNullOrWhiteSpace(fullName))
							fullName = "N/A";
					}

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(member.Sex))
					{
						string sexUpper = member.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					reportData.Add(new PartiallyPaidSharesReportViewModel
					{
						MemberNo = member.MemberNo,
						FullName = fullName,
						Sex = sex,
						ShareCapital = shareCapital,
						SavingsDeposits = savingsDeposits,
						RegistrationFee = registrationFee,
						HasPaidShareCapital = hasPaidShareCapital,
						HasPaidRegistrationFee = hasPaidRegistrationFee,
						HasSavingsDeposits = hasSavingsDeposits,
						MissingShareCapital = missingShareCapital,
						MissingRegistrationFee = missingRegistrationFee,
						MissingSavingsDeposits = missingSavings
					});
				}
			}

			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
			int otherCount = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE" && m.Sex != "NOT SPECIFIED");

			var viewModel = new PartiallyPaidSharesIndexViewModel
			{
				Members = reportData,
				TotalMembers = reportData.Count,
				MaleCount = maleCount,
				FemaleCount = femaleCount,
				OtherCount = otherCount,
				TotalShareCapital = reportData.Sum(m => m.ShareCapital),
				TotalSavingsDeposits = reportData.Sum(m => m.SavingsDeposits),
				TotalRegistrationFee = reportData.Sum(m => m.RegistrationFee),
				ReportDate = reportDate,
				HasData = reportData.Any(),
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				MembersMissingShareCapital = missingShareCapitalCount,
				MembersMissingRegistrationFee = missingRegistrationFeeCount,
				MembersMissingSavings = missingSavingsCount
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.TotalMembers = reportData.Count;
			ViewBag.TotalShareCapital = reportData.Sum(m => m.ShareCapital);
			ViewBag.TotalSavingsDeposits = reportData.Sum(m => m.SavingsDeposits);
			ViewBag.TotalRegistrationFee = reportData.Sum(m => m.RegistrationFee);
			ViewBag.MaleCount = maleCount;
			ViewBag.FemaleCount = femaleCount;
			ViewBag.OtherCount = otherCount;
			ViewBag.HasData = reportData.Any();

			return View("~/Views/Reports/PartiallyPaidShares.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> ExportPartiallyPaidSharesToExcel(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0;

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == null || m.Withdrawn == false)
					&& (m.Archived == null || m.Archived == false))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			var reportData = new List<dynamic>();

			foreach (var member in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == member.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == member.MemberNo);

				decimal shareCapital = 0;
				if (memberContrib != null)
					shareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null)
					shareCapital = memberShare.TotalShares;
				else
					shareCapital = member.ShareCap ?? 0;

				decimal savingsDeposits = memberContrib?.TotalDeposits ?? 0;
				decimal registrationFee = memberContrib?.TotalRegFee ?? member.RegFee ?? 0;

				bool hasPaidShareCapital = minimumShareRequirement == 0 ? true : shareCapital >= minimumShareRequirement;
				bool hasPaidRegistrationFee = registrationFeeRequirement == 0 ? true : registrationFee >= registrationFeeRequirement;
				bool hasSavingsDeposits = savingsDeposits > 0;

				if (!hasPaidShareCapital || !hasPaidRegistrationFee || !hasSavingsDeposits)
				{
					string fullName = "";
					if (member.FullName != null)
						fullName = member.FullName.ToString();
					else
						fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(member.Sex))
					{
						string sexUpper = member.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					reportData.Add(new
					{
						member.MemberNo,
						FullName = fullName,
						Sex = sex,
						ShareCapital = shareCapital,
						SavingsDeposits = savingsDeposits,
						RegistrationFee = registrationFee
					});
				}
			}

			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			using (var workbook = new XLWorkbook())
			{
				var worksheet = workbook.Worksheets.Add("Partially Paid Shares");
				var currentRow = 1;

				worksheet.Cell(currentRow, 1).Value = companyName.ToUpper();
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 1).Style.Font.FontSize = 18;
				worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				currentRow += 2;

				worksheet.Cell(currentRow, 1).Value = $"PARTIALLY PAID SACCO SHARES AS AT {reportDate:dd/MM/yyyy}";
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 1).Style.Font.FontSize = 14;
				worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				currentRow += 2;

				var headers = new[] { "#", "MEMBERNO", "NAMES", "SEX", "SHARE CAPITAL", "SAVINGS/DEPOSITS", "REGISTRATION FEE" };

				for (int i = 0; i < headers.Length; i++)
				{
					worksheet.Cell(currentRow, i + 1).Value = headers[i];
					worksheet.Cell(currentRow, i + 1).Style.Font.Bold = true;
					worksheet.Cell(currentRow, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
					worksheet.Cell(currentRow, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
					worksheet.Cell(currentRow, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
				}
				currentRow++;

				int serialNo = 1;
				foreach (var member in reportData)
				{
					worksheet.Cell(currentRow, 1).Value = serialNo++;
					worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

					worksheet.Cell(currentRow, 2).Value = member.MemberNo;
					worksheet.Cell(currentRow, 3).Value = member.FullName;
					worksheet.Cell(currentRow, 4).Value = member.Sex;
					worksheet.Cell(currentRow, 5).Value = member.ShareCapital;
					worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 6).Value = member.SavingsDeposits;
					worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";
					worksheet.Cell(currentRow, 7).Value = member.RegistrationFee;
					worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";

					worksheet.Range(currentRow, 1, currentRow, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
					currentRow++;
				}

				currentRow += 2;
				worksheet.Cell(currentRow, 4).Value = "GRAND TOTAL:";
				worksheet.Cell(currentRow, 4).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

				worksheet.Cell(currentRow, 5).Value = reportData.Sum(m => (decimal)m.ShareCapital);
				worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0.00";

				worksheet.Cell(currentRow, 6).Value = reportData.Sum(m => (decimal)m.SavingsDeposits);
				worksheet.Cell(currentRow, 6).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";

				worksheet.Cell(currentRow, 7).Value = reportData.Sum(m => (decimal)m.RegistrationFee);
				worksheet.Cell(currentRow, 7).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";

				currentRow += 3;
				worksheet.Cell(currentRow, 1).Value = "TOTAL MEMBERS:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count;
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow++;
				worksheet.Cell(currentRow, 1).Value = "MALE:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count(m => m.Sex == "MALE");
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow++;
				worksheet.Cell(currentRow, 1).Value = "FEMALE:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count(m => m.Sex == "FEMALE");
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow++;
				worksheet.Cell(currentRow, 1).Value = "OTHERS:";
				worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
				worksheet.Cell(currentRow, 2).Value = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE" && m.Sex != "NOT SPECIFIED");
				worksheet.Cell(currentRow, 2).Style.Font.Bold = true;

				currentRow += 2;
				worksheet.Cell(currentRow, 1).Value = $"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
				worksheet.Range(currentRow, 1, currentRow, 7).Merge();
				worksheet.Cell(currentRow, 1).Style.Font.Italic = true;

				worksheet.Columns().AdjustToContents();

				using (var stream = new MemoryStream())
				{
					workbook.SaveAs(stream);
					var content = stream.ToArray();
					return File(content,
						"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
						$"PartiallyPaidShares_{reportDate:yyyyMMdd}.xlsx");
				}
			}
		}

		[HttpPost]
		public async Task<IActionResult> ExportPartiallyPaidSharesToPdf(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var mainShareType = await _context.Sharetypes
				.Where(st => st.CompanyCode == companyCode && st.IsMainShares == true)
				.FirstOrDefaultAsync();

			decimal minimumShareRequirement = mainShareType?.MinAmount ?? 0;
			decimal registrationFeeRequirement = 0;

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == null || m.Withdrawn == false)
					&& (m.Archived == null || m.Archived == false))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var contribShares = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo)
					&& cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(cs => cs.ShareCapitalAmount ?? 0),
					TotalDeposits = g.Sum(cs => cs.DepositsAmount ?? 0),
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToListAsync();

			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo)
					&& s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShares = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToListAsync();

			var reportData = new List<PartiallyPaidMemberData>();

			foreach (var member in members)
			{
				var memberContrib = contribShares.FirstOrDefault(c => c.MemberNo == member.MemberNo);
				var memberShare = shares.FirstOrDefault(s => s.MemberNo == member.MemberNo);

				decimal shareCapital = 0;
				if (memberContrib != null)
					shareCapital = memberContrib.TotalShareCapital;
				else if (memberShare != null)
					shareCapital = memberShare.TotalShares;
				else
					shareCapital = member.ShareCap ?? 0;

				decimal savingsDeposits = memberContrib?.TotalDeposits ?? 0;
				decimal registrationFee = memberContrib?.TotalRegFee ?? member.RegFee ?? 0;

				bool hasPaidShareCapital = minimumShareRequirement == 0 ? true : shareCapital >= minimumShareRequirement;
				bool hasPaidRegistrationFee = registrationFeeRequirement == 0 ? true : registrationFee >= registrationFeeRequirement;
				bool hasSavingsDeposits = savingsDeposits > 0;

				if (!hasPaidShareCapital || !hasPaidRegistrationFee || !hasSavingsDeposits)
				{
					string fullName = "";
					if (member.FullName != null)
						fullName = member.FullName.ToString();
					else
						fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();

					string sex = "NOT SPECIFIED";
					if (!string.IsNullOrEmpty(member.Sex))
					{
						string sexUpper = member.Sex.ToUpper();
						if (sexUpper == "M" || sexUpper == "MALE")
							sex = "MALE";
						else if (sexUpper == "F" || sexUpper == "FEMALE")
							sex = "FEMALE";
						else
							sex = sexUpper;
					}

					reportData.Add(new PartiallyPaidMemberData
					{
						MemberNo = member.MemberNo,
						FullName = fullName,
						Sex = sex,
						ShareCapital = shareCapital,
						SavingsDeposits = savingsDeposits,
						RegistrationFee = registrationFee
					});
				}
			}

			reportData = reportData.OrderBy(m => m.MemberNo).ToList();

			int maleCount = reportData.Count(m => m.Sex == "MALE");
			int femaleCount = reportData.Count(m => m.Sex == "FEMALE");
			int otherCount = reportData.Count(m => m.Sex != "MALE" && m.Sex != "FEMALE" && m.Sex != "NOT SPECIFIED");

			using var stream = new MemoryStream();

			QuestPDF.Fluent.Document.Create(container =>
			{
				container.Page(page =>
				{
					page.Size(PageSizes.A4.Landscape());
					page.Margin(1.5f, Unit.Centimetre);
					page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

					page.Header()
						.AlignCenter()
						.Column(column =>
						{
							column.Item().Text(companyName.ToUpper()).FontSize(18).Bold();
							column.Item().Text($"PARTIALLY PAID SACCO SHARES AS AT {reportDate:dd/MM/yyyy}").FontSize(14).Bold();
						});

					page.Content()
						.PaddingVertical(1, Unit.Centimetre)
						.Column(column =>
						{
							column.Item().Table(table =>
							{
								table.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(0.5f);
									cols.RelativeColumn(1f);
									cols.RelativeColumn(2f);
									cols.RelativeColumn(0.8f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
									cols.RelativeColumn(1.2f);
								});

								table.Header(header =>
								{
									header.Cell().Element(c => c.Text("#").Bold());
									header.Cell().Element(c => c.Text("MEMBERNO").Bold());
									header.Cell().Element(c => c.Text("NAMES").Bold());
									header.Cell().Element(c => c.Text("SEX").Bold());
									header.Cell().Element(c => c.Text("SHARE CAPITAL").Bold());
									header.Cell().Element(c => c.Text("SAVINGS/DEPOSITS").Bold());
									header.Cell().Element(c => c.Text("REG FEE").Bold());
								});

								int serialNo = 1;
								foreach (var member in reportData)
								{
									table.Cell().Element(c => c.Text(serialNo++.ToString()));
									table.Cell().Element(c => c.Text(member.MemberNo ?? ""));
									table.Cell().Element(c => c.Text(member.FullName ?? ""));
									table.Cell().Element(c => c.Text(member.Sex ?? ""));
									table.Cell().Element(c => c.AlignRight().Text($"{member.ShareCapital:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{member.SavingsDeposits:N0}"));
									table.Cell().Element(c => c.AlignRight().Text($"{member.RegistrationFee:N0}"));
								}
							});

							column.Item().PaddingTop(1, Unit.Centimetre);

							column.Item().Table(grandTable =>
							{
								grandTable.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(4);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
								});

								grandTable.Cell().ColumnSpan(1).Element(c => c.AlignRight().Text("GRAND TOTAL:").Bold());
								grandTable.Cell().Element(c => c.AlignRight().Text($"{reportData.Sum(m => m.ShareCapital):N0}").Bold());
								grandTable.Cell().Element(c => c.AlignRight().Text($"{reportData.Sum(m => m.SavingsDeposits):N0}").Bold());
								grandTable.Cell().Element(c => c.AlignRight().Text($"{reportData.Sum(m => m.RegistrationFee):N0}").Bold());
							});

							column.Item().PaddingTop(1, Unit.Centimetre);

							// Statistics - Use a container with width instead of Table.Width
							column.Item().Width(200, Unit.Point).Table(statsTable =>
							{
								statsTable.ColumnsDefinition(cols =>
								{
									cols.RelativeColumn(1);
									cols.RelativeColumn(1);
								});

								statsTable.Cell().Element(c => c.Text("TOTAL MEMBERS:").Bold());
								statsTable.Cell().Element(c => c.Text(reportData.Count.ToString()).Bold());

								statsTable.Cell().Element(c => c.Text("MALE:").Bold());
								statsTable.Cell().Element(c => c.Text(maleCount.ToString("N0")).Bold());

								statsTable.Cell().Element(c => c.Text("FEMALE:").Bold());
								statsTable.Cell().Element(c => c.Text(femaleCount.ToString("N0")).Bold());

								statsTable.Cell().Element(c => c.Text("OTHERS:").Bold());
								statsTable.Cell().Element(c => c.Text(otherCount.ToString("N0")).Bold());
							});
						});

					page.Footer()
						.AlignRight()
						.Text(x => x.Span($"Report Generated: {DateTime.Now:dd/MM/yyyy HH:mm:ss}"));
				});
			}).GeneratePdf(stream);

			var content = stream.ToArray();
			return File(content, "application/pdf", $"PartiallyPaidShares_{reportDate:yyyyMMdd}.pdf");
		}


		#endregion

		#region Helper Classes

		public class ActiveMemberPdfData
		{
			public string MemberNo { get; set; }
			public string Name { get; set; }
			public string Sex { get; set; }
			public decimal Share { get; set; }
			public decimal Deposits { get; set; }
			public decimal RegFee { get; set; }
		}

		public class InactiveMemberPdfData
		{
			public string MemberNo { get; set; }
			public string FullName { get; set; }
			public string Sex { get; set; }
			public decimal ShareCapital { get; set; }
			public decimal SavingsDeposits { get; set; }
			public decimal RegFee { get; set; }
			public string Status { get; set; }
		}

		public class MemberPerSaccoPdfData
		{
			public string MemberNo { get; set; }
			public string FullName { get; set; }
			public string Sex { get; set; }
			public string PhoneNo { get; set; }
			public string IDNo { get; set; }
			public DateTime? ApplicDate { get; set; }
			public int? Age { get; set; }
			public string Status { get; set; }
		}

		public class FullyPaidMemberData
		{
			public string MemberNo { get; set; }
			public string FullName { get; set; }
			public string Sex { get; set; }
			public decimal ShareCapital { get; set; }
			public decimal SavingsDeposits { get; set; }
			public decimal RegistrationFee { get; set; }
		}

		public class PartiallyPaidMemberData
		{
			public string MemberNo { get; set; }
			public string FullName { get; set; }
			public string Sex { get; set; }
			public decimal ShareCapital { get; set; }
			public decimal SavingsDeposits { get; set; }
			public decimal RegistrationFee { get; set; }
		}

		#endregion

		#region Shares and Loans Report

		[HttpGet]
		public IActionResult SharesLoansPERSacco()
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";
			var reportDate = DateTime.Now;

			var viewModel = new SharesAndLoansIndexViewModel
			{
				Members = new List<SharesAndLoansReportViewModel>(),
				ReportDate = reportDate,
				HasData = false,
				UserCompanyCode = companyCode,
				CompanyName = companyName,
				TotalMembers = 0,
				MaleCount = 0,
				FemaleCount = 0,
				OtherCount = 0,
				YouthCount = 0,
				TotalShareCapital = 0,
				TotalDeposits = 0,
				TotalRegFee = 0,
				TotalPassbook = 0,
				TotalLoans = 0,
				TotalOutstandingBalance = 0
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.CompanyName = companyName;
			ViewBag.HasData = false;
            // return RedirectToAction("SharesLoansPERSacco", "MemberReport");

            return View("~/Views/Reports/SharesLoansPERSacco.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> SharesLoansPERSacco(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			// Get all active members
			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == false || m.Withdrawn == null)
					&& (m.Archived == false || m.Archived == null))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			// Get SHARE CAPITAL from Shares table (sum of TotalShares for each member)
			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo) && s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalShareCapital = g.Sum(s => s.TotalShares ?? 0)
				})
				.ToDictionaryAsync(s => s.MemberNo, s => s.TotalShareCapital);

			// Get DEPOSITS/SAVINGS from Contribs table
			var savings = await _context.Contribs
				.Where(c => memberNos.Contains(c.MemberNo) && c.CompanyCode == companyCode)
				.GroupBy(c => c.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalSavings = g.Sum(c => c.Amount ?? 0)
				})
				.ToDictionaryAsync(c => c.MemberNo, c => c.TotalSavings);

			// Get REGISTRATION FEE from ContribShares and Member table
			var regFees = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo) && cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0)
				})
				.ToDictionaryAsync(cs => cs.MemberNo, cs => cs.TotalRegFee);

			// Get LOANS (total disbursed loan amount) from Loans table
			var loans = await _context.Loans
				.Where(l => memberNos.Contains(l.MemberNo)
					&& l.CompanyCode == companyCode
					&& l.Status == (int)Status.Disbursed)
				.GroupBy(l => l.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalLoans = g.Sum(l => l.LoanAmt ?? 0)
				})
				.ToDictionaryAsync(l => l.MemberNo, l => l.TotalLoans);

			// Get PASSBOOK amount (if you have a passbook table, otherwise use 0)
			// Passbook typically represents statement balance or special savings
			var passbook = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo) && cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new
				{
					MemberNo = g.Key,
					TotalPassbook = g.Sum(cs => cs.PassBookAmount ?? 0)
				})
				.ToDictionaryAsync(cs => cs.MemberNo, cs => cs.TotalPassbook);

			// Get CIG/GIG names from Member's Cigcode
			var gigCodes = members.Where(m => !string.IsNullOrEmpty(m.Cigcode))
				.Select(m => m.Cigcode)
				.Distinct()
				.ToList();

			var gigDetails = await _context.CIGs
				.Where(g => gigCodes.Contains(g.GigCode) && g.CompanyCode == companyCode)
				.ToDictionaryAsync(g => g.GigCode, g => g.GigName);

			var reportData = new List<SharesAndLoansReportViewModel>();
			int maleCount = 0, femaleCount = 0, otherCount = 0, youthCount = 0;
			decimal totalShareCapital = 0, totalDeposits = 0, totalRegFee = 0, totalPassbook = 0, totalLoans = 0;

			foreach (var member in members)
			{
				// Calculate age
				int? age = null;
				if (member.Dob.HasValue)
				{
					age = DateTime.Now.Year - member.Dob.Value.Year;
					if (DateTime.Now < member.Dob.Value.AddYears(age.Value)) age--;
					if (age >= 18 && age <= 35) youthCount++;
				}

				// Build full name
				string fullName = "N/A";
				if (!string.IsNullOrWhiteSpace(member.Surname) || !string.IsNullOrWhiteSpace(member.OtherNames))
				{
					fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
					if (string.IsNullOrWhiteSpace(fullName))
						fullName = "N/A";
				}

				// Get GIG Name
				string gigName = "UNASSIGNED";
				if (!string.IsNullOrEmpty(member.Cigcode) && gigDetails.ContainsKey(member.Cigcode))
				{
					gigName = gigDetails[member.Cigcode];
				}
				else if (!string.IsNullOrEmpty(member.Cigcode))
				{
					gigName = member.Cigcode;
				}

				// Count gender
				if (member.Sex?.ToUpper() == "MALE" || member.Sex?.ToUpper() == "M")
				{
					maleCount++;
				}
				else if (member.Sex?.ToUpper() == "FEMALE" || member.Sex?.ToUpper() == "F")
				{
					femaleCount++;
				}
				else
				{
					otherCount++;
				}

				// Get financial values
				decimal shareCapital = shares.ContainsKey(member.MemberNo) ? shares[member.MemberNo] : (member.ShareCap ?? 0);
				decimal deposits = savings.ContainsKey(member.MemberNo) ? savings[member.MemberNo] : 0;
				decimal regFee = regFees.ContainsKey(member.MemberNo) ? regFees[member.MemberNo] : (member.RegFee ?? 0);
				decimal passbookAmount = passbook.ContainsKey(member.MemberNo) ? passbook[member.MemberNo] : 0;
				decimal loanAmount = loans.ContainsKey(member.MemberNo) ? loans[member.MemberNo] : 0;

				totalShareCapital += shareCapital;
				totalDeposits += deposits;
				totalRegFee += regFee;
				totalPassbook += passbookAmount;
				totalLoans += loanAmount;

				reportData.Add(new SharesAndLoansReportViewModel
				{
					MemberNo = member.MemberNo,
					FullName = fullName,
					Age = age,
					CIGName = gigName,
					ShareCapital = shareCapital,
					Deposits = deposits,
					RegFee = regFee,
					Passbook = passbookAmount,
					TotalLoans = loanAmount,
					DateRegistered = member.ApplicDate,
					Sex = member.Sex ?? "Not Specified"
				});
			}

			var viewModel = new SharesAndLoansIndexViewModel
			{
				Members = reportData.OrderBy(m => m.MemberNo).ToList(),
				TotalMembers = reportData.Count,
				MaleCount = maleCount,
				FemaleCount = femaleCount,
				OtherCount = otherCount,
				YouthCount = youthCount,
				TotalShareCapital = totalShareCapital,
				TotalDeposits = totalDeposits,
				TotalRegFee = totalRegFee,
				TotalPassbook = totalPassbook,
				TotalLoans = totalLoans,
				ReportDate = reportDate,
				HasData = reportData.Any(),
				UserCompanyCode = companyCode,
				CompanyName = companyName
			};

			ViewBag.ReportDate = reportDate;
			ViewBag.CompanyName = companyName;
			ViewBag.TotalMembers = reportData.Count;
			ViewBag.TotalShareCapital = totalShareCapital;
			ViewBag.TotalDeposits = totalDeposits;
			ViewBag.TotalRegFee = totalRegFee;
			ViewBag.TotalPassbook = totalPassbook;
			ViewBag.TotalLoans = totalLoans;
			ViewBag.MaleCount = maleCount;
			ViewBag.FemaleCount = femaleCount;
			ViewBag.YouthCount = youthCount;
			ViewBag.HasData = reportData.Any();

			return View("~/Views/Reports/SharesLoansPERSacco.cshtml", viewModel);
		}

		[HttpPost]
		public async Task<IActionResult> ExportSharesAndLoansToExcel(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == false || m.Withdrawn == null)
					&& (m.Archived == false || m.Archived == null))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo) && s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalShareCapital = g.Sum(s => s.TotalShares ?? 0) })
				.ToDictionaryAsync(s => s.MemberNo, s => s.TotalShareCapital);

			var savings = await _context.Contribs
				.Where(c => memberNos.Contains(c.MemberNo) && c.CompanyCode == companyCode)
				.GroupBy(c => c.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalSavings = g.Sum(c => c.Amount ?? 0) })
				.ToDictionaryAsync(c => c.MemberNo, c => c.TotalSavings);

			var regFees = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo) && cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0) })
				.ToDictionaryAsync(cs => cs.MemberNo, cs => cs.TotalRegFee);

			var loans = await _context.Loans
				.Where(l => memberNos.Contains(l.MemberNo) && l.CompanyCode == companyCode && l.Status == (int)Status.Disbursed)
				.GroupBy(l => l.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalLoans = g.Sum(l => l.LoanAmt ?? 0) })
				.ToDictionaryAsync(l => l.MemberNo, l => l.TotalLoans);

			var passbook = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo) && cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalPassbook = g.Sum(cs => cs.PassBookAmount ?? 0) })
				.ToDictionaryAsync(cs => cs.MemberNo, cs => cs.TotalPassbook);

			var gigCodes = members.Where(m => !string.IsNullOrEmpty(m.Cigcode)).Select(m => m.Cigcode).Distinct().ToList();
			var gigDetails = await _context.CIGs
				.Where(g => gigCodes.Contains(g.GigCode) && g.CompanyCode == companyCode)
				.ToDictionaryAsync(g => g.GigCode, g => g.GigName);

			var reportData = new List<dynamic>();

			foreach (var member in members)
			{
				string fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
				if (string.IsNullOrWhiteSpace(fullName)) fullName = "N/A";

				int? age = null;
				if (member.Dob.HasValue)
				{
					age = DateTime.Now.Year - member.Dob.Value.Year;
					if (DateTime.Now < member.Dob.Value.AddYears(age.Value)) age--;
				}

				string gigName = "UNASSIGNED";
				if (!string.IsNullOrEmpty(member.Cigcode) && gigDetails.ContainsKey(member.Cigcode))
					gigName = gigDetails[member.Cigcode];
				else if (!string.IsNullOrEmpty(member.Cigcode))
					gigName = member.Cigcode;

				string sex = "NOT SPECIFIED";
				if (!string.IsNullOrEmpty(member.Sex))
				{
					string sexUpper = member.Sex.ToUpper();
					if (sexUpper == "M" || sexUpper == "MALE")
						sex = "MALE";
					else if (sexUpper == "F" || sexUpper == "FEMALE")
						sex = "FEMALE";
					else
						sex = sexUpper;
				}

				reportData.Add(new
				{
					member.MemberNo,
					Names = fullName,
					Age = age,
					CIGName = gigName,
					ShareCapital = shares.ContainsKey(member.MemberNo) ? shares[member.MemberNo] : (member.ShareCap ?? 0),
					Deposits = savings.ContainsKey(member.MemberNo) ? savings[member.MemberNo] : 0,
					RegFee = regFees.ContainsKey(member.MemberNo) ? regFees[member.MemberNo] : (member.RegFee ?? 0),
					Passbook = passbook.ContainsKey(member.MemberNo) ? passbook[member.MemberNo] : 0,
					Loans = loans.ContainsKey(member.MemberNo) ? loans[member.MemberNo] : 0,
					DateRegistered = member.ApplicDate?.ToString("dd/MM/yyyy") ?? "-",
					Sex = sex
				});
			}

			using var workbook = new XLWorkbook();
			var worksheet = workbook.Worksheets.Add("Shares and Loans Report");
			int currentRow = 1;

			worksheet.Cell(currentRow, 1).Value = companyName.ToUpper();
			worksheet.Range(currentRow, 1, currentRow, 11).Merge();
			worksheet.Cell(currentRow, 1).Style.Font.SetBold().Font.SetFontSize(18);
			worksheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
			currentRow += 2;

			worksheet.Cell(currentRow, 1).Value = $"SHARES AND LOANS REPORT AS AT {reportDate:dd/MM/yyyy}";
			worksheet.Range(currentRow, 1, currentRow, 11).Merge();
			worksheet.Cell(currentRow, 1).Style.Font.SetBold().Font.SetFontSize(14);
			worksheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
			currentRow += 2;

			string[] headers = { "MemberNo", "Names", "Age", "CIGName", "Sex", "Share Capital", "Deposits", "Reg Fee", "Passbook", "Loans", "Date Registered" };
			for (int i = 0; i < headers.Length; i++)
			{
				worksheet.Cell(currentRow, i + 1).Value = headers[i];
				worksheet.Cell(currentRow, i + 1).Style.Font.SetBold();
				worksheet.Cell(currentRow, i + 1).Style.Fill.SetBackgroundColor(XLColor.LightGray);
				worksheet.Cell(currentRow, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
				worksheet.Cell(currentRow, i + 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
			}
			currentRow++;

			foreach (var member in reportData)
			{
				worksheet.Cell(currentRow, 1).Value = member.MemberNo;
				worksheet.Cell(currentRow, 2).Value = member.Names;
				worksheet.Cell(currentRow, 3).Value = member.Age;
				worksheet.Cell(currentRow, 4).Value = member.CIGName;
				worksheet.Cell(currentRow, 5).Value = member.Sex;
				worksheet.Cell(currentRow, 6).Value = member.ShareCapital;
				worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";
				worksheet.Cell(currentRow, 7).Value = member.Deposits;
				worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";
				worksheet.Cell(currentRow, 8).Value = member.RegFee;
				worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0.00";
				worksheet.Cell(currentRow, 9).Value = member.Passbook;
				worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "#,##0.00";
				worksheet.Cell(currentRow, 10).Value = member.Loans;
				worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "#,##0.00";
				worksheet.Cell(currentRow, 11).Value = member.DateRegistered;
				currentRow++;
			}

			currentRow++;
			worksheet.Cell(currentRow, 5).Value = "GRAND TOTAL:";
			worksheet.Cell(currentRow, 5).Style.Font.SetBold();
			worksheet.Cell(currentRow, 6).Value = reportData.Sum(m => (decimal)m.ShareCapital);
			worksheet.Cell(currentRow, 6).Style.Font.SetBold();
			worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0.00";
			worksheet.Cell(currentRow, 7).Value = reportData.Sum(m => (decimal)m.Deposits);
			worksheet.Cell(currentRow, 7).Style.Font.SetBold();
			worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "#,##0.00";
			worksheet.Cell(currentRow, 8).Value = reportData.Sum(m => (decimal)m.RegFee);
			worksheet.Cell(currentRow, 8).Style.Font.SetBold();
			worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0.00";
			worksheet.Cell(currentRow, 9).Value = reportData.Sum(m => (decimal)m.Passbook);
			worksheet.Cell(currentRow, 9).Style.Font.SetBold();
			worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "#,##0.00";
			worksheet.Cell(currentRow, 10).Value = reportData.Sum(m => (decimal)m.Loans);
			worksheet.Cell(currentRow, 10).Style.Font.SetBold();
			worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "#,##0.00";

			worksheet.Columns().AdjustToContents();

			using var stream = new MemoryStream();
			workbook.SaveAs(stream);
			return File(stream.ToArray(),
				"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
				$"SharesAndLoansReport_{reportDate:yyyyMMdd}.xlsx");
		}

		[HttpPost]
		public async Task<IActionResult> ExportSharesAndLoansToPdf(DateTime reportDate)
		{
			var companyCode = User.FindFirstValue("CompanyCode");
			var companyName = User.FindFirstValue("CompanyName") ?? "";

			var members = await _context.Members
				.Where(m => m.CompanyCode == companyCode
					&& (m.Withdrawn == false || m.Withdrawn == null)
					&& (m.Archived == false || m.Archived == null))
				.OrderBy(m => m.MemberNo)
				.ToListAsync();

			var memberNos = members.Select(m => m.MemberNo).ToList();

			var shares = await _context.Shares
				.Where(s => memberNos.Contains(s.MemberNo) && s.CompanyCode == companyCode)
				.GroupBy(s => s.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalShareCapital = g.Sum(s => s.TotalShares ?? 0) })
				.ToDictionaryAsync(s => s.MemberNo, s => s.TotalShareCapital);

			var savings = await _context.Contribs
				.Where(c => memberNos.Contains(c.MemberNo) && c.CompanyCode == companyCode)
				.GroupBy(c => c.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalSavings = g.Sum(c => c.Amount ?? 0) })
				.ToDictionaryAsync(c => c.MemberNo, c => c.TotalSavings);

			var regFees = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo) && cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalRegFee = g.Sum(cs => cs.RegFeeAmount ?? 0) })
				.ToDictionaryAsync(cs => cs.MemberNo, cs => cs.TotalRegFee);

			var loans = await _context.Loans
				.Where(l => memberNos.Contains(l.MemberNo) && l.CompanyCode == companyCode && l.Status == (int)Status.Disbursed)
				.GroupBy(l => l.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalLoans = g.Sum(l => l.LoanAmt ?? 0) })
				.ToDictionaryAsync(l => l.MemberNo, l => l.TotalLoans);

			var passbook = await _context.ContribShares
				.Where(cs => memberNos.Contains(cs.MemberNo) && cs.CompanyCode == companyCode)
				.GroupBy(cs => cs.MemberNo)
				.Select(g => new { MemberNo = g.Key, TotalPassbook = g.Sum(cs => cs.PassBookAmount ?? 0) })
				.ToDictionaryAsync(cs => cs.MemberNo, cs => cs.TotalPassbook);

			var gigCodes = members.Where(m => !string.IsNullOrEmpty(m.Cigcode)).Select(m => m.Cigcode).Distinct().ToList();
			var gigDetails = await _context.CIGs
				.Where(g => gigCodes.Contains(g.GigCode) && g.CompanyCode == companyCode)
				.ToDictionaryAsync(g => g.GigCode, g => g.GigName);

			var reportData = new List<SharesAndLoansReportViewModel>();

			foreach (var member in members)
			{
				string fullName = $"{member.Surname ?? ""} {member.OtherNames ?? ""}".Trim();
				if (string.IsNullOrWhiteSpace(fullName)) fullName = "N/A";

				int? age = null;
				if (member.Dob.HasValue)
				{
					age = DateTime.Now.Year - member.Dob.Value.Year;
					if (DateTime.Now < member.Dob.Value.AddYears(age.Value)) age--;
				}

				string gigName = "UNASSIGNED";
				if (!string.IsNullOrEmpty(member.Cigcode) && gigDetails.ContainsKey(member.Cigcode))
					gigName = gigDetails[member.Cigcode];
				else if (!string.IsNullOrEmpty(member.Cigcode))
					gigName = member.Cigcode;

				reportData.Add(new SharesAndLoansReportViewModel
				{
					MemberNo = member.MemberNo,
					FullName = fullName,
					Age = age,
					CIGName = gigName,
					Sex = member.Sex ?? "Not Specified",
					ShareCapital = shares.ContainsKey(member.MemberNo) ? shares[member.MemberNo] : (member.ShareCap ?? 0),
					Deposits = savings.ContainsKey(member.MemberNo) ? savings[member.MemberNo] : 0,
					RegFee = regFees.ContainsKey(member.MemberNo) ? regFees[member.MemberNo] : (member.RegFee ?? 0),
					Passbook = passbook.ContainsKey(member.MemberNo) ? passbook[member.MemberNo] : 0,
					TotalLoans = loans.ContainsKey(member.MemberNo) ? loans[member.MemberNo] : 0,
					DateRegistered = member.ApplicDate
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
						header.Item().AlignCenter().Text($"SHARES AND LOANS REPORT AS AT {reportDate:dd/MM/yyyy}").FontSize(12).Bold();
						header.Item().AlignCenter().Text($"Generated By: {User.Identity?.Name ?? "System"} On: {DateTime.Now:dd-MMM-yyyy HH:mm}").FontSize(9).Italic();
						header.Item().PaddingTop(0.3f, Unit.Centimetre).LineHorizontal(0.5f);
						header.Item().PaddingBottom(0.5f, Unit.Centimetre);
					});

					page.Content().Table(table =>
					{
						table.ColumnsDefinition(cols =>
						{
							cols.RelativeColumn(1.0f);
							cols.RelativeColumn(1.5f);
							cols.RelativeColumn(0.5f);
							cols.RelativeColumn(1.2f);
							cols.RelativeColumn(0.8f);
							cols.RelativeColumn(1.0f);
							cols.RelativeColumn(1.0f);
							cols.RelativeColumn(1.0f);
							cols.RelativeColumn(1.0f);
							cols.RelativeColumn(1.0f);
							cols.RelativeColumn(1.0f);
						});

						table.Header(header =>
						{
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("MemberNo").Bold().FontSize(8);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Names").Bold().FontSize(8);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Age").Bold().FontSize(8);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("CIGName").Bold().FontSize(8);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Sex").Bold().FontSize(8);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Share Capital").Bold().FontSize(7);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Deposits").Bold().FontSize(7);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Reg Fee").Bold().FontSize(7);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Passbook").Bold().FontSize(7);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Loans").Bold().FontSize(7);
							header.Cell().Border(0.2f).Background("#f0f0f0").Padding(4).AlignCenter().Text("Date Registered").Bold().FontSize(7);
						});

						foreach (var member in reportData)
						{
							table.Cell().Border(0.2f).Padding(4).Text(member.MemberNo ?? "").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).Text(member.FullName ?? "N/A").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).AlignCenter().Text(member.Age?.ToString() ?? "-").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).Text(member.CIGName ?? "Unassigned").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).Text(member.Sex ?? "-").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.ShareCapital:N0}").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.Deposits:N0}").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.RegFee:N0}").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.Passbook:N0}").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).AlignRight().Text($"{member.TotalLoans:N0}").FontSize(7);
							table.Cell().Border(0.2f).Padding(4).AlignCenter().Text(member.DateRegistered?.ToString("dd/MM/yyyy") ?? "-").FontSize(7);
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
			return File(content, "application/pdf", $"SharesAndLoansReport_{reportDate:yyyyMMdd}.pdf");
		}

		#endregion

	}
}