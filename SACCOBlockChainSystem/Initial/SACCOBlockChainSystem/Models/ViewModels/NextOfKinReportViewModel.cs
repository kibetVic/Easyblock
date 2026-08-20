using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class NextOfKinReportViewModel
    {
        // Company Information
        public string CompanyName { get; set; }
        public string CompanyCode { get; set; }
        public string CompanyAddress { get; set; }
        public string CompanyPhone { get; set; }
        public string CompanyEmail { get; set; }

        // Report Information
        public DateTime GeneratedDate { get; set; }
        public string GeneratedBy { get; set; }
        public string ReportTitle { get; set; }

        // All Members with their Next of Kin
        public List<MemberWithNextOfKinDTO> MembersWithNextOfKin { get; set; }

        // Summary Statistics
        public ReportSummaryDTO Summary { get; set; }
    }

    public class MemberWithNextOfKinDTO
    {
        // Member Details
        public string MemberNo { get; set; }
        public string FullName { get; set; }
        public string Surname { get; set; }
        public string OtherNames { get; set; }
        public string IdNumber { get; set; }
        public string PhoneNo { get; set; }
        public string Email { get; set; }
        public string PhysicalAddress { get; set; }
        public string Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public int? Age { get; set; }
        public string MembershipType { get; set; }
        public string RegistrationType { get; set; }
        public DateTime? RegistrationDate { get; set; }
        public string Status { get; set; }
        public decimal? ShareCapital { get; set; }
        public string CIGGroup { get; set; }
        public string Department { get; set; }
        public string Station { get; set; }

        // Next of Kin List for this member
        public List<NextOfKinReportDTO> NextOfKeens { get; set; }

        // Member Summary
        public int TotalNextOfKeens => NextOfKeens?.Count ?? 0;
        public decimal TotalBenefitPercentage => NextOfKeens?.Sum(n => n.BenefitPercentage ?? 0) ?? 0;
        public bool HasValidBenefit => TotalBenefitPercentage <= 100;
    }

    public class NextOfKinReportDTO
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Relationship { get; set; }
        public string PhoneNo { get; set; }
        public string Email { get; set; }
        public string PhysicalAddress { get; set; }
        public string IdNumber { get; set; }
        public string PassportNumber { get; set; }
        public string Employer { get; set; }
        public string Occupation { get; set; }
        public decimal? BenefitPercentage { get; set; }
        public int? PriorityOrder { get; set; }
        public bool IsPrimary { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
    }

    public class ReportSummaryDTO
    {
        public int TotalMembers { get; set; }
        public int TotalNextOfKeens { get; set; }
        public int MembersWithCompleteBenefit { get; set; }
        public int MembersWithInvalidBenefit { get; set; }
        public int MembersWithNoNextOfKin { get; set; }
        public decimal AverageBenefitPercentage { get; set; }

        // Read-only computed properties
        public bool IsBenefitValid => TotalNextOfKeens > 0;
        public decimal OverallCompletionRate => TotalMembers > 0 ? (decimal)MembersWithCompleteBenefit / TotalMembers * 100 : 0;
    }
}