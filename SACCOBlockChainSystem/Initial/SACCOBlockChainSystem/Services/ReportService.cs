using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using System.Drawing;

namespace SACCOBlockChainSystem.Services
{
    public interface IReportService
    {
        Task<NextOfKinReportViewModel> GetAllMembersNextOfKinReportAsync(string companyCode);
        byte[] GenerateExcelReport(NextOfKinReportViewModel reportData);
        byte[] GeneratePdfReport(NextOfKinReportViewModel reportData);
    }

    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<ReportService> _logger;

        public ReportService(
            ApplicationDbContext context,
            ICompanyContextService companyContextService,
            ILogger<ReportService> logger)
        {
            _context = context;
            _companyContextService = companyContextService;
            _logger = logger;

            // Initialize QuestPDF
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task<NextOfKinReportViewModel> GetAllMembersNextOfKinReportAsync(string companyCode)
        {
            try
            {
                // Get all active members for this company
                var members = await _context.Members
                    .Where(m => m.CompanyCode == companyCode && m.Status == 1 && m.Archived != true)
                    .OrderBy(m => m.MemberNo)
                    .ToListAsync();

                // Get all next of kin records for these members
                var memberNos = members.Select(m => m.MemberNo).ToList();
                var allNextOfKeens = await _context.NextOfKeens
                    .Where(n => memberNos.Contains(n.MemberNo) &&
                                n.CompanyCode == companyCode &&
                                n.Status == "Active")
                    .OrderBy(n => n.PriorityOrder)
                    .ToListAsync();

                // Get company details
                var company = await _context.Companies
                    .FirstOrDefaultAsync(c => c.CompanyCode == companyCode);

                var membersWithNextOfKin = new List<MemberWithNextOfKinDTO>();
                int membersWithCompleteBenefit = 0;
                int membersWithInvalidBenefit = 0;
                int membersWithNoNextOfKin = 0;
                decimal totalBenefitSum = 0;

                foreach (var member in members)
                {
                    var memberNextOfKeens = allNextOfKeens
                        .Where(n => n.MemberNo == member.MemberNo)
                        .ToList();

                    string cigGroupName = null;
                    if (!string.IsNullOrEmpty(member.Cigcode))
                    {
                        var cig = await _context.CIGs
                            .FirstOrDefaultAsync(c => c.GigCode == member.Cigcode && c.CompanyCode == companyCode);
                        cigGroupName = cig?.GigName;
                    }

                    var totalBenefit = memberNextOfKeens.Sum(n => n.BenefitPercentage ?? 0);
                    var hasValidBenefit = totalBenefit <= 100;

                    if (memberNextOfKeens.Count == 0)
                    {
                        membersWithNoNextOfKin++;
                    }
                    else if (hasValidBenefit)
                    {
                        membersWithCompleteBenefit++;
                    }
                    else
                    {
                        membersWithInvalidBenefit++;
                    }

                    totalBenefitSum += totalBenefit;

                    membersWithNextOfKin.Add(new MemberWithNextOfKinDTO
                    {
                        MemberNo = member.MemberNo,
                        FullName = $"{member.Surname} {member.OtherNames}".Trim(),
                        Surname = member.Surname,
                        OtherNames = member.OtherNames,
                        IdNumber = member.Idno,
                        PhoneNo = member.PhoneNo,
                        Email = member.Email,
                        PhysicalAddress = member.PresentAddr,
                        Gender = member.Sex,
                        DateOfBirth = member.Dob,
                        Age = member.Age,
                        MembershipType = member.MembershipType,
                        RegistrationType = member.MemberDescription,
                        RegistrationDate = member.ApplicDate,
                        Status = member.Status == 1 ? "Active" : "Inactive",
                        ShareCapital = member.ShareCap,
                        CIGGroup = cigGroupName ?? member.Cigcode,
                        Department = member.Dept,
                        Station = member.Station,
                        NextOfKeens = memberNextOfKeens.Select(n => new NextOfKinReportDTO
                        {
                            Id = n.Id,
                            FullName = n.FullName,
                            Relationship = n.Relationship,
                            PhoneNo = n.PhoneNo,
                            Email = n.Email,
                            PhysicalAddress = n.PhysicalAddress,
                            IdNumber = n.IdNumber,
                            PassportNumber = n.PassportNumber,
                            Employer = n.Employer,
                            Occupation = n.Occupation,
                            BenefitPercentage = n.BenefitPercentage,
                            PriorityOrder = n.PriorityOrder,
                            IsPrimary = n.IsPrimary,
                            Status = n.Status,
                            Notes = n.Notes
                        }).ToList()
                    });
                }

                var reportData = new NextOfKinReportViewModel
                {
                    CompanyName = company?.CompanyName ?? "SACCO",
                    CompanyCode = companyCode,
                    CompanyAddress = company?.Address ?? "N/A",
                    CompanyPhone = company?.Telephone ?? "N/A",
                    CompanyEmail = company?.Email ?? "N/A",
                    GeneratedDate = DateTime.Now,
                    GeneratedBy = _companyContextService.GetCurrentUserName() ?? "System",
                    ReportTitle = "All Members - Next of Kin Report",
                    MembersWithNextOfKin = membersWithNextOfKin,
                    Summary = new ReportSummaryDTO
                    {
                        TotalMembers = members.Count,
                        TotalNextOfKeens = allNextOfKeens.Count,
                        MembersWithCompleteBenefit = membersWithCompleteBenefit,
                        MembersWithInvalidBenefit = membersWithInvalidBenefit,
                        MembersWithNoNextOfKin = membersWithNoNextOfKin,
                        AverageBenefitPercentage = members.Count > 0 ? totalBenefitSum / members.Count : 0
                    }
                };

                return reportData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating all members report");
                throw;
            }
        }

        public byte[] GenerateExcelReport(NextOfKinReportViewModel reportData)
        {
            using (var workbook = new XLWorkbook())
            {
                // Create worksheet
                var worksheet = workbook.Worksheets.Add("Next of Kin Report");

                // Set company header
                worksheet.Cell("A1").Value = reportData.CompanyName;
                worksheet.Cell("A1").Style.Font.FontSize = 18;
                worksheet.Cell("A1").Style.Font.Bold = true;

                worksheet.Cell("A2").Value = reportData.CompanyAddress;
                worksheet.Cell("A3").Value = $"Tel: {reportData.CompanyPhone} | Email: {reportData.CompanyEmail}";
                worksheet.Cell("A4").Value = $"Report Generated: {reportData.GeneratedDate:dd/MM/yyyy HH:mm:ss}";
                worksheet.Cell("A5").Value = $"Generated By: {reportData.GeneratedBy}";

                // Report Title
                worksheet.Cell("A7").Value = reportData.ReportTitle;
                worksheet.Cell("A7").Style.Font.FontSize = 14;
                worksheet.Cell("A7").Style.Font.Bold = true;

                // Summary Section
                int row = 9;
                worksheet.Cell($"A{row}").Value = "SUMMARY";
                worksheet.Range($"A{row}:H{row}").Merge();
                worksheet.Cell($"A{row}").Style.Font.Bold = true;
                worksheet.Cell($"A{row}").Style.Fill.BackgroundColor = XLColor.LightGray;

                row += 2;
                worksheet.Cell($"A{row}").Value = "Total Members:";
                worksheet.Cell($"A{row}").Style.Font.Bold = true;
                worksheet.Cell($"B{row}").Value = reportData.Summary.TotalMembers;

                worksheet.Cell($"C{row}").Value = "Total Next of Kin:";
                worksheet.Cell($"C{row}").Style.Font.Bold = true;
                worksheet.Cell($"D{row}").Value = reportData.Summary.TotalNextOfKeens;

                row++;
                worksheet.Cell($"A{row}").Value = "Members with Complete Benefit (≤100%):";
                worksheet.Cell($"A{row}").Style.Font.Bold = true;
                worksheet.Cell($"B{row}").Value = reportData.Summary.MembersWithCompleteBenefit;

                worksheet.Cell($"C{row}").Value = "Members with Invalid Benefit (>100%):";
                worksheet.Cell($"C{row}").Style.Font.Bold = true;
                worksheet.Cell($"D{row}").Value = reportData.Summary.MembersWithInvalidBenefit;

                row++;
                worksheet.Cell($"A{row}").Value = "Members with No Next of Kin:";
                worksheet.Cell($"A{row}").Style.Font.Bold = true;
                worksheet.Cell($"B{row}").Value = reportData.Summary.MembersWithNoNextOfKin;

                worksheet.Cell($"C{row}").Value = "Average Benefit Percentage:";
                worksheet.Cell($"C{row}").Style.Font.Bold = true;
                worksheet.Cell($"D{row}").Value = $"{reportData.Summary.AverageBenefitPercentage:F2}%";

                row += 3;

                // Column Headers
                string[] headers = { "Member No", "Member Name", "ID Number", "Phone", "Status", "Total NOK", "Benefit %", "NOK Name", "Relationship", "NOK Phone", "Benefit %", "Is Primary" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(row, i + 1).Value = headers[i];
                    worksheet.Cell(row, i + 1).Style.Font.Bold = true;
                    worksheet.Cell(row, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
                    worksheet.Cell(row, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

                row++;

                // Data rows
                foreach (var member in reportData.MembersWithNextOfKin)
                {
                    if (member.NextOfKeens.Any())
                    {
                        foreach (var nok in member.NextOfKeens)
                        {
                            worksheet.Cell(row, 1).Value = member.MemberNo;
                            worksheet.Cell(row, 2).Value = member.FullName;
                            worksheet.Cell(row, 3).Value = member.IdNumber ?? "-";
                            worksheet.Cell(row, 4).Value = member.PhoneNo ?? "-";
                            worksheet.Cell(row, 5).Value = member.Status;
                            worksheet.Cell(row, 6).Value = member.TotalNextOfKeens;
                            worksheet.Cell(row, 7).Value = $"{member.TotalBenefitPercentage:F2}%";
                            worksheet.Cell(row, 8).Value = nok.FullName;
                            worksheet.Cell(row, 9).Value = nok.Relationship;
                            worksheet.Cell(row, 10).Value = nok.PhoneNo;
                            worksheet.Cell(row, 11).Value = $"{nok.BenefitPercentage:F2}%";
                            worksheet.Cell(row, 12).Value = nok.IsPrimary ? "Yes" : "No";

                            if (!member.HasValidBenefit)
                            {
                                worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.LightPink;
                            }

                            row++;
                        }
                    }
                    else
                    {
                        worksheet.Cell(row, 1).Value = member.MemberNo;
                        worksheet.Cell(row, 2).Value = member.FullName;
                        worksheet.Cell(row, 3).Value = member.IdNumber ?? "-";
                        worksheet.Cell(row, 4).Value = member.PhoneNo ?? "-";
                        worksheet.Cell(row, 5).Value = member.Status;
                        worksheet.Cell(row, 6).Value = 0;
                        worksheet.Cell(row, 7).Value = "0%";
                        worksheet.Cell(row, 8).Value = "No next of kin recorded";
                        worksheet.Cell(row, 9).Value = "-";
                        worksheet.Cell(row, 10).Value = "-";
                        worksheet.Cell(row, 11).Value = "-";
                        worksheet.Cell(row, 12).Value = "-";
                        row++;
                    }
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }

        public byte[] GeneratePdfReport(NextOfKinReportViewModel reportData)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(1.5f, Unit.Centimetre);
                    page.MarginRight(1.5f, Unit.Centimetre);
                    page.MarginTop(1.5f, Unit.Centimetre);
                    page.MarginBottom(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    // Header
                    page.Header()
                        .Column(headerColumn =>
                        {
                            headerColumn.Item().AlignCenter().Text(reportData.CompanyName)
                                .FontSize(18)
                                .Bold()
                                .FontColor(Colors.Blue.Darken2);

                            headerColumn.Item().AlignCenter().Text(reportData.CompanyAddress)
                                .FontSize(8)
                                .FontColor(Colors.Grey.Darken1);

                            headerColumn.Item().AlignCenter().Text($"Tel: {reportData.CompanyPhone} | Email: {reportData.CompanyEmail}")
                                .FontSize(8)
                                .FontColor(Colors.Grey.Darken1);

                            headerColumn.Item().PaddingTop(3).AlignCenter().LineHorizontal(0.5f);

                            headerColumn.Item().PaddingTop(3).AlignCenter().Text($"Report Generated: {reportData.GeneratedDate:dd/MM/yyyy HH:mm:ss}")
                                .FontSize(7)
                                .FontColor(Colors.Grey.Darken2);

                            headerColumn.Item().AlignCenter().Text($"Generated By: {reportData.GeneratedBy}")
                                .FontSize(7)
                                .FontColor(Colors.Grey.Darken2);

                            headerColumn.Item().PaddingTop(3).AlignCenter().Text(reportData.ReportTitle)
                                .FontSize(12)
                                .Bold()
                                .FontColor(Colors.Blue.Medium);

                            headerColumn.Item().PaddingTop(2).AlignCenter().LineHorizontal(0.5f);
                        });

                    // Content
                    page.Content()
                        .PaddingVertical(0.3f, Unit.Centimetre)
                        .Column(column =>
                        {
                            // Summary Section
                            column.Item().PaddingBottom(8).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Table(summaryTable =>
                            {
                                summaryTable.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                });

                                summaryTable.Header(header =>
                                {
                                    header.Cell().ColumnSpan(4).Background(Colors.Blue.Lighten4)
                                        .Padding(3).Text("SUMMARY STATISTICS")
                                        .Bold().FontSize(10).FontColor(Colors.Blue.Darken2).AlignCenter();
                                });

                                // Row 1
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text("Total Members:").Bold().FontSize(8);
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(reportData.Summary.TotalMembers.ToString()).FontSize(8);
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text("Total Next of Kin:").Bold().FontSize(8);
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(reportData.Summary.TotalNextOfKeens.ToString()).FontSize(8);

                                // Row 2
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text("Complete Benefit (≤100%):").Bold().FontSize(8);
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(reportData.Summary.MembersWithCompleteBenefit.ToString()).FontSize(8)
                                    .FontColor(Colors.Green.Medium);
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text("Invalid Benefit (>100%):").Bold().FontSize(8);
                                summaryTable.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                    .Text(reportData.Summary.MembersWithInvalidBenefit.ToString()).FontSize(8)
                                    .FontColor(Colors.Red.Medium);

                                // Row 3
                                summaryTable.Cell().Padding(3)
                                    .Text("No Next of Kin:").Bold().FontSize(8);
                                summaryTable.Cell().Padding(3)
                                    .Text(reportData.Summary.MembersWithNoNextOfKin.ToString()).FontSize(8)
                                    .FontColor(Colors.Orange.Medium);
                                summaryTable.Cell().Padding(3)
                                    .Text("Average Benefit:").Bold().FontSize(8);
                                summaryTable.Cell().Padding(3)
                                    .Text($"{reportData.Summary.AverageBenefitPercentage:F2}%").FontSize(8);
                            });

                            // Members Section - Each member separately
                            foreach (var member in reportData.MembersWithNextOfKin)
                            {
                                var isInvalid = !member.HasValidBenefit;
                                var borderColor = isInvalid ? Colors.Red.Lighten2 : Colors.Grey.Lighten2;

                                // Member Card
                                column.Item().PaddingTop(8).Border(0.5f).BorderColor(borderColor).Padding(6).Column(memberColumn =>
                                {
                                    // Member Header
                                    memberColumn.Item().Background(isInvalid ? Colors.Red.Lighten4 : Colors.Blue.Lighten4)
                                        .Padding(4).Row(memberHeader =>
                                        {
                                            memberHeader.RelativeItem().Text($"MEMBER: {member.MemberNo} - {member.FullName}")
                                                .FontSize(10).Bold().FontColor(Colors.Blue.Darken2);
                                            memberHeader.RelativeItem().AlignRight().Text($"Status: {member.Status}")
                                                .FontSize(9).Bold().FontColor(isInvalid ? Colors.Red.Medium : Colors.Green.Medium);
                                        });

                                    // Member Details
                                    memberColumn.Item().PaddingTop(5).Row(details =>
                                    {
                                        details.RelativeItem().Column(detailCol =>
                                        {
                                            detailCol.Item().Text($"Full Name: {member.FullName}").FontSize(8);
                                            detailCol.Item().Text($"ID Number: {member.IdNumber ?? "N/A"}").FontSize(8);
                                            detailCol.Item().Text($"Phone: {member.PhoneNo ?? "N/A"}").FontSize(8);
                                            detailCol.Item().Text($"Email: {member.Email ?? "N/A"}").FontSize(8);
                                        });

                                        details.RelativeItem().Column(detailCol =>
                                        {
                                            detailCol.Item().Text($"Membership Type: {member.MembershipType ?? "N/A"}").FontSize(8);
                                            detailCol.Item().Text($"Registration Date: {member.RegistrationDate:dd/MM/yyyy}").FontSize(8);
                                            detailCol.Item().Text($"CIG Group: {member.CIGGroup ?? "N/A"}").FontSize(8);
                                            detailCol.Item().Text($"Share Capital: {member.ShareCapital?.ToString("N2") ?? "0.00"}").FontSize(8);
                                        });

                                        details.RelativeItem().Column(detailCol =>
                                        {
                                            detailCol.Item().Text($"Total Next of Kin: {member.TotalNextOfKeens}").FontSize(8);
                                            detailCol.Item().Text($"Total Benefit: {member.TotalBenefitPercentage:F2}%").FontSize(8)
                                                .FontColor(isInvalid ? Colors.Red.Medium : Colors.Black);
                                            if (!isInvalid && member.TotalBenefitPercentage > 0)
                                            {
                                                detailCol.Item().Text($"Remaining: {(100 - member.TotalBenefitPercentage):F2}%").FontSize(8)
                                                    .FontColor(Colors.Green.Medium);
                                            }
                                        });
                                    });

                                    // Next of Kin Table Header
                                    if (member.NextOfKeens.Any())
                                    {
                                        memberColumn.Item().PaddingTop(6).Text("NEXT OF KIN / BENEFICIARIES")
                                            .FontSize(9).Bold().FontColor(Colors.Blue.Darken2);

                                        memberColumn.Item().PaddingTop(3).Table(nokTable =>
                                        {
                                            nokTable.ColumnsDefinition(columns =>
                                            {
                                                columns.ConstantColumn(20);   // No
                                                columns.RelativeColumn(80);    // Full Name (larger relative width)
                                                columns.ConstantColumn(35);    // Relationship
                                                columns.ConstantColumn(35);    // Phone
                                                columns.ConstantColumn(35);    // ID Number
                                                columns.ConstantColumn(25);    // Benefit %
                                                columns.ConstantColumn(22);    // Primary
                                            });

                                            // Header
                                            nokTable.Header(header =>
                                            {
                                                header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text("#").FontSize(7).Bold().AlignCenter();
                                                header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text("Full Name").FontSize(7).Bold();
                                                header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text("Relationship").FontSize(7).Bold();
                                                header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text("Phone").FontSize(7).Bold();
                                                header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text("ID Number").FontSize(7).Bold();
                                                header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text("Benefit %").FontSize(7).Bold().AlignCenter();
                                                header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text("Primary").FontSize(7).Bold().AlignCenter();
                                            });

                                            // Rows
                                            int nokNo = 1;
                                            foreach (var nok in member.NextOfKeens)
                                            {
                                                var rowBg = nokNo % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;

                                                nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text(nokNo.ToString()).FontSize(7).AlignCenter();
                                                nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text(nok.FullName.Length > 25 ? nok.FullName.Substring(0, 22) + "..." : nok.FullName).FontSize(7);
                                                nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text(nok.Relationship.Length > 12 ? nok.Relationship.Substring(0, 10) + ".." : nok.Relationship).FontSize(7);
                                                nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text(nok.PhoneNo).FontSize(7);
                                                nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text(nok.IdNumber?.Length > 12 ? nok.IdNumber.Substring(0, 10) + ".." : nok.IdNumber ?? "-").FontSize(7);
                                                nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text($"{nok.BenefitPercentage:F0}%").FontSize(7).AlignCenter();
                                                nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(1)
                                                    .Text(nok.IsPrimary ? "Yes" : "No").FontSize(7).AlignCenter();

                                                nokNo++;
                                            }
                                        });
                                    }

                                    //// Next of Kin Table Header
                                    //if (member.NextOfKeens.Any())
                                    //{
                                    //    memberColumn.Item().PaddingTop(6).Text("NEXT OF KIN / BENEFICIARIES")
                                    //        .FontSize(9).Bold().FontColor(Colors.Blue.Darken2);

                                    //    memberColumn.Item().PaddingTop(3).Table(nokTable =>
                                    //    {
                                    //        nokTable.ColumnsDefinition(columns =>
                                    //        {
                                    //            columns.ConstantColumn(25);   // No
                                    //            columns.RelativeColumn(50);    // Full Name
                                    //            columns.ConstantColumn(40);    // Relationship
                                    //            columns.ConstantColumn(38);    // Phone
                                    //            columns.ConstantColumn(45);    // ID Number
                                    //            columns.ConstantColumn(32);    // Benefit %
                                    //            columns.ConstantColumn(28);    // Primary
                                    //        });

                                    //        // Header
                                    //        nokTable.Header(header =>
                                    //        {
                                    //            header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text("#").FontSize(7).Bold().AlignCenter();
                                    //            header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text("Full Name").FontSize(7).Bold();
                                    //            header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text("Relationship").FontSize(7).Bold();
                                    //            header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text("Phone").FontSize(7).Bold();
                                    //            header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text("ID Number").FontSize(7).Bold();
                                    //            header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text("Benefit %").FontSize(7).Bold().AlignRight();
                                    //            header.Cell().Background(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text("Primary").FontSize(7).Bold().AlignCenter();
                                    //        });

                                    //        // Rows
                                    //        int nokNo = 1;
                                    //        foreach (var nok in member.NextOfKeens)
                                    //        {
                                    //            var rowBg = nokNo % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;

                                    //            nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text(nokNo.ToString()).FontSize(7).AlignCenter();
                                    //            nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text(nok.FullName).FontSize(7);
                                    //            nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text(nok.Relationship).FontSize(7);
                                    //            nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text(nok.PhoneNo).FontSize(7);
                                    //            nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text(nok.IdNumber ?? "-").FontSize(7);
                                    //            nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text($"{nok.BenefitPercentage:F1}%").FontSize(7).AlignRight();
                                    //            nokTable.Cell().Background(rowBg).PaddingVertical(2).PaddingHorizontal(2)
                                    //                .Text(nok.IsPrimary ? "Yes" : "No").FontSize(7).AlignCenter();

                                    //            nokNo++;
                                    //        }
                                    //    });
                                    //}
                                    else
                                    {
                                        memberColumn.Item().PaddingTop(6).Padding(4).Background(Colors.Grey.Lighten3)
                                            .Text("No next of kin / beneficiaries recorded for this member")
                                            .FontSize(8).Italic().FontColor(Colors.Grey.Darken2).AlignCenter();
                                    }
                                });
                            }
                        });
                });
            });

            return document.GeneratePdf();
        }
    }
}