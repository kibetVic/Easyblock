using System;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class MemberDTO
    {
        public string MemberNo { get; set; } = null!;
        public string Surname { get; set; } = null!;
        public string OtherNames { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Idno { get; set; } = null!;
        public string? PhoneNo { get; set; }
        public string? LandLine { get; set; }
        public string? Email { get; set; }
        public string? OfficePhone { get; set; }
        public string? HomePhone { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public int? Age { get; set; }
        public string? MaritalStatus { get; set; }
        public string? Employer { get; set; }
        public string? Department { get; set; }
        public string? Station { get; set; }
        public string? PresentAddress { get; set; }
        public string? HomeAddress { get; set; }

        // Company Information
        public string CompanyCode { get; set; } = null!;
        public string? GroupCig { get; set; }

        // Membership Details
        public string? MembershipType { get; set; } // Individual / Corporate
        public string? RegistrationType { get; set; } // Board Member / Ordinary Member
        public string Status { get; set; } = null!;
        public DateTime? DateJoined { get; set; }

        // Financial Balances
        public decimal CurrentBalance { get; set; }
        public decimal ShareBalance { get; set; }
        public decimal LoanBalance { get; set; }
        public decimal TotalBalance { get; set; }

        // Status Flags
        public bool IsActive { get; set; }
        public bool IsDormant { get; set; }

        // Additional Fields
        public DateTime? LastTransactionDate { get; set; }
        public string? ProfilePicture { get; set; }
        public string? BlockchainTxId { get; set; }
        public int TotalTransactions { get; set; }
    }

    public class MemberRegistrationDTO
    {
        // Auto-generated but editable
        [Display(Name = "Member Number")]
        public string? MemberNo { get; set; }

        [Required(ErrorMessage = "Surname is required")]
        [Display(Name = "Surname")]
        public string Surname { get; set; } = null!;

        [Required(ErrorMessage = "Other names are required")]
        [Display(Name = "Other Names")]
        public string OtherNames { get; set; } = null!;

        [Required(ErrorMessage = "ID Number is required")]
        [Display(Name = "ID Number")]
        public string IdNo { get; set; } = null!;

        [Phone(ErrorMessage = "Invalid phone number")]
        [Display(Name = "Phone Number")]
        public string? PhoneNo { get; set; }

        [Phone(ErrorMessage = "Invalid landline number")]
        [Display(Name = "Landline")]
        public string? LandLine { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address")]
        [Display(Name = "Email Address")]
        public string? Email { get; set; }

        [Display(Name = "Gender")]
        public string? Gender { get; set; }

        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [Display(Name = "Age")]
        public int? Age { get; set; } // Calculated from DOB

        [Display(Name = "Station/County")]
        public string? Station { get; set; }

        [Display(Name = "Department")]
        public string? Department { get; set; }

        [Display(Name = "Present Address")]
        public string? PresentAddress { get; set; }

        [Display(Name = "Company Code")]
        public string? CompanyCode { get; set; } // Read-only

        [Display(Name = "Group CIG")]
        public string? Cigcode { get; set; } // Select CIG for each member

        [Display(Name = "Membership Type")]
        public string? MembershipType { get; set; } // Individual / Corporate - Checkbox

        [Display(Name = "Registration Type")]
        public string? RegistrationType { get; set; } // Board Member / Ordinary Member - Checkbox

        [Display(Name = "Initial Shares")]
        [Range(0, double.MaxValue, ErrorMessage = "Initial shares must be a positive number")]
        public decimal InitialShares { get; set; } = 0;

        // System Fields
        public string? CreatedBy { get; set; }
        public string? MaritalStatus { get; set; }
        public string? Status { get; set; }
        public DateTime RegistrationDate { get; set; } = DateTime.Now;
    }

    public class MemberResponseDTO
    {
        public string MemberNo { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime RegistrationDate { get; set; }
        public string? BlockchainTxId { get; set; }
        public decimal ShareBalance { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? CompanyCode { get; set; }
        public string? MembershipType { get; set; }
        public string? RegistrationType { get; set; }
        public bool IsActive { get; set; }
    }

    public class MemberUpdateDTO
    {
        public string? PhoneNo { get; set; }
        public string? IdNo { get; set; }
        public string? CompanyCode { get; set; }
        public string? Surname { get; set; }
        public string? OtherNames { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Age { get; set; }
        public string? Cigcode { get; set; }
        public string? LandLine { get; set; }
        public string? Email { get; set; }
        public string? PresentAddress { get; set; }
        public string? HomeAddress { get; set; }
        public string? Employer { get; set; }
        public string? Department { get; set; }
        public string? Station { get; set; }
        public string? MembershipType { get; set; }
        public string? RegistrationType { get; set; }
        public string? MaritalStatus { get; set; }
        public string? GroupCig { get; set; }
        public string? UpdatedBy { get; set; }
        public string? CreatedBy { get; set; }
        public string? Status { get; set; }
        public DateTime RegistrationDate { get; set; }
    }

    public class MemberSummaryDTO
    {
        public string MemberNo { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string IdNumber { get; set; } = null!;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? MembershipType { get; set; }
        public string? RegistrationType { get; set; }
        public decimal ShareBalance { get; set; }
        public decimal LoanBalance { get; set; }
        public decimal TotalBalance { get; set; }
        public DateTime? MemberSince { get; set; }
        public string Status { get; set; } = null!;
        public string? Station { get; set; }
        public string? Department { get; set; }
        public int TotalTransactions { get; set; }
        public DateTime? LastTransactionDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsDormant { get; set; }
    }

    public class MemberTransactionSummary
    {
        public string TransactionType { get; set; } = null!;
        public int Count { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime? LastTransactionDate { get; set; }
    }

    public class MemberFilterDTO
    {
        public string? SearchTerm { get; set; }
        public string? Status { get; set; }
        public string? MembershipType { get; set; }
        public string? RegistrationType { get; set; }
        public string? CompanyCode { get; set; }
        public string? GroupCig { get; set; }
        public string? Department { get; set; }
        public string? Station { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsDormant { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class MemberDropdownDTO
    {
        public string MemberNo { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? PhoneNo { get; set; }
        public string? Email { get; set; }
    }

    public class MemberBulkImportDTO
    {
        public string CompanyCode { get; set; } = null!;
        public string? GroupCig { get; set; }
        public List<MemberImportRow> Members { get; set; } = new();
        public string? CreatedBy { get; set; }
    }

    public class MemberImportRow
    {
        public string Surname { get; set; } = null!;
        public string OtherNames { get; set; } = null!;
        public string IdNo { get; set; } = null!;
        public string? PhoneNo { get; set; }
        public string? LandLine { get; set; }
        public string? Email { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Employer { get; set; }
        public string? Department { get; set; }
        public string? Station { get; set; }
        public string? MembershipType { get; set; }
        public string? RegistrationType { get; set; }
        public decimal InitialShares { get; set; }
    }
}